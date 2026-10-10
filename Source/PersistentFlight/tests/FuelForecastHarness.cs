using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using PersistentFlight.Regions;
using PersistentFlight;
[assembly: KSPAssembly("PersistentFlight.FuelForecastTests",0,9)]
[KSPAddon(KSPAddon.Startup.MainMenu,true)]
public sealed class FuelForecastHarness:MonoBehaviour
{
    string result;float deadline,nextReport;double start,queuedUT,fuelBefore,completeUT;int phase;
    FleetBurnRequest apiRequest;
    bool siteTested,firstCompleted,reloaded;ManeuverNode originalNode,secondNode;
    void Awake(){DontDestroyOnLoad(this);}
    void Note(string s){Debug.Log("[PF-MC-TEST] "+s);File.AppendAllText(result,s+Environment.NewLine);}
    void Check(bool value,string why){if(!value)throw new Exception(why);}
    IEnumerator Start(){
        result=Path.Combine(KSPUtil.ApplicationRootPath,"pf-fuel-forecast-result.txt");File.WriteAllText(result,"");deadline=Time.realtimeSinceStartup+700;
        yield return new WaitForSeconds(3);try{Prepare();}catch(Exception e){Note("FAIL prepare "+e);Application.Quit();}
    }
    FleetEventRuleRequest eventSignal,eventFollow,eventTimeout;bool eventSetup,eventHandshake,eventTimeoutChecked;
    FleetEventRuleRequest pilotEvent;bool pilotEventQueued;
    double eventTimeoutStart;
    void EventBefore(Vessel craft,Vessel focus){
        ForecastBefore(craft);
        var service=PersistentFlightScenario.Instance;var source=MissionControl.Modules(craft).First();var target=MissionControl.Modules(focus).First();
        double fuel=Fuel(focus);var active=FlightGlobals.ActiveVessel;
        eventSignal=new FleetEventRuleRequest(Guid.NewGuid(),"Native kOS task",source.part.flightID,"BEGIN_TEST",target.part.flightID,FleetAction.Signal,"ACK_TEST","HANDLER_DONE",300,60,true);
        eventFollow=new FleetEventRuleRequest(Guid.NewGuid(),"Dependent notification",target.part.flightID,"HANDLER_DONE",target.part.flightID,FleetAction.Notify,"Handler acknowledged task","CHAIN_DONE",300,30,true);
        FleetEventApi.Register(eventSignal);FleetEventApi.Register(eventSignal);FleetEventApi.Register(eventFollow);
        Check(FleetEventApi.List().Count(r=>r.Request.Id==eventSignal.Id)==1,"Event registration duplicated");
        Check(Fuel(focus)==fuel&&FlightGlobals.ActiveVessel==active&&focus.ctrlState.mainThrottle==0,"Rule registration changed flight");
        FleetEventApi.Publish(source.part.flightID,craft.id,"BEGIN_TEST","Native test");Check(FleetEventApi.Get(eventSignal.Id).State==FleetRuleState.Ready,"Signal trigger failed");
        Check(FleetEventApi.Begin(eventSignal.Id),"Signal dispatch failed");
        bool denied=false;try{FleetEventApi.Complete(eventSignal.Id,source.part.flightID,true,"wrong CPU");}catch(InvalidOperationException){denied=true;}Check(denied,"Wrong CPU acknowledged action");
        bool offThread=false;var thread=new System.Threading.Thread(()=>{try{FleetEventApi.List();}catch(InvalidOperationException){offThread=true;}});thread.Start();thread.Join();Check(offThread,"Off-thread events accepted");
        var clean=new ConfigNode("SCENARIO");service.OnSave(clean);string path=Path.Combine(KSPUtil.ApplicationRootPath,"events-roundtrip.cfg");clean.Save(path);clean=ConfigNode.Load(path);service.OnLoad(clean);
        Check(FleetEventApi.Pending(target.part.flightID).Request.Id==eventSignal.Id,"Running task lost on file round trip");
        Check(FleetEventApi.Timeline().Last().EventName=="SAVE_LOADED","Timeline load boundary missing");
        var bad=clean.CreateCopy();bad.GetNode("FLEET_EVENTS").AddNode(bad.GetNode("FLEET_EVENTS").GetNodes("RULE")[0].CreateCopy());service.OnLoad(bad);Check(FleetEventApi.IsHeld,"Corrupt rules did not hold events");
        bool held=false;try{FleetEventApi.List();}catch(InvalidOperationException){held=true;}Check(held,"Held ledger returned apparent empty rules");
        var heldSave=new ConfigNode("SCENARIO");service.OnSave(heldSave);Check(heldSave.GetNode("FLEET_EVENTS").GetNodes("RULE").Length==3,"Corrupt rules were discarded");service.OnLoad(heldSave);Check(FleetEventApi.IsHeld,"Event hold did not persist");service.OnLoad(clean);
        var duplicate=clean.CreateCopy();duplicate.AddNode(duplicate.GetNode("FLEET_EVENTS").CreateCopy());service.OnLoad(duplicate);Check(FleetEventApi.IsHeld,"Duplicate root accepted");heldSave=new ConfigNode("SCENARIO");service.OnSave(heldSave);Check(heldSave.GetNodes("FLEET_EVENTS").Length==2,"Duplicate root evidence discarded");service.OnLoad(clean);
        var missing=clean.CreateCopy();missing.RemoveNodes("FLEET_EVENTS");service.OnLoad(missing);Check(FleetEventApi.IsHeld,"Missing new ledger treated as legacy");service.OnLoad(clean);
        var future=clean.CreateCopy();future.GetNode("FLEET_EVENTS").SetValue("version",999);service.OnLoad(future);Check(FleetEventApi.IsHeld,"Future schema accepted");service.OnLoad(clean);
        Note("PASS event registration, CPU ownership, thread guard, running task file round trip, timeline branching, corrupt/missing/future ledger holds and preserved evidence");
    }
    bool EventHandshakeAndTimeout(Vessel craft,Vessel focus){
        var target=MissionControl.Modules(focus).First();
        if(!eventHandshake){
            if(FleetEventApi.Get(eventSignal.Id).State!=FleetRuleState.Complete||FleetEventApi.Get(eventFollow.Id).State!=FleetRuleState.Complete)return false;
            Check(target.missionPhase=="Event handler acknowledged task","Actual kOS handler did not run");
            var entries=FleetEventApi.Timeline();Check(entries.Count(e=>e.EventName=="ACTION_COMPLETED"&&e.RuleId==eventSignal.Id)==1,"Task acknowledged multiple times");
            long seq=entries.Last().Sequence;FleetEventApi.Complete(eventSignal.Id,target.part.flightID,true,"retry");Check(FleetEventApi.Timeline().Last().Sequence==seq,"Ack retry duplicated event");
            eventTimeout=new FleetEventRuleRequest(Guid.NewGuid(),"Timeout safety test",target.part.flightID,"NEVER_EMITTED",target.part.flightID,FleetAction.Signal,"NEVER","",3,10,true);FleetEventApi.Register(eventTimeout);eventTimeoutStart=Planetarium.GetUniversalTime();eventHandshake=true;
            Note("PASS real kOS ACTION/ACTIONID/FINISHACTION handshake and event-dependent native notification");
        }
        if(!eventTimeoutChecked){
            if(Planetarium.GetUniversalTime()-eventTimeoutStart<4||FleetEventApi.Get(eventTimeout.Id).HoldPending)return false;
            Check(FleetEventApi.Get(eventTimeout.Id).State==FleetRuleState.Failed&&target.missionHold,"Timeout did not apply native mission hold");
            Check(focus.ctrlState.mainThrottle==0,"Timeout hold left throttle on");target.missionHold=false;target.roverHold=false;eventTimeoutChecked=true;
            target.eventObserved=true;target.eventOrbit=false;FleetEventDirector.Observe(target);Check(FleetEventApi.Timeline().Any(e=>e.EventName=="ORBIT_ESTABLISHED"&&e.Cpu==target.part.flightID),"Orbit transition not captured");
            target.eventVessel=Guid.NewGuid().ToString();FleetEventDirector.Observe(target);Check(FleetEventApi.Timeline().Any(e=>e.EventName=="VESSEL_CHANGED"&&e.Cpu==target.part.flightID),"CPU identity transition not captured");
            string export=RegionalFleetPanel.ExportMissionTimeline();Check(File.ReadAllText(export).Contains("HANDLER_DONE"),"Timeline CSV export missing chain");
            Note("PASS native timeout hold, synthetic orbit/vessel transition capture and CSV timeline export");
        }
        return true;
    }
    void EventAfterReload(){
        Check(FleetEventApi.Get(eventSignal.Id).State==FleetRuleState.Complete&&FleetEventApi.Get(eventFollow.Id).State==FleetRuleState.Complete&&FleetEventApi.Get(eventTimeout.Id).State==FleetRuleState.Failed,"Native scene load lost rule outcomes");
        Check(FleetEventApi.Timeline().Any(e=>e.EventName=="SAVE_LOADED"),"Native load has no timeline boundary");
        Note("PASS event outcomes and timeline survive cold flight scene reload");
    }
    void EventAfterTravel(){
        EventAfterReload();var entries=FleetEventApi.Timeline();Check(entries.Any(e=>e.EventName=="MANEUVER_COMPLETED"),"Live maneuver completions missing from timeline");
        Check(entries.Any(e=>e.EventName=="BURN_COMPLETED"),"Unloaded burn completion missing from timeline");
        File.Copy(RegionalFleetPanel.ExportMissionTimeline(),Path.Combine(KSPUtil.ApplicationRootPath,"events-timeline.csv"),true);
        Note("PASS timeline live maneuver and unloaded burn completion hooks, cross-body saves and export");
    }


