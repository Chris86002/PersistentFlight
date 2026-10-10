using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using PersistentFlight;
using PersistentFlight.Regions;
[assembly:KSPAssembly("PersistentFlight.SoiTests",0,17)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public class SoiPlanHarness:MonoBehaviour {
    string result;int phase;float deadline,nextReport;double stamp,fuelBefore;bool loadedOnce,visitedObserver;ManeuverNode first,second;
    void Awake(){DontDestroyOnLoad(this);}
    void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-SOI-TEST] "+s);}
    void Check(bool b,string why){if(!b)throw new Exception(why);}
    IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-soi-plan-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+700;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}}
    void Prepare(){
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
                if(n==0){var c=cpu.CreateCopy();c.RemoveNodes("harddisk");c.SetValue("activated",i==1?"True":"False",true);c.SetValue("bootFile",i==1?"boot/pf_mission_test.ks":"None",true);p.AddNode(c);
                    var keep=p.AddNode("MODULE");keep.AddValue("name","ModulePersistentFlight");keep.AddValue("armed",i==2?"False":"True");keep.AddValue("rangeKm","60");
                    var auto=p.AddNode("MODULE");auto.AddValue("name","ModuleRegionalAutonomy");auto.AddValue("automatic",i==2?"False":"True");auto.AddValue("recordFlight","True");
                }
                if(n==2){var tag=p.AddNode("MODULE");tag.AddValue("name","KOSNameTag");tag.AddValue("nameTag","NODE_ENGINE");}v.AddNode(p);
            }
            if(i==1){var moon=FlightGlobals.Bodies.First(b=>b.bodyName=="Mun");var incoming=new Orbit();incoming.UpdateFromStateVectors(new Vector3d(moon.sphereOfInfluence-130000,0,0),new Vector3d(1000,200,0),moon,10000);var on=v.GetNode("ORBIT");on.SetValue("SMA",incoming.semiMajorAxis.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("ECC",incoming.eccentricity.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("INC",incoming.inclination.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("LPE",incoming.argumentOfPeriapsis.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("LAN",incoming.LAN.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("MNA",incoming.meanAnomalyAtEpoch.ToString("R",System.Globalization.CultureInfo.InvariantCulture));on.SetValue("EPH","10000");on.SetValue("REF",moon.flightGlobalsIndex.ToString());}
            fs.AddNode(v);
        }
        root.Save(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/mc.sfs"));HighLogic.SaveFolder="PersistentFlightTest";
        var game=GamePersistence.LoadGame("mc",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;game.flightState.activeVesselIdx=1;HighLogic.CurrentGame=game;game.Start();
    }

    void Update(){
        if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
        try{
            Check(Time.realtimeSinceStartup<deadline,"Native SOI timeout");
            var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");if(craft==null)return;
            var now=Planetarium.GetUniversalTime();var m=MissionControl.Modules(craft).FirstOrDefault();
            if(phase==0&&m!=null&&m.PilotOnline&&craft==FlightGlobals.ActiveVessel&&craft.patchedConicSolver!=null){
                Check(craft.mainBody.bodyName=="Mun","Escape fixture not on Mun");
                first=craft.patchedConicSolver.AddManeuverNode(now+35);first.OnGizmoUpdated(new Vector3d(0,0,3),first.UT);
                second=craft.patchedConicSolver.AddManeuverNode(now+230);second.OnGizmoUpdated(new Vector3d(0,0,5),second.UT);craft.patchedConicSolver.UpdateFlightPlan();stamp=now;phase=1;
            }
            else if(phase==1&&now-stamp>2){
                Check(second.patch!=null&&second.patch.referenceBody.bodyName=="Kerbin","Native future node did not cross to Kerbin");
                bool denied=false;try{MissionQueue.QueuePlan(craft,m);}catch(InvalidOperationException){denied=true;}Check(denied&&m.QueueCount==0,"Legacy plan accepted future-body node");
                MissionQueue.QueuePlan(craft,m,true);Check(m.soiMission&&m.QueueCount==2,"Cross-body plan not captured");fuelBefore=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount);
                Note("PASS native two-body map plan, legacy rejection and initial Mun burn activation");phase=2;
            }
            else if(phase==2&&m!=null&&!m.nodeRequested&&m.QueueCount==1&&!m.missionHold){
                Check(MissionControl.ReadOrbit(MissionQueue.Read(m.queuedNodes64).Single().Target).referenceBody.bodyName=="Kerbin","Future-body snapshot lost");
                var original=m.queuedNodes64;int departure=m.soiDepartureBody;try{m.soiDepartureBody=-1;MissionQueue.Advance(m);Check(m.missionHold&&m.queuedNodes64==original,"Unexpected-body hold erased queue");}finally{m.soiDepartureBody=departure;m.missionHold=false;m.roverHold=false;}
                bool conflict=false;try{PersistentFlightScenario.Instance.Queue(craft,now+60,1,"PROGRADE");}catch(InvalidOperationException){conflict=true;}Check(conflict,"Distant burn competed with waiting SOI plan");
                Check(!m.nodeRequested,"Future body burn became active on departure body");
                GamePersistence.SaveGame("soi-wait",HighLogic.SaveFolder,SaveMode.OVERWRITE);var game=GamePersistence.LoadGame("soi-wait",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;HighLogic.CurrentGame=game;phase=3;stamp=now;game.Start();Note("PASS completed Mun burn, waiting queue, held wrong-body check and ownership guard; cold loading waiting plan");
            }
            else if(phase==3&&m!=null&&m.PilotOnline&&now-stamp>5){
                Check(m.soiMission&&m.QueueCount==1&&!m.nodeRequested&&m.soiDepartureBody==craft.mainBody.flightGlobalsIndex,"Cold reload lost waiting state");
                loadedOnce=true;phase=4;stamp=now;var focus=FlightGlobals.Vessels.First(v=>v.vesselName=="MC FOCUS");visitedObserver=true;FleetDirector.Visit(focus);Note("PASS cold recovery while waiting; visiting distant Kerbin observer before natural SOI escape");
            }
            else if(phase==4){
                if(m!=null&&m.missionHold)throw new Exception(m.nodeStatus);
                if(craft.mainBody.bodyName=="Kerbin"&&m!=null&&m.nodeRequested){
                    Check(loadedOnce&&visitedObserver&&FlightGlobals.ActiveVessel==craft,"Arrival did not automatically visit sleeping craft");
                    Check(m.soiDepartureBody==craft.mainBody.flightGlobalsIndex,"Arrival did not reconcile expected body");
                    Check(FleetEventApi.Timeline().Any(e=>e.EventName=="SOI_PLAN_READY"),"Arrival timeline missing");
                    Note("PASS natural Mun -> Kerbin SOI transition, unloaded coast, automatic visit and checked arrival burn activation");phase=5;
                }
            }
            else if(phase==5&&m!=null){
                Check(!m.missionHold,"Arrival burn held: "+m.nodeStatus);
                if(!m.soiMission&&m.QueueCount==0&&m.nodeStatus=="Complete"){
                    Check(MissionQueue.Read(m.completedNodes64).Count==2,"Completion history lost first burn");
                    double actual=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount);
                    Check(actual<fuelBefore,"Burns did not consume fuel");var residual=MissionControl.ReadOrbit(m.nodeTargetOrbit).getOrbitalVelocityAtUT(m.nodeUT)-craft.orbit.getOrbitalVelocityAtUT(m.nodeUT);Check(residual.magnitude<.5,"Arrival residual too large");
                    Check(craft.ctrlState.mainThrottle<.001,"Arrival throttle remained open");Note("PASS ALL SOI PLAN NATIVE OPERATIONS: residual="+residual.magnitude+" fuel used="+(fuelBefore-actual));phase=6;stamp=now;
                }
            }
            else if(phase==6&&now-stamp>3){Application.Quit();}
            if(Time.realtimeSinceStartup>=nextReport){nextReport=Time.realtimeSinceStartup+10;Note("phase="+phase+" UT="+now+" body="+craft.mainBody.bodyName+" loaded="+craft.loaded+" state="+(m==null?"sleeping":m.nodeStatus)+" queue="+(m==null?"proto":m.QueueCount.ToString()));}
        }catch(Exception e){Note("FAIL "+e);Application.Quit();}
    }
}
