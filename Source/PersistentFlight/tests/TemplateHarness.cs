using System;using System.IO;using System.Linq;using System.Collections;using UnityEngine;using PersistentFlight;using PersistentFlight.Regions;
[assembly: KSPAssembly("PersistentFlight.TemplateTests",0,23)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class TemplateHarness:MonoBehaviour {
 string result,library;int phase;float deadline;double stamp,fuel,firstUT,sourceTarget;MissionTemplate template;Guid destination;ManeuverNode[] applied;
 void Awake(){DontDestroyOnLoad(this);}
 void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-TEMPLATE-TEST] "+s);}
 void Check(bool b,string s){if(!b)throw new Exception(s);}
 void Reject(Action a,string s){bool denied=false;try{a();}catch(InvalidOperationException){denied=true;}catch(ArgumentException){denied=true;}Check(denied,s);}
 double Fuel(Vessel v){double c;return MissionControl.Resource(v,"LiquidFuel",out c);}
 IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-template-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+700;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}}
 void Update(){if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;try{
  Check(Time.realtimeSinceStartup<deadline,"Timeout phase "+phase);double now=Planetarium.GetUniversalTime();var source=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC FOCUS");var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");
  if(phase==0&&source!=null&&craft!=null&&source.loaded&&!source.packed&&craft.loaded&&!craft.packed&&now>10008){MapView.EnterMapView();var a=source.patchedConicSolver.AddManeuverNode(now+120);a.OnGizmoUpdated(new Vector3d(0,0,15),a.UT);var b=source.patchedConicSolver.AddManeuverNode(now+180);b.OnGizmoUpdated(new Vector3d(0,0,10),b.UT);source.patchedConicSolver.UpdateFlightPlan();stamp=now;phase=1;}
  else if(phase==1&&now-stamp>2&&source.patchedConicSolver.maneuverNodes.All(n=>n.nextPatch!=null)){
   double f=Fuel(source);template=MissionTemplateFlight.Capture(source,"Native two-burn template");library=MissionTemplateFlight.Folder;MissionTemplateStore.Save(library,template);template=MissionTemplateStore.Load(library,template.Id);Check(template.Steps.Count==2&&Math.Abs(template.Steps[1].Delay-60)<.001,"Relative timing wrong");sourceTarget=source.patchedConicSolver.maneuverNodes[0].nextPatch.semiMajorAxis;
   Reject(()=>MissionTemplateFlight.Apply(source,template,70),"Existing plan overwritten");Check(source.patchedConicSolver.maneuverNodes.Count==2&&Fuel(source)==f&&source.ctrlState.mainThrottle==0&&MissionControl.Modules(source).All(m=>m.QueueCount==0),"Capture/rejected apply changed source controls or map plan");
   Check(RegionFlightRunner.Instance.Focus(craft),"Destination focus failed");destination=craft.id;stamp=now;phase=2;Note("PASS native map capture, persistent checked file and preserved existing source plan");
  }
  else if(phase==2&&craft==FlightGlobals.ActiveVessel&&MissionControl.Pilot(craft)!=null&&now-stamp>2){
   Check(craft.patchedConicSolver.maneuverNodes.Count==0,"Destination has unexpected map plan");var wrong=MissionTemplateStore.Decode(MissionTemplateStore.Encode(template));wrong.Body="Mun";Reject(()=>MissionTemplateFlight.Apply(craft,wrong,70),"Wrong body accepted");Reject(()=>MissionTemplateFlight.Apply(craft,template,0),"Invalid first delay accepted");craft.ctrlState.mainThrottle=.1f;try{Reject(()=>MissionTemplateFlight.Apply(craft,template,70),"Powered craft accepted");}finally{craft.ctrlState.mainThrottle=0;}
   Check(craft.patchedConicSolver.maneuverNodes.Count==0&&MissionControl.Modules(craft).All(m=>m.QueueCount==0),"Rejected application mutated destination");fuel=Fuel(craft);applied=MissionTemplateFlight.Apply(craft,template,70);firstUT=applied[0].UT;Check(Math.Abs(firstUT-now-70)<.001&&Math.Abs(applied[1].UT-firstUT-60)<.001,"Fresh application times wrong");Check(applied[0].DeltaV.z==15&&applied[1].DeltaV.z==10&&craft.ctrlState.mainThrottle==0&&Fuel(craft)==fuel&&MissionControl.Modules(craft).All(m=>m.QueueCount==0),"Applying template started mission or changed vector/fuel");stamp=now;phase=3;Note("PASS wrong-body, unsafe timing and powered-craft refusal; fresh relative plan on a different craft/orbit without thrust or queue activation");
  }
  else if(phase==3&&now-stamp>3&&applied.All(n=>n.nextPatch!=null)){
   Check(Math.Abs(applied[0].nextPatch.semiMajorAxis-sourceTarget)>10000,"Destination reused source target orbit");var m=MissionControl.Pilot(craft);Check(FleetFuelForecast.Evaluate(m,25).State.ToString()!="Unknown","Template fuel estimate unavailable");
   var panel=FindObjectOfType<RegionalFleetPanel>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(RegionalFleetPanel).GetField("showTemplates",flags).SetValue(panel,true);typeof(RegionalFleetPanel).GetField("selectedTemplate",flags).SetValue(panel,template.Id);typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});phase=31;stamp=now;
  }
  else if(phase==31&&now-stamp>2){ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"mission-template-preview.png"));phase=32;stamp=now;}
  else if(phase==32&&now-stamp>2){var panel=FindObjectOfType<RegionalFleetPanel>();typeof(RegionalFleetPanel).GetMethod("SetOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,new object[]{false});var m=MissionControl.Pilot(craft);MissionQueue.QueuePlan(craft,m);Check(m.QueueCount==2,"Recomputed plan did not queue");Reject(()=>MissionTemplateFlight.Apply(craft,template,70),"Busy destination accepted");Check(m.QueueCount==2,"Busy refusal erased queued plan");GamePersistence.SaveGame("template-queued",HighLogic.SaveFolder,SaveMode.OVERWRITE);var game=GamePersistence.LoadGame("template-queued",HighLogic.SaveFolder,true,false);int index=game.flightState.protoVessels.FindIndex(v=>v.vesselID==destination);FlightDriver.StartAndFocusVessel(game,index);stamp=now;phase=4;Note("PASS recomputed destination orbit, real template UI preview, fuel advisory and explicit queue; actual cold scene reload requested");}
  else if(phase==4&&craft!=null&&craft.id==destination&&craft.loaded&&!craft.packed&&now-stamp>5&&MissionControl.Pilot(craft)!=null){var m=MissionControl.Pilot(craft);Check(m.QueueCount==2&&Math.Abs(m.nodeUT-firstUT)<.001,"Fresh template queue did not survive reload");Check(MissionTemplateStore.Load(MissionTemplateFlight.Folder,template.Id).Steps.Count==2,"Template library did not survive reload");MapView.ExitMapView();Check(RegionFlightRunner.Instance.Focus(source),"Unfocus destination failed");phase=5;Note("PASS cold-load boot, fresh two-node queue and external template library retained");}
  else if(phase==5){var m=MissionControl.Modules(craft).First();Check(!m.missionHold,"Template execution held: "+m.nodeStatus);if(m.QueueCount==0){var reports=PersistentFlightScenario.Instance.Debriefs.Records.Where(r=>r.Vessel==destination&&r.Mode==DebriefMode.LiveManeuver).ToArray();Check(reports.Length==2&&reports.All(r=>r.Finished&&r.Outcome=="Complete"&&r.ResidualDV<=.5)&&Fuel(craft)<fuel,"Applied template did not complete both actual unfocused burns");File.Copy(RegionalFleetPanel.ExportFlightDebriefs(),Path.Combine(KSPUtil.ApplicationRootPath,"template-flight-debrief.csv"),true);Reject(()=>MissionTemplateStore.Delete(library,template.Id,"stale"),"Stale template removal accepted");MissionTemplateStore.Delete(library,template.Id,template.Checksum);Check(!File.Exists(Path.Combine(library,template.Id.ToString("N")+".pft")),"Verified template removal failed");Note("PASS both template-derived maneuvers executed by real unfocused kOS pilot after cold load; debrief export and verified deletion");Note("PASS ALL MISSION TEMPLATE NATIVE OPERATIONS");phase=6;stamp=now;}}
  else if(phase==6&&now-stamp>2){Application.Quit();phase=7;}
 }catch(Exception e){Note("FAIL "+e);phase=99;Application.Quit();}}
    void Prepare(){
        string folder="PFTemplates-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath,"saves",folder));
        var root=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/template.sfs"));var g=root.GetNode("GAME");g.SetValue("Title","Mission control disposable test (SANDBOX)");g.SetValue("Mode","SANDBOX");g.SetValue("scene","5");
        var fs=g.GetNode("FLIGHTSTATE");var source=fs.GetNodes("VESSEL").First(v=>v.GetNodes("PART").Any(p=>p.GetNodes("MODULE").Any(m=>m.GetValue("name")=="kOSProcessor")));
        var originals=source.GetNodes("PART");var cpu=originals.SelectMany(p=>p.GetNodes("MODULE")).First(m=>m.GetValue("name")=="kOSProcessor").CreateCopy();
        var tiny=new[]{originals.First(p=>p.GetValue("name")=="landerCabinSmall").CreateCopy(),originals.First(p=>p.GetValue("name")=="fuelTank.long").CreateCopy(),originals.First(p=>p.GetValue("name")=="liquidEngine3.v2").CreateCopy()};
        fs.RemoveNodes("VESSEL");fs.SetValue("UT","10000");fs.SetValue("activeVessel","0",true);
        for(int i=0;i<3;i++){
            var v=source.CreateCopy();v.RemoveNodes("PART");v.SetValue("ref",(920000+i*100).ToString());v.SetValue("pid",Guid.NewGuid().ToString("N"));v.SetValue("persistentId",(920000+i*100).ToString());
            v.SetValue("name",i==0?"MC FOCUS":i==1?"MC PILOT":"MC REMOTE");v.SetValue("sit","ORBITING");v.SetValue("landed","False");v.SetValue("splashed","False");v.SetValue("type","Probe");v.SetValue("alt","100000");v.SetValue("hgt","100000");v.SetValue("lat","0");v.SetValue("lon","0");v.SetValue("rot","0,0,0,1");v.SetValue("landedAt","");v.RemoveNodes("FLIGHTPLAN");
            var orbit=v.GetNode("ORBIT");orbit.SetValue("SMA",i==2?"300000":i==1?"720000":"700000");orbit.SetValue("ECC","0");orbit.SetValue("INC","0");orbit.SetValue("LPE","0");orbit.SetValue("LAN","0");orbit.SetValue("MNA",i==0?"0":"0.001");orbit.SetValue("EPH","10000");orbit.SetValue("REF",i==2?"2":"1");
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