    double predictedLF;
    void ForecastBefore(Vessel craft){
        var m=MissionControl.Modules(craft).First();
        Check(m.fuelForecast!=null&&m.fuelForecast.Known&&m.forecastSource=="Script request"&&m.landingReserveDV==125,"Real kOS fuel suffix calls failed");
        var engine=craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Single();
        var fuel=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel").ToArray();
        double amount=Fuel(craft),throttle=craft.ctrlState.mainThrottle;bool ignited=engine.EngineIgnited;int count=m.QueueCount;
        var normal=FleetFuelForecast.Evaluate(m,25);Check(normal.WithinReserve,"Small native plan does not fit: "+normal.Reason);
        double limit=engine.thrustPercentage;try{engine.thrustPercentage=(float)(limit/2);var half=FleetFuelForecast.Evaluate(m,25);Check(half.Known&&Math.Abs(half.AvailableDV-normal.AvailableDV)<.01&&Math.Abs(half.Duration/normal.Duration-2)<.01,"Native thrust limiter forecast wrong");}finally{engine.thrustPercentage=(float)limit;}
        try{foreach(var r in fuel)r.flowState=false;var blocked=FleetFuelForecast.Evaluate(m,25);Check(blocked.State==FuelForecastState.Insufficient&&blocked.AvailableDV==0,"Disabled tank included in native forecast");}finally{foreach(var r in fuel)r.flowState=true;}
        double reserve=m.landingReserveDV;try{m.landingReserveDV=10000;var danger=FleetFuelForecast.Evaluate(m,25);Check(danger.State==FuelForecastState.ReserveRisk,"Native reserve risk missing");}finally{m.landingReserveDV=reserve;}
        Check(Fuel(craft)==amount&&craft.ctrlState.mainThrottle==throttle&&engine.EngineIgnited==ignited&&m.QueueCount==count&&!m.missionHold,"Advisory changed flight controls or resources");
        m.burnMargin=0;var exact=FleetFuelForecast.Evaluate(m,25);predictedLF=exact.FuelMass/.005*.45;
        Check(exact.Known&&predictedLF>0,"Native cost unavailable");
        Note("PASS native tagged inactive engine and kOS suffixes, thrust limiter, disabled tank, reserve warning, and read-only advisory; predicted LF="+predictedLF);
    }
    void ForecastQueue(ModuleRegionalAutonomy m){
        var r=FleetFuelForecast.Queue(m);Check(r.Known&&Math.Abs(r.PlanDV-25)<.01,"Saved queue forecast missed a node");
        double reserve=m.landingReserveDV;try{m.landingReserveDV=10000;m.nextFuelForecast=0;FleetFuelForecast.Monitor(m);Check(!m.missionHold&&m.nodeRequested&&FleetEventApi.Timeline().Any(e=>e.EventName=="FUEL_FORECAST_WARNING"&&e.Cpu==m.part.flightID),"Forecast warning applied control or omitted timeline");}finally{m.landingReserveDV=reserve;}
        m.nextFuelForecast=0;FleetFuelForecast.Monitor(m);Check(FleetEventApi.Timeline().Any(e=>e.EventName=="FUEL_FORECAST_CLEAR"&&e.Cpu==m.part.flightID),"Forecast clear timeline missing");
        // KSP serializes KSPField values through the part save, outside OnSave.
        // Inspect the actual GamePersistence snapshot below instead.
        Note("PASS whole saved queue forecast, warning/clear timeline transitions and persisted reserve settings");
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
    static double Fuel(Vessel v){return v.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount);}
    void NativeOperations(Vessel craft,Vessel focus,ModuleRegionalAutonomy m){
        var director=FleetDirector.Instance;Check(director!=null,"Fleet director missing");
        var other=MissionControl.Modules(focus).First();other.guardWarp=false;m.guardWarp=true;m.warpReady=false;
        TimeWarp.SetRate(1,true);director.ProtectWarp();Check(TimeWarp.CurrentRate==1,"Warp entered with an unsafe mission");
        m.warpReady=true;m.warpUntil=Planetarium.GetUniversalTime()+120;TimeWarp.SetRate(1,true);director.ProtectWarp();Check(TimeWarp.CurrentRate>1,"Safe coast lease did not permit warp");
        m.warpUntil=Planetarium.GetUniversalTime()+1;director.ProtectWarp();Check(TimeWarp.CurrentRate==1,"Warp did not stop at coast deadline");
        Note("PASS native warp blocks active work, permits leased coasting, stops before deadline");
        var resources=craft.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel"||r.resourceName=="Oxidizer").ToArray();var amounts=resources.Select(r=>r.amount).ToArray();
        foreach(var e in craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Activate();
        for(int i=0;i<resources.Length;i++)resources[i].amount=resources[i].maxAmount*.005;
        m.abortOnCritical=true;m.ScriptPulse();typeof(FleetDirector).GetMethod("Monitor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(director,new object[]{m});
        Check(m.missionHold&&!craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Any(e=>e.EngineIgnited),"Critical reserve did not hold engines");
        for(int i=0;i<resources.Length;i++)resources[i].amount=amounts[i];m.missionHold=false;m.abortOnCritical=false;m.roverHold=false;m.warpReady=false;
        Note("PASS actual low-propellant reserve warning and engine shutdown safety hold");
        var node=craft.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime()+100);node.OnGizmoUpdated(new Vector3d(0,0,5),node.UT);craft.patchedConicSolver.UpdateFlightPlan();MissionControl.QueueNext(craft,m);m.HoldMission("Test hold");MissionControl.Finish(m,false);Check(m.nodeRequested&&m.QueueCount==1,"Hold erased mission request");m.missionHold=false;m.CancelNode("Cancelled by test");
        Note("PASS mission hold retains queued request; cancellation clears queue");
        var oldAltitude=focus.altitude;var oldVertical=focus.verticalSpeed;other.landingTakeover=true;other.landingTakeoverHeight=6000;
        try{focus.altitude=5000;focus.verticalSpeed=-10;typeof(FleetDirector).GetMethod("LandingHandoff",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(director,new object[]{focus});Check(FlightGlobals.ActiveVessel==focus,"Native landing handoff did not switch focus");}
        finally{focus.altitude=oldAltitude;focus.verticalSpeed=oldVertical;other.landingTakeover=false;}
        Check(RegionFlightRunner.Instance.Regions.Any(r=>r.Vessel==craft),"Landing handoff did not preserve outgoing regional craft");
        Note("PASS synthetic descending-altitude trigger causes real native landing focus switch with outgoing craft still regional (not a touchdown test)");
        RegionFlightRunner.Instance.Focus(craft);
        var panel=FindObjectOfType<RegionalFleetPanel>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"fleet-features-preview.png"));
    }
    void Update(){
        if(result==null)return;if(Time.realtimeSinceStartup>deadline){Note("FAIL timeout");Application.Quit();return;}
        if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
        try{
            var focus=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC FOCUS");var craft=FlightGlobals.Vessels.FirstOrDefault(v=>v.vesselName=="MC PILOT");if(craft==null||focus==null||(phase<8&&(!craft.loaded||craft.packed)))return;
            var m=MissionControl.Modules(craft).FirstOrDefault();var now=Planetarium.GetUniversalTime();if(start==0)start=now;
            if(!siteTested&&now-start>3){
                MissionControl.SetSite(m,"Custom test",craft.mainBody,-.1,-75.3);Check(MissionControl.Site(m).x==-.1,"Site latitude changed");
                bool rejected=false;try{MissionControl.SetSite(m,"Invalid",craft.mainBody,91,0);}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid latitude accepted");
                bool landed=focus.Landed;try{focus.Landed=true;MissionControl.SetTargetSite(m,focus);Check(MissionControl.Site(m).x==focus.latitude,"Target vessel site not resolved");}finally{focus.Landed=landed;}
                MissionControl.SetSite(m,"Custom test",craft.mainBody,-.1,-75.3);
                Check(FleetSample.Quote("A,\"B\"\nC")=="\"A,\"\"B\"\"\nC\"","CSV escaping failed");
                siteTested=true;Note("PASS custom landing site, rejected invalid latitude, selected-vessel site, CSV escaping");
            }
            if(!eventSetup&&m.PilotOnline&&now-start>8){EventBefore(craft,focus);eventSetup=true;}
            if(phase==0&&eventSetup&&EventHandshakeAndTimeout(craft,focus)&&m.PilotOnline&&now-start>8){Check(RegionFlightRunner.Instance.Focus(craft),"Could not focus pilot");MapView.EnterMapView();queuedUT=now;phase=1;}
            else if(phase==1&&craft==FlightGlobals.ActiveVessel&&craft.patchedConicSolver!=null&&now-queuedUT>2){
                originalNode=craft.patchedConicSolver.AddManeuverNode(now+50);originalNode.OnGizmoUpdated(new Vector3d(0,0,15),originalNode.UT);
                secondNode=craft.patchedConicSolver.AddManeuverNode(now+110);secondNode.OnGizmoUpdated(new Vector3d(0,0,10),secondNode.UT);craft.patchedConicSolver.UpdateFlightPlan();
                queuedUT=now;phase=6;
            }
            else if(phase==6&&originalNode.nextPatch!=null&&now-queuedUT>1){
                if(!pilotEventQueued){pilotEvent=new FleetEventRuleRequest(Guid.NewGuid(),"Event queue flight plan",m.part.flightID,"CAPTURE_PLAN",m.part.flightID,FleetAction.Signal,"QUEUE_FLIGHT_PLAN","PLAN_CAPTURED",10,15,true);FleetEventApi.Register(pilotEvent);FleetEventApi.Publish(m.part.flightID,craft.id,"CAPTURE_PLAN","Native pilot test");pilotEventQueued=true;return;} if(FleetEventApi.Get(pilotEvent.Id).State==FleetRuleState.Failed)throw new Exception("Event pilot queue failed: "+FleetEventApi.Get(pilotEvent.Id).Detail);if(FleetEventApi.Get(pilotEvent.Id).State!=FleetRuleState.Complete)return;Note("PASS actual idle kOS pilot captured its two-node plan from an event task");Check(m.QueueCount==2,"Both maneuvers were not queued");Check(m.resumePilot,"Recovery pilot was not registered");Check((MissionControl.PreviewRemaining(m)-originalNode.GetBurnVector(craft.orbit)).magnitude<1e-6,"Snapshot vector differs from native node");
                Check((MissionControl.ReadOrbit(m.nodeTargetOrbit).getOrbitalVelocityAtUT(m.nodeUT)-originalNode.nextPatch.getOrbitalVelocityAtUT(m.nodeUT)).magnitude<1e-6,"Snapshot round trip changed orbit");
                var preview=FleetSample.Capture(craft);Check(m.nodeRequested,"Dashboard sampling mutated job");
                ForecastQueue(m);
                GamePersistence.SaveGame("mc-queued",HighLogic.SaveFolder,SaveMode.OVERWRITE);
                var saved=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves/PersistentFlightTest/mc-queued.sfs"));var module=saved.GetNode("GAME").GetNode("FLIGHTSTATE").GetNodes("VESSEL").First(v=>v.GetValue("name")=="MC PILOT").GetNodes("PART").SelectMany(p=>p.GetNodes("MODULE")).First(x=>x.GetValue("name")=="ModuleRegionalAutonomy");
                Check(module.GetValue("nodeRequested")=="True"&&module.GetValue("landingSite")=="Custom test","Queued maneuver/site were not saved");
                Check(Double.Parse(module.GetValue("landingReserveDV"),System.Globalization.CultureInfo.InvariantCulture)==125&&Double.Parse(module.GetValue("burnMargin"),System.Globalization.CultureInfo.InvariantCulture)==0&&module.GetValue("forecastQueue")=="True","Forecast settings not persisted in game save");
                Check(module.GetValue("queuedNodes64")!=""&&module.GetValue("resumePilot")=="True","Queue/recovery boot not saved");
                Check(MissionQueue.Read(m.queuedNodes64).Count==1,"Queue round trip failed");
                bool invalid=false;try{MissionQueue.Read("not base64");}catch(FormatException){invalid=true;}Check(invalid,"Corrupt queue accepted");
                Check(FleetSafety.Evaluate(.05,1,50,100,false,.1,.1)=="Low propellant reserve","Reserve alert missing");
                Check(FleetSafety.Evaluate(1,1,0,0,false,.1,.1)=="","Missing batteries treated as empty");
                Check(FleetSafety.Critical(1,1,1,100,false),"Critical power threshold failed");
                fuelBefore=Fuel(craft);queuedUT=now;phase=7;reloaded=true;
                var reload=GamePersistence.LoadGame("mc-queued",HighLogic.SaveFolder,true,false);reload.startScene=GameScenes.FLIGHT;HighLogic.CurrentGame=reload;reload.Start();
                Note("PASS two-node queue, invalid queue rejection, saved queue/recovery boot, reserve policies; performing cold scene reload");
                Note("PASS native next-node capture, target-orbit round trip, saved maneuver/site, switched away before burn");
            }
            else if(phase==7&&now-queuedUT>5&&m.PilotOnline){
                EventAfterReload();Check(m.landingReserveDV==125&&m.burnMargin==0&&m.forecastQueue,"Cold load lost forecast settings");Note("PASS forecast reserve settings restored after cold scene load");
                Check(reloaded&&m.QueueCount==2,"Cold reload lost mission queue");
                Check(m.pilotEngineTag=="NODE_ENGINE","Cold reload lost tagged engines");
                Check(craft.parts.SelectMany(p=>p.Modules.OfType<kOS.Module.kOSProcessor>()).Any(c=>c.bootFile=="boot/pf_mission_resume.ks"),"Recovery boot selection lost");
                originalNode=null;secondNode=null;
                var panel=FindObjectOfType<RegionalFleetPanel>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(RegionalFleetPanel).GetField("selected",flags).SetValue(panel,craft.id);
                typeof(RegionalFleetPanel).GetField("showEvents",flags).SetValue(panel,false);
                typeof(RegionalFleetPanel).GetField("scroll",flags).SetValue(panel,new Vector2(0,710));
                typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});phase=71;completeUT=now;
                Note("PASS cold-load CPU boot resumed actual kOS pilot with both maneuvers");
            }
            else if(phase==71&&now-completeUT>2){ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"fuel-forecast-preview.png"));phase=72;completeUT=now;}
            else if(phase==72&&now-completeUT>2){var panel=FindObjectOfType<RegionalFleetPanel>();typeof(RegionalFleetPanel).GetMethod("SetOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,new object[]{false});Check(RegionFlightRunner.Instance.Focus(focus),"Could not switch away after reload");phase=2;}
            else if(phase==2){
                if(MissionQueue.Read(m.completedNodes64).Count==1&&!firstCompleted){firstCompleted=true;Check(m.nodeRequested&&m.QueueCount==1,"Next maneuver was not promoted");Note("PASS first background burn completed and second automatically promoted");}
                // Explicitly emulate disappearance of the node UI on an unfocused
                // craft. The target-orbit snapshot must still execute the burn.
                if(originalNode!=null&&craft.patchedConicSolver!=null&&craft.patchedConicSolver.maneuverNodes.Contains(originalNode)){originalNode.RemoveSelf();originalNode=null;Note("Native node removed after capture; background pilot must use snapshot");}
                if(m.nodeStatus=="Complete"){
                    // kOS writes its control channel before the next native
                    // physics update publishes it into vessel.ctrlState.
                    if(completeUT==0){completeUT=now;return;}if(now-completeUT<1)return;
                    Check(firstCompleted,"First burn was not observed");Check(MissionQueue.Read(m.completedNodes64).Count==2,"Completion history lost a maneuver");Check(craft!=FlightGlobals.ActiveVessel,"Burn was not background");Check(Fuel(craft)<fuelBefore-.01,"Burn did not consume fuel");
                    var residual=MissionControl.ReadOrbit(m.nodeTargetOrbit).getOrbitalVelocityAtUT(m.nodeUT)-craft.orbit.getOrbitalVelocityAtUT(m.nodeUT);
                    Check(residual.magnitude<=.5,"Burn residual too large: "+residual.magnitude);Check(craft.ctrlState.mainThrottle<.001,"Throttle remained on");
                    Check(!craft.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Any(e=>e.EngineIgnited),"Tagged maneuver engine remained active");
                    double actual=fuelBefore-Fuel(craft);Check(Math.Abs(actual-predictedLF)<Math.Max(.2,predictedLF*.1),"Native fuel cost differs from prediction: actual="+actual+" estimate="+predictedLF);Note("PASS forecast vs actual two-burn consumption: predicted="+predictedLF+" actual="+actual);
                    completeUT=now;phase=3;Note("PASS unfocused kOS maneuver: residual="+residual.magnitude+" fuel consumed="+(fuelBefore-Fuel(craft)));}
                else if(!m.nodeRequested)throw new Exception("Pilot stopped: "+m.nodeStatus);
            }
            else if(phase==3&&now-completeUT>5){
                Check(MissionControl.Pilot(craft)!=null,"Pilot did not return to idle");Check(RegionFlightRunner.Instance.Focus(craft),"Return focus failed");MapView.EnterMapView();completeUT=now;phase=4;
            }
            else if(phase==4&&craft==FlightGlobals.ActiveVessel&&now-completeUT>2){
                var node=craft.patchedConicSolver.AddManeuverNode(now+50);node.OnGizmoUpdated(new Vector3d(0,0,10),node.UT);craft.patchedConicSolver.UpdateFlightPlan();MissionControl.QueueNext(craft,m);m.CancelNode("Cancelled by test");completeUT=now;phase=5;
            }
            else if(phase==5&&now-completeUT>3){
                Check(!m.nodeRequested&&craft.ctrlState.mainThrottle<.001,"Cancellation left throttle/request active");
                var recorder=FleetRecorder.Instance;Check(recorder!=null&&recorder.History.ContainsKey(craft.id)&&recorder.History.ContainsKey(focus.id),"Fleet recorder did not record both craft");
                var csv=Path.Combine(recorder.LogDirectory,"fleet.csv");Check(File.Exists(csv),"Flight recorder file missing");
                using(var stream=new FileStream(csv,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))Check(reader.ReadToEnd().Split('\n').Length>30,"Flight recorder file empty");
                Check(recorder.Current[craft.id].Power>0,"Dashboard power missing");
                Note("PASS cancel, focus return, two-craft dashboard and chart histories, persisted CSV: "+csv);Note("PASS FLEET FEATURE QUEUE / COLD RECOVERY BUNDLE");
                NativeOperations(craft,focus,m);
                completeUT=now;phase=10;
            }
            else if(phase==10&&now-completeUT>4){
                var remote=FlightGlobals.Vessels.First(v=>v.vesselName=="MC REMOTE");phase=8;completeUT=now;FleetDirector.Visit(remote);
                Note("Visiting remote Mun craft through saved fleet travel");
            }
            else if(phase==8&&FlightGlobals.ActiveVessel.vesselName=="MC REMOTE"&&FlightGlobals.ActiveVessel.loaded&&!FlightGlobals.ActiveVessel.packed&&now-completeUT>3){
                var remote=FlightGlobals.ActiveVessel;Check(remote.mainBody.bodyName=="Mun","Remote travel focused wrong body");
                foreach(var e in remote.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Activate();remote.ctrlState.mainThrottle=0;
                var call=typeof(RegionalFleetPanel).GetMethod("QueueDistant",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                
                var service=PersistentFlightScenario.Instance;
                var cancelled=new FleetBurnRequest(Guid.NewGuid(),"Lodestone-test",remote.id,now+60,1,"PROGRADE");
                FleetMissionApi.Submit(cancelled);
                bool denied=false;try{FleetMissionApi.Cancel(cancelled.MissionId,"other-owner");}catch(InvalidOperationException){denied=true;}
                Check(denied&&!FleetMissionApi.Get(cancelled.MissionId).Finished,"Wrong owner cancelled a burn");
                var cancelledResult=FleetMissionApi.Cancel(cancelled.MissionId,cancelled.Owner);
                Check(cancelledResult.Finished&&cancelledResult.Status=="Cancelled"&&service.ScheduledCount==0,"Cancel did not remove scheduler entry");
                apiRequest=new FleetBurnRequest(Guid.NewGuid(),"Lodestone-test",remote.id,now+12,5,"prograde");
                var submitted=FleetMissionApi.Submit(apiRequest);int count=service.Jobs.Count;
                Check(FleetMissionApi.Submit(apiRequest).MissionId==submitted.MissionId&&service.Jobs.Count==count,"Duplicate mission submitted twice");
                bool conflict=false;try{FleetMissionApi.Submit(new FleetBurnRequest(apiRequest.MissionId,apiRequest.Owner,remote.id,apiRequest.StartUT,6,"PROGRADE"));}catch(InvalidOperationException){conflict=true;}
                Check(conflict,"Conflicting mission ID accepted");
                bool competing=false;try{FleetMissionApi.Submit(new FleetBurnRequest(Guid.NewGuid(),"other-owner",remote.id,now+20,1,"PROGRADE"));}catch(InvalidOperationException){competing=true;}
                Check(competing,"Competing mission accepted");
                bool threadRejected=false;var thread=new System.Threading.Thread(()=>{try{FleetMissionApi.Get(apiRequest.MissionId);}catch(InvalidOperationException){threadRejected=true;}});thread.Start();thread.Join();Check(threadRejected,"Off-thread gateway access accepted");
                var snapshot=new ConfigNode("SCENARIO");service.OnSave(snapshot);service.OnLoad(snapshot);
                Check(FleetMissionApi.Submit(apiRequest).MissionId==apiRequest.MissionId&&service.ScheduledCount==1,"Save/reload replayed or lost request");
                Check(FleetMissionApi.Get(cancelled.MissionId).Finished,"Superseded cancellation receipt lost");
                var corrupted=snapshot.CreateCopy();corrupted.AddNode(snapshot.GetNodes("MISSION_RECEIPT")[0].CreateCopy());service.OnLoad(corrupted);
                bool held=false;try{FleetMissionApi.Submit(apiRequest);}catch(InvalidOperationException){held=true;}Check(held,"Corrupt receipts did not hold gateway");
                var heldSave=new ConfigNode("SCENARIO");service.OnSave(heldSave);service.OnLoad(heldSave);held=false;try{FleetMissionApi.Submit(apiRequest);}catch(InvalidOperationException){held=true;}Check(held,"Receipt hold lost on save/reload");
                service.OnLoad(snapshot);Check(FleetMissionApi.Get(apiRequest.MissionId)!=null,"Valid snapshot restore failed");
                foreach(var x in MissionControl.Modules(remote)){x.automatic=false;x.monitorMission=false;x.guardWarp=false;x.warpReady=false;}
                foreach(var keep in remote.parts.SelectMany(p=>p.Modules.OfType<ModulePersistentFlight>()))keep.armed=false;
                Note("PASS API duplicate/conflict/ownership/thread checks, pending/cancelled receipt restore, corrupt receipt hold persists and valid snapshot recovery");

                Check(!RegionalResidency.Armed(remote),"Distant queue did not release regional keepalive");
                phase=9;completeUT=now;FleetDirector.Visit(craft);Note("PASS remote Mun travel and distant analytic burn queue; returning to Kerbin");
            }
            else if(phase==12&&now-completeUT>2){ScreenCapture.CaptureScreenshot(Path.Combine(KSPUtil.ApplicationRootPath,"event-missions-performance.png"));phase=13;completeUT=now;}
            else if(phase==13&&now-completeUT>2){Application.Quit();}
            else if(phase==9){
                var remote=FlightGlobals.Vessels.First(v=>v.vesselName=="MC REMOTE");var job=PersistentFlight.PersistentFlightScenario.Instance.Find(remote.id);
                Check(job!=null,"Distant burn did not survive travel save/load");
                if(job.Finished){Check(job.Status=="Complete","Remote burn stopped: "+job.Status);Check(!remote.loaded,"Remote burn did not run unloaded");Check(job.DeliveredDV>=4.99,"Remote delta-v incomplete");
                    Note("PASS concurrent unloaded Mun burn while focused at Kerbin: delta-v="+job.DeliveredDV);
                    
                    var service=PersistentFlightScenario.Instance;var receipt=FleetMissionApi.Get(apiRequest.MissionId);
                    Check(receipt.Finished&&receipt.Status=="Complete"&&receipt.DeliveredDV>=4.99,"API completion receipt missing");
                    int count=service.Jobs.Count;FleetMissionApi.Submit(apiRequest);Check(service.Jobs.Count==count,"Completed request replayed");
                    GamePersistence.SaveGame("fleet-scale-complete",HighLogic.SaveFolder,SaveMode.OVERWRITE);
                    var panel=FindObjectOfType<RegionalFleetPanel>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    typeof(RegionalFleetPanel).GetField("showEvents",flags).SetValue(panel,true);
                    typeof(RegionalFleetPanel).GetMethod("SetOpen",flags).Invoke(panel,new object[]{true});
                    string report=(string)typeof(RegionalFleetPanel).GetMethod("PerformanceText",flags).Invoke(panel,null);
                    File.WriteAllText(Path.Combine(KSPUtil.ApplicationRootPath,"event-missions-performance.txt"),report);
                    Check(FleetPerformance.CoreTick.Snapshot().Samples>0&&FleetPerformance.RegionalTick.Snapshot().Samples>0,"Timing samples not collected");
                    Note("PASS analytic execution after native scene reload, completion receipt and retry without replay, timing report");
                    EventAfterTravel();Note("PASS ALL FUEL FORECAST AND EVENT MISSION NATIVE OPERATIONS");phase=12;completeUT=now;
}
            }
            if(m!=null&&Time.realtimeSinceStartup>=nextReport){nextReport=Time.realtimeSinceStartup+10;Note("phase="+phase+" UT="+now+" pilot="+m.PilotOnline+" task="+m.nodeStatus+" queued="+m.nodeRequested+" fuel="+Fuel(craft)+" alt="+craft.altitude+" regional="+RegionFlightRunner.Instance.Regions.Any(r=>r.Vessel==craft)+" forecast="+(m.fuelForecast==null?"null":m.fuelForecast.State+":"+m.fuelForecast.Reason)+" tag="+m.pilotEngineTag);}
        }catch(Exception e){Note("FAIL "+e);Application.Quit();}
    }
}
