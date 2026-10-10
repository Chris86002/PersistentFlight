using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using PersistentFlight;
using PersistentFlight.Regions;
[assembly: KSPAssembly("PersistentFlight.InterruptionTests",0,21)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class InterruptionHarness:MonoBehaviour {
 string result;int phase;float deadline;double stamp,fuel;ManeuverNode node;Guid report;Part cpuPart,enginePart,coupledRoot;DockedVesselInfo dockInfo;double[] power;
 void Awake(){DontDestroyOnLoad(this);}
 void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-INTERRUPTION-TEST] "+s);}
 void Check(bool b,string s){if(!b)throw new Exception(s);}
 IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-interruption-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+650;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}}
 ModuleRegionalAutonomy Pilot(){return cpuPart==null?null:cpuPart.Modules.OfType<ModuleRegionalAutonomy>().FirstOrDefault();}
 double Fuel(Vessel v){double c;return MissionControl.Resource(v,"LiquidFuel",out c);}
 void NewNode(ModuleRegionalAutonomy m,double dv){
  Check(RegionFlightRunner.Instance.Focus(m.vessel)||FlightGlobals.ActiveVessel==m.vessel,"Focus failed");MapView.EnterMapView(); foreach(var old in m.vessel.patchedConicSolver.maneuverNodes.ToArray()) old.RemoveSelf();
  node=m.vessel.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime()+35);node.OnGizmoUpdated(new Vector3d(0,0,dv),node.UT);m.vessel.patchedConicSolver.UpdateFlightPlan();stamp=Planetarium.GetUniversalTime();
 }
 void Queue(ModuleRegionalAutonomy m){MissionControl.QueueNext(m.vessel,m);MapView.ExitMapView();m.abortOnCritical=false;}
 void Held(ModuleRegionalAutonomy m){
  Check(m.missionHold&&m.nodeRequested&&m.interruptionReason!="","No retained safety hold");Check(m.vessel.ctrlState.mainThrottle==0,"Held throttle is nonzero");
  Check(m.vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).All(e=>!e.EngineIgnited&&e.finalThrust<.01),"Held engine stayed active");
  bool denied=false;try{MissionIntegrity.Release(m);}catch(InvalidOperationException){denied=true;}Check(denied,"Unsafe resume accepted");
 }
 void Cancel(ModuleRegionalAutonomy m){m.CancelNode("Interruption fixture reviewed");if(node!=null&&node.patch!=null)node.RemoveSelf();node=null;Check(!m.nodeRequested&&m.interruptionReason==""&&!m.missionHold,"Cancel did not release interrupted plan");}
 void Update(){if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
  try{
   Check(Time.realtimeSinceStartup<deadline,"Interruption timeout phase "+phase);double now=Planetarium.GetUniversalTime();
   var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");
   if(phase==0&&craft!=null&&craft.loaded&&!craft.packed&&MissionControl.Pilot(craft)!=null&&now>10008){cpuPart=MissionControl.Pilot(craft).part;NewNode(Pilot(),50);phase=1;}
   else if(phase==1&&node.nextPatch!=null&&now-stamp>2){Queue(Pilot());phase=2;}
   else if(phase==2){var m=Pilot();if(m.burnInProgress&&m.vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Any(e=>e.finalThrust>1)){
    fuel=Fuel(m.vessel);report=Guid.Parse(m.debriefId);GamePersistence.SaveGame("mid-burn",HighLogic.SaveFolder,SaveMode.OVERWRITE);
    var raw=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,"mid-burn.sfs"));Check(raw.GetNode("GAME").GetNode("FLIGHTSTATE").GetNodes("VESSEL").SelectMany(v=>v.GetNodes("PART")).SelectMany(p=>p.GetNodes("MODULE")).Any(n=>n.GetValue("name")=="ModuleRegionalAutonomy"&&n.GetValue("burnInProgress")=="True"),"Powered state not persisted");
    var game=GamePersistence.LoadGame("mid-burn",HighLogic.SaveFolder,true,false);int index=game.flightState.protoVessels.FindIndex(v=>v.vesselID==m.vessel.id);FlightDriver.StartAndFocusVessel(game,index);cpuPart=null;node=null;phase=3;stamp=now;Note("PASS real kOS powered burn saved and cold native reload requested");
   }}
   else if(phase==3&&craft!=null&&craft.loaded&&!craft.packed&&now-stamp>5){
    cpuPart=MissionControl.Modules(craft).First().part;var m=Pilot();Held(m);Check(m.interruptionReason.Contains("Saved during"),"Reload cause missing");Check(Math.Abs(Fuel(craft)-fuel)<.001,"Restored held burn consumed fuel");Check(PersistentFlightScenario.Instance.Debriefs.Find(report).Incomplete,"Reload debrief not marked partial");
    Cancel(m);NewNode(m,50);phase=4;Note("PASS actual mid-burn reload retains plan, shuts engines, preserves fuel, marks partial debrief and refuses resume");
   }
   else if(phase==4&&node.nextPatch!=null&&now-stamp>2&&Pilot().PilotOnline){Queue(Pilot());phase=5;}
   else if(phase==5){var m=Pilot();if(m.burnInProgress&&m.vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Any(e=>e.finalThrust>1)){
    var ec=m.vessel.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="ElectricCharge").ToArray();power=ec.Select(r=>r.amount).ToArray();foreach(var r in ec)r.amount=0;phase=6;stamp=now;
   }}
   else if(phase==6&&now-stamp>2){var m=Pilot();Held(m);Check(m.interruptionReason.Contains("CPU power"),"CPU power cause missing");var ec=m.vessel.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="ElectricCharge").ToArray();for(int i=0;i<ec.Length;i++)ec[i].amount=power[i];phase=7;stamp=now;Note("PASS real CPU power starvation interrupts an actual burn with optional critical abort disabled");}
   else if(phase==7&&now-stamp>5){var m=Pilot();Held(m);Cancel(m);NewNode(m,10);phase=8;Note("PASS restoring power does not restart held thrust; cancel releases plan");}
   else if(phase==8&&node.nextPatch!=null&&now-stamp>2&&Pilot().PilotOnline){Queue(Pilot());var m=Pilot();enginePart=m.vessel.parts.First(p=>p.Modules.OfType<ModuleEngines>().Any());foreach(var e in enginePart.Modules.OfType<ModuleEngines>())e.Activate();m.vessel.parts.First(p=>p.name.Contains("fuelTank")).decouple(0);phase=9;stamp=now;}
   else if(phase==9&&now-stamp>2){var m=Pilot();Held(m);Check(enginePart.vessel!=m.vessel,"Stock separation did not split vessel");Check(enginePart.Modules.OfType<ModuleEngines>().All(e=>!e.EngineIgnited),"Detached engine fragment remained ignited");Check(m.interruptionReason.Contains("Craft"),"Separation cause missing");Cancel(m);phase=10;stamp=now;Note("PASS actual stock part decoupling holds queued plan and shuts the original detached engine fragment");Prepare();cpuPart=null;node=null;}
   else if(phase==10&&craft!=null&&craft.loaded&&!craft.packed&&MissionControl.Pilot(craft)!=null&&now>10008){cpuPart=MissionControl.Pilot(craft).part;NewNode(Pilot(),10);phase=11;}
   else if(phase==11&&node.nextPatch!=null&&now-stamp>2){var m=Pilot();Queue(m);var other=FlightGlobals.Vessels.First(v=>v.vesselName=="MC FOCUS");coupledRoot=other.rootPart;dockInfo=new DockedVesselInfo{name=other.vesselName,rootPartUId=other.rootPart.flightID,vesselType=other.vesselType};other.SetPosition(m.vessel.GetWorldPos3D()+new Vector3d(6,0,0));coupledRoot.Couple(m.vessel.rootPart);phase=12;stamp=now;}
   else if(phase==12&&now-stamp>2){var m=Pilot();Check(coupledRoot.vessel==m.vessel&&m.vessel.parts.Count>=6,"Native coupling did not merge vessels");Held(m);Cancel(m);NewNode(m,10);phase=13;Note("PASS forced stock docking-backend vessel coupling holds plan after genuine vessel/part reconstruction");}
   else if(phase==13&&node.nextPatch!=null&&now-stamp>2&&Pilot().PilotOnline){Queue(Pilot());coupledRoot.Undock(dockInfo);phase=14;stamp=now;}
   else if(phase==14&&now-stamp>2){var m=Pilot();Check(coupledRoot.vessel!=m.vessel,"Native undock did not split vessels");Held(m);Cancel(m);phase=15;stamp=now;Note("PASS actual stock undocking holds the merged-craft plan and refuses stale resume");Note("PASS ALL INTERRUPTION NATIVE OPERATIONS");}
   else if(phase==15&&now-stamp>2){phase=16;Application.Quit();}
  }catch(Exception e){Note("FAIL "+e);phase=99;Application.Quit();}
 }
    void Prepare(){
        string folder="PFInterrupt-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath,"saves",folder));
        var root=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/template.sfs"));var g=root.GetNode("GAME");g.SetValue("Title","Mission control disposable test (SANDBOX)");g.SetValue("Mode","SANDBOX");g.SetValue("scene","5");
        var fs=g.GetNode("FLIGHTSTATE");var source=fs.GetNodes("VESSEL").First(v=>v.GetNodes("PART").Any(p=>p.GetNodes("MODULE").Any(m=>m.GetValue("name")=="kOSProcessor")));
        var originals=source.GetNodes("PART");var cpu=originals.SelectMany(p=>p.GetNodes("MODULE")).First(m=>m.GetValue("name")=="kOSProcessor").CreateCopy();
        var tiny=new[]{originals.First(p=>p.GetValue("name")=="landerCabinSmall").CreateCopy(),originals.First(p=>p.GetValue("name")=="fuelTank.long").CreateCopy(),originals.First(p=>p.GetValue("name")=="liquidEngine3.v2").CreateCopy()};
        fs.RemoveNodes("VESSEL");fs.SetValue("UT","10000");fs.SetValue("activeVessel","0",true);
        for(int i=0;i<3;i++){
            var v=source.CreateCopy();v.RemoveNodes("PART");v.SetValue("ref",(920000+i*100).ToString());v.SetValue("pid",Guid.NewGuid().ToString("N"));v.SetValue("persistentId",(920000+i*100).ToString());
            v.SetValue("name",i==0?"MC FOCUS":i==1?"MC PILOT":"MC REMOTE");v.SetValue("sit","ORBITING");v.SetValue("landed","False");v.SetValue("splashed","False");v.SetValue("type","Probe");v.SetValue("alt","100000");v.SetValue("hgt","100000");v.SetValue("lat","0");v.SetValue("lon","0");v.SetValue("rot","0,0,0,1");v.SetValue("landedAt","");v.RemoveNodes("FLIGHTPLAN");
            var orbit=v.GetNode("ORBIT");orbit.SetValue("SMA",i==2?"300000":"700000");orbit.SetValue("ECC","0");orbit.SetValue("INC","0");orbit.SetValue("LPE","0");orbit.SetValue("LAN","0");orbit.SetValue("MNA",i==0?"0":"0.001");orbit.SetValue("EPH","10000");orbit.SetValue("REF",i==2?"2":"1");
            v.GetNode("CTRLSTATE").SetValue("mainThrottle","0");
            for(int n=0;n<3;n++){
                var p=tiny[n].CreateCopy();p.SetValue("uid",(920000+i*100+n).ToString());p.SetValue("persistentId",(920000+i*100+n).ToString(),true);p.SetValue("parent",n==0?"0":(n-1).ToString());p.SetValue("position","0,"+(-3*n)+",0");p.SetValue("rotation","0,0,0,1");p.RemoveValues("attN");p.RemoveValues("sym");p.RemoveValues("crew");p.SetValue("srfN",", -1");p.SetValue("attm","0");p.SetValue("autostrutMode","Off");
                if(n>0)p.AddValue("attN","top, "+(n-1));if(n<2)p.AddValue("attN","bottom, "+(n+1));
                foreach(var m in p.GetNodes("MODULE").Where(m=>new[]{"kOSProcessor","ModulePersistentFlight","ModuleRegionalAutonomy","MechJebCore","ModuleTripLogger","KOSNameTag"}.Contains(m.GetValue("name"))))p.RemoveNode(m);
                foreach(var r in p.GetNodes("RESOURCE"))r.SetValue("amount",r.GetValue("maxAmount"));
                foreach(var m in p.GetNodes("MODULE").Where(m=>m.GetValue("name")=="ModuleEngines"||m.GetValue("name")=="ModuleEnginesFX")){m.SetValue("EngineIgnited","False",true);m.SetValue("engineShutdown","False",true);}
                if(n==0){var c=cpu.CreateCopy();c.RemoveNodes("harddisk");c.SetValue("activated",i!=2?"True":"False",true);c.SetValue("bootFile",i==1?"boot/pf_mission_test.ks":i==0?"boot/pf_event_test.ks":"None",true);p.AddNode(c);
                    var keep=p.AddNode("MODULE");keep.AddValue("name","ModulePersistentFlight");keep.AddValue("armed",i==2?"False":"True");keep.AddValue("rangeKm","60");
                    var auto=p.AddNode("MODULE");auto.AddValue("name","ModuleRegionalAutonomy");auto.AddValue("automatic",i==2?"False":"True");auto.AddValue("recordFlight","True");
                }
                if(n==2){var tag=p.AddNode("MODULE");tag.AddValue("name","KOSNameTag");tag.AddValue("nameTag","NODE_ENGINE");}v.AddNode(p);
            }
            fs.AddNode(v);
        }
        root.Save(Path.Combine(KSPUtil.ApplicationRootPath,"saves/"+folder+"/mc.sfs"));HighLogic.SaveFolder=folder;
        var game=GamePersistence.LoadGame("mc",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;game.flightState.activeVesselIdx=0;HighLogic.CurrentGame=game;game.Start();
    }

}
