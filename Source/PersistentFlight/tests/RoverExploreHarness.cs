using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEngine;
using PersistentFlight;
using PersistentFlight.Regions;
namespace PersistentFlight.Tests {
 [KSPAddon(KSPAddon.Startup.MainMenu,true)]
 public sealed class RoverExploreHarness:MonoBehaviour {
  string result,saved;bool done,ready,reloading,reloaded,battery;float deadline,nextReport,reloadWait;double start,lat,lon,siteAlt,maxSpeed,maxDistance;RoverPoint home;Guid routeId;int phase;
  static string F(double v){return v.ToString("R",CultureInfo.InvariantCulture);}static string Q(Quaternion q){return F(q.x)+","+F(q.y)+","+F(q.z)+","+F(q.w);}static string V(Vector3 v){return F(v.x)+","+F(v.y)+","+F(v.z);}
  string PathAt(string p){return Path.Combine(KSPUtil.ApplicationRootPath,p);}
  public void Awake(){DontDestroyOnLoad(this);}
  public IEnumerator Start(){result=PathAt("pf-rover-explore-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+1000;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Fail(e.ToString());}}
  void Note(string s){Debug.Log("[PF-EXPLORE] "+s);File.AppendAllText(result,s+Environment.NewLine);}void Fail(string s){if(done)return;Note("FAIL "+s);done=true;Application.Quit();}
    void Prepare(){
   File.Delete(PathAt("Ships/Script/pf_rover_boot.txt"));
   File.Delete(PathAt("Ships/Script/pf_rover_ready.flag"));File.Delete(PathAt("Ships/Script/pf_rover_pause.flag"));File.WriteAllText(PathAt("Ships/Script/pf_rover.csv"),"");File.WriteAllText(PathAt("Ships/Script/pf_rover_route.csv"),"");
   var root=ConfigNode.Load(PathAt("saves/PersistentFlightTest/template.sfs"));var g=root.GetNode("GAME");var fs=g.GetNode("FLIGHTSTATE");
   var original=fs.GetNodes("VESSEL").First(v=>v.GetNodes("PART").Any(p=>p.GetValue("name")=="landerCabinSmall"));
   var source=original.GetNodes("PART").First(p=>p.GetValue("name")=="landerCabinSmall");
   var cpu=original.GetNodes("PART").SelectMany(p=>p.GetNodes("MODULE")).First(m=>m.GetValue("name")=="kOSProcessor");
   var body=FlightGlobals.Bodies.First(b=>b.bodyName=="Mun");lat=0;lon=0;siteAlt=body.TerrainAltitude(lat,lon,false);
   var rover=original.CreateCopy();rover.RemoveNodes("PART");
   for(int i=0;i<6;i++){
    var p=source.CreateCopy();p.SetValue("name",i==0?"landerCabinSmall":i==1?"batteryBank":"wheelMed");
    p.SetValue("uid",(820000+i).ToString());p.SetValue("persistentId",(820000+i).ToString(),true);p.SetValue("parent","0");
    p.SetValue("mass",i==0?"0.6":i==1?"0.05":"0.105");
    Quaternion toCraft=Quaternion.Euler(-90,0,0);
    Vector3 placement=i==0?Vector3.zero:i==1?new Vector3(0,-.6f,0):new Vector3(i%2==0?-1.7f:1.7f,-.8f,i<4?1.6f:-1.6f);
    p.SetValue("position",V(toCraft*placement));
    p.SetValue("rotation",Q(i==0?Quaternion.identity:toCraft*(i>=2 && i%2==0?Quaternion.Euler(0,180,0):Quaternion.identity)));
    p.RemoveValues("sym");p.RemoveValues("crew");p.RemoveValues("attN");p.SetValue("srfN",i==0?", -1":"srfAttach, 0");p.SetValue("attm",i==0?"0":"1");p.SetValue("autostrutMode","Off");
    p.RemoveNodes("MODULE");p.RemoveNodes("RESOURCE");
    if(i==0){var c=cpu.CreateCopy();c.RemoveNodes("harddisk");c.SetValue("activated","True",true);c.SetValue("bootFile","boot/pf_rover_explore_test.ks",true);p.AddNode(c);
     var keep=p.AddNode("MODULE");keep.AddValue("name","ModulePersistentFlight");keep.AddValue("armed","True");keep.AddValue("rangeKm","60");
     var auto=p.AddNode("MODULE");auto.AddValue("name","ModuleRegionalAutonomy");auto.AddValue("automatic","True");
    }
    if(i==1){var ec=p.AddNode("RESOURCE");ec.AddValue("name","ElectricCharge");ec.AddValue("amount","1000");ec.AddValue("maxAmount","1000");ec.AddValue("flowState","True");}
    rover.AddNode(p);
   }
   rover.SetValue("pid",Guid.NewGuid().ToString("N"));rover.SetValue("persistentId","820000");rover.SetValue("ref","820000");rover.SetValue("name","PF ROVER");rover.SetValue("type","Rover");
   rover.SetValue("sit","LANDED");rover.SetValue("landed","True");rover.SetValue("splashed","False");rover.SetValue("lat",F(lat));rover.SetValue("lon",F(lon));rover.SetValue("alt",F(siteAlt+2.2));rover.SetValue("hgt","2.2");rover.SetValue("landedAt","");
   var normal=(Vector3)body.GetRelSurfaceNVector(lat,lon);rover.SetValue("rot",Q(Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(90,0,0)));rover.SetValue("nrm",V(normal),true);rover.RemoveNodes("FLIGHTPLAN");
   var orbit=rover.GetNode("ORBIT");orbit.SetValue("REF",body.flightGlobalsIndex);orbit.SetValue("SMA",body.Radius+100000);orbit.SetValue("ECC","0");orbit.SetValue("INC","0");orbit.SetValue("LPE","0");orbit.SetValue("LAN","0");orbit.SetValue("MNA","0");orbit.SetValue("EPH","10000");
   var observer=rover.CreateCopy();observer.RemoveNodes("PART");var pod=rover.GetNodes("PART")[0].CreateCopy();pod.SetValue("uid","830000");pod.SetValue("persistentId","830000");
   pod.GetNodes("MODULE").First(m=>m.GetValue("name")=="kOSProcessor").SetValue("activated","False");observer.AddNode(pod);
   observer.SetValue("pid",Guid.NewGuid().ToString("N"));observer.SetValue("persistentId","830000");observer.SetValue("ref","830000");observer.SetValue("name","PF ROVER OBSERVER");observer.SetValue("type","Probe");observer.SetValue("sit","ORBITING");observer.SetValue("landed","False");observer.SetValue("alt","100000");observer.SetValue("hgt","100000");observer.SetValue("rot","0,0,0,1");
   fs.RemoveNodes("VESSEL");fs.AddNode(rover);fs.AddNode(observer);fs.SetValue("UT","10000");fs.SetValue("activeVessel","0");g.SetValue("Title","PersistentFlightTest (SANDBOX)");g.SetValue("Mode","SANDBOX");g.SetValue("scene","5");
   root.Save(PathAt("saves/PersistentFlightTest/persistent.sfs"));HighLogic.SaveFolder="PersistentFlightTest";
   var game=GamePersistence.LoadGame("persistent",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;game.flightState.activeVesselIdx=0;HighLogic.CurrentGame=game;
   if(!game.scenarios.Any(s=>s.moduleName==typeof(PersistentFlightScenario).Name))game.AddProtoScenarioModule(typeof(PersistentFlightScenario),new[]{GameScenes.FLIGHT,GameScenes.SPACECENTER,GameScenes.TRACKSTATION});
   game.Start();Note("Prepared six-part stock Mun rover: four TR-2L wheels, command pod, 1000 EC; site="+lat+","+lon+" terrain="+siteAlt);
  }

  public void Update(){if(result!=null&&!done&&Time.realtimeSinceStartup>deadline)Fail("Runtime deadline");}
  public void FixedUpdate(){if(result==null||done||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;try{Tick();}catch(Exception e){Fail(e.ToString());}}
  void Check(bool value,string message){if(!value)throw new Exception(message);}
  void Tick(){
   double now=Planetarium.GetUniversalTime();if(start==0)start=now;
   var rover=FlightGlobals.Vessels.FirstOrDefault(v=>v.loaded&&v.vesselName=="PF ROVER");var observer=FlightGlobals.Vessels.FirstOrDefault(v=>v.loaded&&v.vesselName=="PF ROVER OBSERVER");
   if(reloading){if(Time.realtimeSinceStartup<reloadWait||rover==null||rover.packed)return;var recovered=MissionControl.Modules(rover)[0];var restored=RoverRouteController.Get(recovered);var snapshot=RoverRoute.Load(saved);Check(recovered.roverHold&&restored!=null&&restored.Request.Id==routeId&&restored.Index==snapshot.Index&&restored.Mode==snapshot.Mode&&restored.Hold==snapshot.Hold&&restored.ReturnIndex==snapshot.ReturnIndex&&restored.Trace.Length==snapshot.Trace.Length&&restored.Trace.Zip(snapshot.Trace,(a,b)=>a.Distance(b,200000)<.001).All(x=>x),"Cold route progress, trace or pause changed; pause="+recovered.roverHold);Check(restored.Trace.Length>=2,"Cold trace missing");Check(FlightGlobals.ActiveVessel!=rover,"Expected observer focus after cold load");if(RegionFlightRunner.Instance.Regions.All(x=>x.Vessel!=rover))return;
    reloading=false;reloaded=true;Note("PASS cold save/reload preserves route, breadcrumbs and pause while observer focused");RoverRouteController.Resume(recovered);phase=3;return;}
   if(rover==null||rover.packed)return;
   Check(rover.parts.Count==6,"Rover lost parts");Check(!rover.parts.SelectMany(p=>p.Modules.OfType<ModuleWheels.ModuleWheelDamage>()).Any(x=>x.isDamaged),"Stock wheel damaged");
   var m=MissionControl.Modules(rover)[0];var own=RegionFlightRunner.Instance.Regions.FirstOrDefault(x=>x.Vessel==rover);int feet=own==null?RegionalWheels.NativeContacts(rover):own.GroundedWheels;
   if(!ready){rover.ActionGroups.SetGroup(KSPActionGroup.Brakes,true);if(now-start<8)return;Check(feet>=3,"Fixture did not settle");home=new RoverPoint(rover.latitude,rover.longitude);routeId=Guid.NewGuid();
    var points=new[]{home.Offset(82.4,70,rover.mainBody.Radius),home.Offset(82.4,140,rover.mainBody.Radius),home.Offset(82.4,220,rover.mainBody.Radius)};
    RoverRouteController.Configure(m,new RoverRouteRequest(routeId,"Mun",home,points,3,5,25,.25,120,true));Check(m.roverHold&&m.ActiveRoverRoute,"New route not paused and owned");
    string valid=m.roverRoute64;m.roverRoute64="damaged";Check(RoverRouteController.Get(m)==null&&m.roverRouteHeld&&m.roverRoute64=="damaged"&&m.ActiveRoverRoute,"Corrupt checkpoint did not hold/preserve ownership");m.roverRoute64=valid;m.roverRouteHeld=false;RoverRouteController.Reset(m);
    m.Heartbeat(true);try{MissionControl.ValidateQueue(rover,m);throw new Exception("Conflicting maneuver accepted");}catch(InvalidOperationException e){Check(e.Message.Contains("rover route"),"Wrong queue guard: "+e.Message);}m.pilotReady=false;
    Note("PASS focused configuration, paused start, corrupt checkpoint preservation and maneuver ownership guard");var panel=FindObjectOfType<RegionalFleetPanel>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(RegionalFleetPanel).GetField("showRoutes",flags).SetValue(panel,true);typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});ScreenCapture.CaptureScreenshot(PathAt("rover-routes-preview.png"));RoverRouteController.Resume(m);ready=true;Check(RegionFlightRunner.Instance.Focus(observer),"Focus rejected");File.WriteAllText(PathAt("Ships/Script/pf_rover_ready.flag"),"ready");return;}
   var r=RoverRouteController.Get(m);Check(r!=null,"Route vanished");double distance=home.Distance(new RoverPoint(rover.latitude,rover.longitude),rover.mainBody.Radius);maxDistance=Math.Max(maxDistance,distance);maxSpeed=Math.Max(maxSpeed,rover.horizontalSrfSpeed);
   if(Time.realtimeSinceStartup>nextReport){nextReport=Time.realtimeSinceStartup+10;Note("Telemetry UT="+now+" phase="+phase+" route="+r.Mode+" hold="+r.Hold+" stop="+r.Index+" feet="+feet+" speed="+rover.horizontalSrfSpeed+" distance="+distance+" target="+new RoverPoint(rover.latitude,rover.longitude).Distance(r.Target,rover.mainBody.Radius)+" state="+RoverRouteController.Status(m)+" input="+rover.ctrlState.wheelThrottle+" steer="+rover.ctrlState.wheelSteer+" trace="+r.Trace.Length);}
   Check(r.Hold==RoverHold.None,"Unexpected route hold "+r.Hold);Check(!m.missionHold,"Unexpected mission safety hold "+m.missionPhase);
   if(phase==0&&distance>35){Check(own!=null,"No private background solver");m.roverHold=true;phase=1;Note("Pause before cold snapshot");}
   if(phase==1&&rover.horizontalSrfSpeed<.1){saved=m.roverRoute64;Check(rover.ActionGroups[KSPActionGroup.Brakes],"Pause did not brake");GamePersistence.SaveGame("pf-rover-explore-recovery",HighLogic.SaveFolder,SaveMode.OVERWRITE);var game=GamePersistence.LoadGame("pf-rover-explore-recovery",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;HighLogic.CurrentGame=game;reloading=true;reloadWait=Time.realtimeSinceStartup+12;phase=2;game.Start();return;}
   if(phase==3&&r.Index>=2&&!battery){Note("PASS two actual waypoints reached and recorded after cold recovery");foreach(var ec in rover.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(x=>x.resourceName=="ElectricCharge"))ec.amount=ec.maxAmount*.24;battery=true;phase=4;}
   if(phase==4&&r.Mode==RoverMode.Returning){Note("PASS battery reserve triggered breadcrumb return before third waypoint");phase=5;}
   if(phase==5&&r.Mode==RoverMode.Home&&rover.horizontalSrfSpeed<.1){Check(reloaded&&battery&&r.Index==2&&distance<6&&maxDistance>125&&maxSpeed>1&&maxSpeed<5&&feet>=3,"Drive/return acceptance failed");Check(rover.ActionGroups[KSPActionGroup.Brakes]&&Math.Abs(rover.ctrlState.wheelThrottle)<.001,"Home did not brake");var events=FleetEventApi.Timeline();Check(events.Any(x=>x.EventName=="ROVER_HOME")&&events.Count(x=>x.EventName=="WAYPOINT_REACHED")>=2,"Mission timeline omitted rover milestones");Note("PASS ALL ROVER EXPLORATION NATIVE OPERATIONS: displacement="+maxDistance+" maxSpeed="+maxSpeed+" finalDistance="+distance+" feet="+feet+" trace="+r.Trace.Length);done=true;Application.Quit();}
  }
 }
}
