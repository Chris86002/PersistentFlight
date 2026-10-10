using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;
using PersistentFlight;
using PersistentFlight.Regions;
[assembly: KSPAssembly("PersistentFlight.RecoveryTests",0,20)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class RecoveryHarness:MonoBehaviour {
 string result,baseline,folder;int phase;float deadline;double stamp,expectedLF;Guid expectedFocus;ManeuverNode queuedNode;
 void Awake(){DontDestroyOnLoad(this);}
 void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-RECOVERY-TEST] "+s);}
 void Check(bool b,string why){if(!b)throw new Exception(why);}
 IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-recovery-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+300;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}}
 void Inspect(Vessel craft,Vessel remote,ModuleRegionalAutonomy m){
  double cap,lf=MissionControl.Resource(craft,"LiquidFuel",out cap);float throttle=craft.ctrlState.mainThrottle;string queue=m.queuedNodes64;bool requested=m.nodeRequested;
  var rows=FlightReadiness.Inspect(craft);Check(rows.Any(r=>r.Item.EndsWith("pilot")&&!r.Attention),"live heartbeat missing");Check(rows.Any(r=>r.Item.EndsWith("engines / fuel")&&!r.Attention),"tagged stock engine/fuel not ready");
  Check(FlightReadiness.Inspect(remote).Any(r=>r.Item=="Flight state"&&r.Attention),"remote live state claimed");
  var ec=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).First(r=>r.resourceName=="ElectricCharge");double amount=ec.amount;try{ec.amount=0;Check(FlightReadiness.Inspect(craft).Any(r=>r.Item=="Power"&&r.Attention),"empty power not flagged");}finally{ec.amount=amount;}
  string target=m.nodeTargetOrbit;try{m.nodeRequested=true;m.nodeTargetOrbit="damaged";Check(FlightReadiness.Inspect(craft).Any(r=>r.Detail.Contains("Could not validate")),"damaged target not flagged");Check(m.nodeRequested,"inspection cancelled mission");}finally{m.nodeRequested=requested;m.nodeTargetOrbit=target;}
  bool held=m.missionHold;try{m.missionHold=true;Check(FlightReadiness.Inspect(craft).Any(r=>r.Item.EndsWith("ownership")&&r.Attention),"held mission not flagged");}finally{m.missionHold=held;}
  Check(MissionControl.Resource(craft,"LiquidFuel",out cap)==lf&&craft.ctrlState.mainThrottle==throttle&&m.queuedNodes64==queue&&m.nodeRequested==requested,"inspection changed flight");
  Note("PASS native readiness: live heartbeat, stock tagged engines, connected fuel, empty power, held mission, sleeping remote and invalid target; no guidance mutation");
 }
 void Update(){if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
  try{
   Check(Time.realtimeSinceStartup<deadline,"Recovery timeout");var recovery=FleetRecovery.Instance;if(recovery==null)return;
   var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");var remote=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC REMOTE");var focus=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC FOCUS");if(craft==null||remote==null||focus==null)return;double now=Planetarium.GetUniversalTime();
   if(phase==0&&craft.loaded&&!craft.packed&&MissionControl.Pilot(craft)!=null&&now>10008){
    var m=MissionControl.Pilot(craft);Inspect(craft,remote,m);recovery.SetAutomatic(false);
    folder=Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder);
    for(int i=0;i<10;i++)recovery.Capture("Rotation "+i);
    Check(recovery.Points().Length==8,"native rotation not bounded");Check(Directory.GetFiles(folder,"PF-Recovery-*.loadmeta").Length==8,"native preview caches leaked during rotation");foreach(var p in recovery.Points())Check(ConfigNode.Load(Path.Combine(folder,p.Name+".sfs")).GetNode("GAME").HasNode("FLIGHTSTATE"),"not a whole game save");
    Note("PASS ten native full-game saves rotate to eight points");
    var bad=recovery.Points().First();File.AppendAllText(Path.Combine(folder,bad.Name+".sfs"),"\nDAMAGED");bool denied=false;try{recovery.Restore(bad.Name);}catch(InvalidDataException){denied=true;}Check(denied&&FlightGlobals.ActiveVessel==focus,"damaged save restored");Note("PASS damaged save refused before any focus change");
    Check(RegionFlightRunner.Instance.Focus(craft),"focus before queue failed");recovery.SetAutomatic(true);
    MapView.EnterMapView();queuedNode=craft.patchedConicSolver.AddManeuverNode(now+600);queuedNode.OnGizmoUpdated(new Vector3d(0,0,10),queuedNode.UT);craft.patchedConicSolver.UpdateFlightPlan();phase=10;stamp=now;
   }
   else if(phase==10&&queuedNode.nextPatch!=null&&now-stamp>2){
    var m=MissionControl.Pilot(craft);if(m==null)return;MissionControl.QueueNext(craft,m);
    Check(recovery.Points().First().Reason.Contains("Maneuver queued"),"automatic queue recovery missing");m.CancelNode("Recovery fixture cancel");queuedNode.RemoveSelf();MapView.ExitMapView();
    // Recording quarantine must not suppress independent recovery.
    var service=PersistentFlightScenario.Instance;var raw=typeof(PersistentFlightScenario).GetField("rawDebriefs",BindingFlags.NonPublic|BindingFlags.Instance);object previousRaw=raw.GetValue(service);bool requested=m.nodeRequested;bool burning=m.burnInProgress;
    try{raw.SetValue(service,new ConfigNode("FLIGHT_DEBRIEFS"));m.nodeRequested=true;typeof(FleetRecovery).GetField("lastAutomatic",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(recovery,-1000f);FlightDebriefRecorder.Begin(m);Check(recovery.Points().First().Reason.Contains("Before pilot burn"),"debrief quarantine suppressed recovery");}
    finally{raw.SetValue(service,previousRaw);m.nodeRequested=requested;m.burnInProgress=burning;}
    Note("PASS automatic pre-burn recovery remains available with quarantined debrief recording (transient native fault injection)");
    recovery.SetAutomatic(false);
    m.landingSite="Recovery marker";var tank=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).First(r=>r.resourceName=="LiquidFuel");tank.amount=tank.maxAmount*.9;expectedLF=tank.amount;expectedFocus=craft.id;
    var saved=recovery.Capture("Restore baseline");baseline=saved.Name;tank.amount=tank.maxAmount*.6;m.landingSite="Changed after recovery";
    Check(RegionFlightRunner.Instance.Focus(focus),"focus mutation failed");
    phase=11;stamp=now;
   }
   else if(phase==11&&FlightGlobals.ActiveVessel==focus&&now-stamp>1){
    recovery.Restore(baseline);phase=1;stamp=now;Note("PASS automatic queued-plan snapshot and actual native recovery requested with current-state backup");
   }
   else if(phase==1&&FlightGlobals.ActiveVessel.id==expectedFocus&&craft.loaded&&!craft.packed&&now-stamp>1){
    double cap;Check(Math.Abs(MissionControl.Resource(craft,"LiquidFuel",out cap)-expectedLF)<.001,"tank state not rewound");Check(MissionControl.Modules(craft).Any(m=>m.landingSite=="Recovery marker"),"CPU state not rewound");Check(!recovery.Automatic,"per-save recovery preference not preserved");Check(recovery.Points().Any(p=>p.Reason.StartsWith("Before restoring:")),"return point missing");
    Note("PASS cold native restore returns saved focus, fuel and CPU marker; current-state recovery and disabled preference retained");
    var panel=FindObjectOfType<RegionalFleetPanel>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(RegionalFleetPanel).GetField("showReadiness",flags).SetValue(panel,true);typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});phase=2;stamp=now;
   }
   else if(phase==2&&now-stamp>2){ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"flight-readiness-preview.png"));phase=3;stamp=now;}
   else if(phase==3&&now-stamp>2){Note("PASS ALL RECOVERY AND READINESS NATIVE OPERATIONS");phase=4;Application.Quit();}
  }catch(Exception e){Note("FAIL "+e);phase=99;Application.Quit();}
 }
    void Prepare(){
        string folder="PFRecovery-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath,"saves",folder));
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