using System;using System.IO;using System.Linq;using System.Collections;using UnityEngine;using PersistentFlight;using PersistentFlight.Regions;
[assembly: KSPAssembly("PersistentFlight.LandingTests",0,22)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class LandingHarness:MonoBehaviour {
 Vector3d warmPos;Vessel warmV;float warmUntil;System.Collections.Generic.List<FlightDebrief> history=new System.Collections.Generic.List<FlightDebrief>();string result;int phase,trial;float deadline;double stamp;Guid craftId;FlightDebrief report;
 void Awake(){DontDestroyOnLoad(this);}
 void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-LANDING-TEST] "+s);}
 void Check(bool b,string s){if(!b)throw new Exception(s);}
 IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-landing-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+600;yield return new WaitForSeconds(3);Prepare();}
 void Drop(Vessel v){
  Check(RegionFlightRunner.Instance.Focus(v)||v==FlightGlobals.ActiveVessel,"Focus failed");var b=v.mainBody;double lat=trial==1?0:-.2187,lon=trial==1?-50:-74.5732;double ground=Math.Max(0,b.TerrainAltitude(lat,lon,false));Check(trial!=1||ground==0,"Water fixture is not ocean");
  var m=MissionControl.Modules(v).First();MissionControl.SetSite(m,"Native touchdown target",b,lat,lon+.001);m.landingReports=true;if(trial==2)m.landingVessel=Guid.NewGuid().ToString();
  Vector3d pos=b.GetWorldSurfacePosition(lat,lon,ground+(trial==2?45:3)),up=(pos-b.position).normalized;
  v.SetPosition(pos);v.SetRotation(Quaternion.FromToRotation(v.transform.up,(Vector3)up)*v.transform.rotation);
  v.SetWorldVelocity(((FlightGlobals.RefFrameIsRotating?Vector3d.zero:b.getRFrmVel(pos))-Krakensbane.GetFrameVelocity())-up*(trial==2?60:1));v.ctrlState.mainThrottle=0;craftId=v.id;stamp=Planetarium.GetUniversalTime();phase=1;Note("DROP trial="+trial+" terrain="+ground);
 }
 void Update(){if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;try{
  Check(Time.realtimeSinceStartup<deadline,"Timeout phase="+phase+" trial="+trial);double now=Planetarium.GetUniversalTime();
  if(phase==0){var v=FlightGlobals.Vessels.FirstOrDefault(x=>x.vesselName=="MC PILOT");if(v!=null&&v.loaded&&!v.packed&&now>10008){var m=MissionControl.Modules(v).First();Check(RegionFlightRunner.Instance.Focus(v)||v==FlightGlobals.ActiveVessel,"Warmup focus failed");m.landingReports=false;m.automatic=false;foreach(var region in RegionFlightRunner.Instance.Regions.Where(r=>r.Vessel==v).ToArray())RegionFlightRunner.Instance.Release(region);double lat=trial==1?0:-.2187,lon=trial==1?-50:-74.5732;var b=v.mainBody;warmPos=b.GetWorldSurfacePosition(lat,lon,Math.Max(0,b.TerrainAltitude(lat,lon,false))+500);warmV=v;warmUntil=Time.realtimeSinceStartup+15;phase=4;}}
  else if(phase==4){var b=warmV.mainBody;double lat=trial==1?0:-.2187,lon=trial==1?-50:-74.5732;warmPos=b.GetWorldSurfacePosition(lat,lon,Math.Max(0,b.TerrainAltitude(lat,lon,false))+500);warmV.SetPosition(warmPos);warmV.SetWorldVelocity(((FlightGlobals.RefFrameIsRotating?Vector3d.zero:b.getRFrmVel(warmPos))-Krakensbane.GetFrameVelocity()));if(Time.realtimeSinceStartup>=warmUntil){foreach(var r in history)PersistentFlightScenario.Instance.Debriefs.Add(r.Copy());Drop(warmV);}}
  else if(phase==1){report=PersistentFlightScenario.Instance.Debriefs.Records.LastOrDefault(r=>r.Vessel==craftId&&r.Mode==DebriefMode.Landing);if(report!=null)phase=2;else Check(now-stamp<15,"No observed approach");}
  else if(phase==2&&report.Finished){
   if(trial<2){var v=FlightGlobals.Vessels.FirstOrDefault(x=>x.id==craftId);Check(v!=null&&v.LandedOrSplashed,"No real native contact: "+report.Outcome+" speed="+report.TouchdownSpeed+" vertical="+report.VerticalSpeed);Check(report.Contact==(trial==1?"Water":"Ground"),"Contact type wrong: "+report.Contact);Check(report.TouchdownSpeed>0&&report.TouchdownSpeed<15,"Pre-contact speed missing/implausible");Check(report.TargetDistance>0&&report.TargetDistance<200,"Target distance wrong");Check(report.RemainingEC>0&&report.InitialParts==1&&Math.Abs(report.RemainingLF-5)<.001&&Math.Abs(report.RemainingOx-6.11)<.001,"Remaining resources/parts missing");Note("PASS actual "+report.Contact+" contact: speed="+report.TouchdownSpeed+" miss="+report.TargetDistance+" lost="+report.LostParts+" outcome="+report.Outcome);}
   else {Check(report.Outcome.StartsWith("Failed")||report.LostParts>0,"Hard impact not reported as damage/failure");Check(Math.Abs(report.RemainingLF-5)<.001&&Math.Abs(report.RemainingOx-6.11)<.001,"Destruction replaced last live fuel with zero");Check(Double.IsNaN(report.TargetDistance)&&report.LandingNote!="","Missing surface target substituted");Note("PASS native hard impact records failed approach or part loss: "+report.Outcome);}
   var s=PersistentFlightScenario.Instance;var saved=new ConfigNode("SCENARIO");s.OnSave(saved);s.OnLoad(saved);Check(s.Debriefs.Find(report.Id).Contact==report.Contact&&s.Debriefs.Find(report.Id).Outcome==report.Outcome,"Landing report roundtrip failed");
   history.Add(report.Copy());if(trial<2){trial++;phase=0;report=null;Prepare();}else{HistoryChecks();File.Copy(RegionalFleetPanel.ExportFlightDebriefs(),Path.Combine(KSPUtil.ApplicationRootPath,"landing-debrief.csv"),true);Check(File.ReadAllText(Path.Combine(KSPUtil.ApplicationRootPath,"landing-debrief.csv")).Contains("pre_contact_speed"),"Landing CSV missing");Note("PASS saved landing reports and CSV export");Note("PASS ALL LANDING NATIVE OPERATIONS");phase=3;Application.Quit();}
  }
 }catch(Exception e){Note("FAIL "+e);phase=99;Application.Quit();}}
 void HistoryChecks(){
  var s=PersistentFlightScenario.Instance;var burn=new FlightDebrief{Id=Guid.NewGuid(),Branch=s.Debriefs.Branch,Vessel=craftId,PlannedDV=10,QueuedUT=10000,NodeUT=10050,Outcome="Old pending burn"};s.Debriefs.Add(burn);
  var original=new ConfigNode("SCENARIO");s.OnSave(original);var legacy=original.CreateCopy();legacy.SetValue("debriefVersion","1");var root=legacy.GetNode("FLIGHT_DEBRIEFS");foreach(var row in root.GetNodes("BURN_REPORT")){if(row.GetValue("Mode")=="Landing"){root.RemoveNode(row);continue;}foreach(var field in typeof(FlightDebrief).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))if(FlightDebrief.LandingField(field.Name))row.RemoveValues(field.Name);}root.SetValue("count",root.GetNodes("BURN_REPORT").Length.ToString());
  s.OnLoad(legacy);Check(s.DebriefError==""&&s.Debriefs.Find(burn.Id)!=null,"Format-1 burn history not restored");s.OnLoad(original);
  var pending=report.Copy();pending.Id=Guid.NewGuid();pending.Finished=false;pending.EndUT=0;pending.Outcome="Approach observed";s.Debriefs.Add(pending);var open=new ConfigNode("SCENARIO");s.OnSave(open);s.OnLoad(open);var restored=s.Debriefs.Find(pending.Id);Check(restored.Finished&&restored.Incomplete&&restored.Outcome.Contains("save reloaded"),"Reloaded approach mistaken for success");
  var damaged=open.CreateCopy();damaged.GetNode("FLIGHT_DEBRIEFS").GetNodes("BURN_REPORT").First().RemoveValues("TargetDistance");s.OnLoad(damaged);Check(s.DebriefError!="","Damaged version-2 landing fields accepted");var held=new ConfigNode("SCENARIO");s.OnSave(held);Check(held.GetValue("debriefHeld")=="True","Damaged history not preserved/held");s.OnLoad(open);Check(s.DebriefError=="","Good history did not recover");
  Note("PASS old format-1 burn history migration, interrupted open-approach reload, strict damaged-field hold and evidence retention");
 }
    void Prepare(){
        string folder="PFLanding-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath,"saves",folder));
        var root=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/template.sfs"));var g=root.GetNode("GAME");g.SetValue("Title","Mission control disposable test (SANDBOX)");g.SetValue("Mode","SANDBOX");g.SetValue("scene","5");
        var fs=g.GetNode("FLIGHTSTATE");var source=fs.GetNodes("VESSEL").First(v=>v.GetNodes("PART").Any(p=>p.GetNodes("MODULE").Any(m=>m.GetValue("name")=="kOSProcessor")));
        var originals=source.GetNodes("PART");var cpu=originals.SelectMany(p=>p.GetNodes("MODULE")).First(m=>m.GetValue("name")=="kOSProcessor").CreateCopy();
        var tiny=new[]{originals.First(p=>p.GetValue("name")=="landerCabinSmall").CreateCopy()};
        fs.RemoveNodes("VESSEL");fs.SetValue("UT","10000");fs.SetValue("activeVessel","0",true);
        for(int i=0;i<3;i++){
            var v=source.CreateCopy();v.RemoveNodes("PART");v.SetValue("ref",(920000+i*100).ToString());v.SetValue("pid",Guid.NewGuid().ToString("N"));v.SetValue("persistentId",(920000+i*100).ToString());
            v.SetValue("name",i==0?"MC FOCUS":i==1?"MC PILOT":"MC REMOTE");v.SetValue("sit","ORBITING");v.SetValue("landed","False");v.SetValue("splashed","False");v.SetValue("type","Probe");v.SetValue("alt","100000");v.SetValue("hgt","100000");v.SetValue("lat","0");v.SetValue("lon","0");v.SetValue("rot","0,0,0,1");v.SetValue("landedAt","");v.RemoveNodes("FLIGHTPLAN");
            var orbit=v.GetNode("ORBIT");orbit.SetValue("SMA",i==2?"300000":"700000");orbit.SetValue("ECC","0");orbit.SetValue("INC","0");orbit.SetValue("LPE","0");orbit.SetValue("LAN","0");orbit.SetValue("MNA",i==0?"0":"0.001");orbit.SetValue("EPH","10000");orbit.SetValue("REF",i==2?"2":"1");
            v.GetNode("CTRLSTATE").SetValue("mainThrottle","0");
            for(int n=0;n<1;n++){
                var p=tiny[n].CreateCopy();p.SetValue("uid",(920000+i*100+n).ToString());p.SetValue("persistentId",(920000+i*100+n).ToString(),true);p.SetValue("parent",n==0?"0":(n-1).ToString());p.SetValue("position","0,"+(-3*n)+",0");p.SetValue("rotation","0,0,0,1");p.RemoveValues("attN");p.RemoveValues("sym");p.RemoveValues("crew");p.SetValue("srfN",", -1");p.SetValue("attm","0");p.SetValue("autostrutMode","Off");
                if(n>0)p.AddValue("attN","top, "+(n-1));if(n<0)p.AddValue("attN","bottom, "+(n+1));
                foreach(var m in p.GetNodes("MODULE").Where(m=>new[]{"kOSProcessor","ModulePersistentFlight","ModuleRegionalAutonomy","MechJebCore","ModuleTripLogger","KOSNameTag"}.Contains(m.GetValue("name"))))p.RemoveNode(m);
                foreach(var r in p.GetNodes("RESOURCE"))r.SetValue("amount",r.GetValue("maxAmount"));
                foreach(var m in p.GetNodes("MODULE").Where(m=>m.GetValue("name")=="ModuleEngines"||m.GetValue("name")=="ModuleEnginesFX")){m.SetValue("EngineIgnited","False",true);m.SetValue("engineShutdown","False",true);}
                if(n==0){foreach(var spec in new[]{new[]{"LiquidFuel","5","10"},new[]{"Oxidizer","6.11","12.22"}}){var r=p.AddNode("RESOURCE");r.AddValue("name",spec[0]);r.AddValue("amount",spec[1]);r.AddValue("maxAmount",spec[2]);}var c=cpu.CreateCopy();c.RemoveNodes("harddisk");c.SetValue("activated","False",true);c.SetValue("bootFile","None",true);p.AddNode(c);
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

