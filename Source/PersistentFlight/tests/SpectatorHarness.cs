using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;
using PersistentFlight;
using PersistentFlight.Regions;
[assembly: KSPAssembly("PersistentFlight.SpectatorTests",0,19)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class SpectatorHarness:MonoBehaviour {
    string result;float deadline;double stamp;int phase;
    Guid background;
    void Awake(){DontDestroyOnLoad(this);}
    void Note(string s){File.AppendAllText(result,s+Environment.NewLine);Debug.Log("[PF-SPECTATOR-TEST] "+s);}
    void Check(bool b,string why){if(!b)throw new Exception(why);}
    IEnumerator Start(){result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-spectator-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+450;yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}}
    void Safety(FleetSpectator viewer,Vessel craft,Vessel focus,ModuleRegionalAutonomy m){
        double fuel=MissionControl.Resource(craft,"LiquidFuel",out stamp);stamp=Planetarium.GetUniversalTime();
        viewer.StartTour();typeof(FleetSpectator).GetField("nextSwitch",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(viewer,-1f);
        double throttle=craft.ctrlState.mainThrottle;try{craft.ctrlState.mainThrottle=.5f;viewer.Tick();Check(FlightGlobals.ActiveVessel==craft&&viewer.Touring&&viewer.Status.Contains("Powered"),"Tour left powered craft");}finally{craft.ctrlState.mainThrottle=(float)throttle;viewer.StopTour();}
        bool requested=m.nodeRequested;double ut=m.nodeUT,dv=m.nodeInitialDV;try{m.nodeRequested=true;m.nodeUT=Planetarium.GetUniversalTime()+60;m.nodeInitialDV=1;Check(!viewer.NextLocal(1)&&FlightGlobals.ActiveVessel==craft,"Imminent burn focus guard failed");}finally{m.nodeRequested=requested;m.nodeUT=ut;m.nodeInitialDV=dv;}
        bool landed=craft.Landed;try{craft.Landed=true;Check(!viewer.NextLocal(1),"Tour entered surface flight");}finally{craft.Landed=landed;}
        bool held=m.missionHold;try{m.missionHold=true;Check(!viewer.NextLocal(1),"Tour left held mission");}finally{m.missionHold=held;}
        double capacity;Check(MissionControl.Resource(craft,"LiquidFuel",out capacity)==fuel,"Safety guards changed fuel");
        foreach(var e in craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Activate();
        var job=PersistentFlightScenario.Instance.Queue(craft,Planetarium.GetUniversalTime()+600,1,"PROGRADE");background=job.VesselId;
        Check(!viewer.NextLocal(1),"Tour left captured analytic job");
        Check(RegionFlightRunner.Instance.Focus(focus),"Direct test focus failed");
        Note("PASS native powered/held/surface/imminent-burn guards, read-only fuel and current analytic ownership");
    }

    void WarpAndIdle(FleetSpectator viewer){
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        viewer.StopTour();viewer.Visible=false;var before=viewer.Cards;typeof(FleetSpectator).GetField("nextCapture",flags).SetValue(viewer,-1f);viewer.Tick();Check(System.Object.ReferenceEquals(before,viewer.Cards),"Closed idle viewer scanned fleet");
        viewer.Visible=true;viewer.Tick();Check(!System.Object.ReferenceEquals(before,viewer.Cards),"Visible viewer failed to refresh");
        var modules=FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded).SelectMany(v=>MissionControl.Modules(v)).ToArray();var old=modules.Select(m=>m.guardWarp).ToArray();
        var saved=FlightGlobals.Vessels.Where(v=>v!=null&&!v.loaded&&v.protoVessel!=null).SelectMany(v=>v.protoVessel.protoPartSnapshots.SelectMany(p=>p.modules)).Where(m=>m.moduleName=="ModuleRegionalAutonomy").ToArray();var previous=saved.Select(m=>m.moduleValues.GetValue("guardWarp")).ToArray();
        var active=FlightGlobals.ActiveVessel;
        try{
            foreach(var m in modules)m.guardWarp=false;foreach(var m in saved)m.moduleValues.SetValue("guardWarp","False",true);
            TimeWarp.SetRate(1,true);Check(TimeWarp.CurrentRate>1,"Native warp fixture did not enter warp");Check(!viewer.NextLocal(1)&&FlightGlobals.ActiveVessel==active,"Spectator switched during warp");
        }finally{TimeWarp.SetRate(0,true);for(int i=0;i<modules.Length;i++)modules[i].guardWarp=old[i];for(int i=0;i<saved.Length;i++){if(previous[i]==null)saved[i].moduleValues.RemoveValues("guardWarp");else saved[i].moduleValues.SetValue("guardWarp",previous[i],true);}}
        Note("PASS visible-panel native tour, idle scan suppression and actual warp switch guard");
    }

    void Update(){
        if(result==null||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
        try{
            Check(Time.realtimeSinceStartup<deadline,"Spectator native timeout");
            var viewer=FleetSpectator.Instance;if(viewer==null)return;
            var focus=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC FOCUS");var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");var remote=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC REMOTE");
            if(craft==null||focus==null||remote==null)return;double now=Planetarium.GetUniversalTime();
            if(phase==0&&craft.loaded&&!craft.packed&&MissionControl.Pilot(craft)!=null&&now>10008){
                foreach(var module in remote.protoVessel.protoPartSnapshots.SelectMany(p=>p.modules).Where(p=>p.moduleName=="ModuleRegionalAutonomy")){module.moduleValues.SetValue("automatic","True",true);module.moduleValues.SetValue("missionPhase","Remote coast",true);}
                viewer.Refresh();var before=FlightGlobals.ActiveVessel;double cap,fuel=MissionControl.Resource(craft,"LiquidFuel",out cap);viewer.Watch(remote.id);
                var card=viewer.Cards.Single(c=>c.Id==remote.id);Check(card.Phase=="Saved: Remote coast"&&!card.Loaded&&card.Body=="Mun","Remote status not truthful");Check(FlightGlobals.ActiveVessel==before&&MissionControl.Resource(craft,"LiquidFuel",out cap)==fuel,"Status view changed focus or resources");
                FleetEventApi.Publish(0,remote.id,"SPECTATOR_TEST","Saved remote mission observed");
                var panel=FindObjectOfType<RegionalFleetPanel>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(RegionalFleetPanel).GetField("showSpectator",flags).SetValue(panel,true);typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});
                viewer.PinCurrent();Check(viewer.Watched==before.id&&!viewer.FollowFocus&&!viewer.Touring,"Pin state wrong");viewer.Interval=5;viewer.StartTour();stamp=now;phase=1;
                Note("PASS remote saved status, observational selection and native pin; auto tour started");
            }
            else if(phase==1&&FlightGlobals.ActiveVessel==craft&&now-stamp>5){
                Check(viewer.Touring&&viewer.FollowFocus,"Tour did not stay active");Check(!remote.loaded,"Tour loaded distant craft");Check(viewer.NextLocal(-1),"Previous local craft failed");stamp=now;phase=2;Note("PASS automatic local native focus, distant craft excluded and previous-craft request");
            }
            else if(phase==2&&FlightGlobals.ActiveVessel==focus&&now-stamp>1){Check(RegionFlightRunner.Instance.Focus(craft),"Manual test focus failed");stamp=now;phase=3;}
            else if(phase==3&&FlightGlobals.ActiveVessel==craft&&now-stamp>1){
                Check(!viewer.Touring&&viewer.Status.Contains("outside"),"Manual focus did not pause tour");Safety(viewer,craft,focus,MissionControl.Modules(craft).First());stamp=now;phase=4;
            }
            else if(phase==4&&FlightGlobals.ActiveVessel==focus&&now-stamp>1){
                viewer.Refresh();viewer.Watch(craft.id);bool denied=false;try{viewer.VisitWatched();}catch(InvalidOperationException){denied=true;}Check(denied&&FlightGlobals.ActiveVessel==focus&&!PersistentFlightScenario.Instance.Find(background).Finished,"Spectator interfered with target analytic job");PersistentFlightScenario.Instance.Cancel(background);
                foreach(var e in craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Shutdown();viewer.Refresh();viewer.Watch(remote.id);viewer.VisitWatched();phase=5;stamp=now;Note("PASS target analytic job visit blocked; manual native visit to Mun requested");
            }
            else if(phase==5&&FlightGlobals.ActiveVessel==remote&&remote.loaded&&!remote.packed&&now-stamp>2){
                Check(!viewer.Touring,"Tour resumed across native scene load");viewer.Refresh();Check(viewer.Cards.Single(c=>c.Id==remote.id).Loaded,"Visited card stayed asleep");Check(remote.mainBody.bodyName=="Mun","Wrong remote body");viewer.Watch(craft.id);viewer.VisitWatched();phase=6;stamp=now;Note("PASS manual Mun visit, refreshed native status and tour stopped across reload; returning to Kerbin");
            }
            else if(phase==6&&FlightGlobals.ActiveVessel==craft&&craft.loaded&&!craft.packed&&now-stamp>2){
                Check(!viewer.Touring,"Tour resumed on return");viewer.Refresh();viewer.Watch(remote.id);
                var panel=FindObjectOfType<RegionalFleetPanel>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(RegionalFleetPanel).GetField("showSpectator",flags).SetValue(panel,true);typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});phase=7;stamp=now;
            }
            else if(phase==7&&now-stamp>2){ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"fleet-spectator-preview.png"));phase=8;stamp=now;}
            else if(phase==8&&now-stamp>2){WarpAndIdle(viewer);Note("PASS ALL SPECTATOR NATIVE OPERATIONS");phase=9;stamp=now;Application.Quit();}
        }catch(Exception e){Note("FAIL "+e);Application.Quit();}
    }
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
                if(n==0){var c=cpu.CreateCopy();c.RemoveNodes("harddisk");c.SetValue("activated",i!=2?"True":"False",true);c.SetValue("bootFile",i==1?"boot/pf_mission_test.ks":i==0?"boot/pf_event_test.ks":"None",true);p.AddNode(c);
                    var keep=p.AddNode("MODULE");keep.AddValue("name","ModulePersistentFlight");keep.AddValue("armed",i==2?"False":"True");keep.AddValue("rangeKm","60");
                    var auto=p.AddNode("MODULE");auto.AddValue("name","ModuleRegionalAutonomy");auto.AddValue("automatic",i==2?"False":"True");auto.AddValue("recordFlight","True");
                }
                if(n==2){var tag=p.AddNode("MODULE");tag.AddValue("name","KOSNameTag");tag.AddValue("nameTag","NODE_ENGINE");}v.AddNode(p);
            }
            fs.AddNode(v);
        }
        root.Save(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/mc.sfs"));HighLogic.SaveFolder="PersistentFlightTest";
        var game=GamePersistence.LoadGame("mc",HighLogic.SaveFolder,true,false);game.startScene=GameScenes.FLIGHT;game.flightState.activeVesselIdx=0;HighLogic.CurrentGame=game;game.Start();
    }

}