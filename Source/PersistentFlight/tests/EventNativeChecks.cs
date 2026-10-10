    FleetEventRuleRequest eventSignal,eventFollow,eventTimeout;bool eventSetup,eventHandshake,eventTimeoutChecked;
    FleetEventRuleRequest pilotEvent;bool pilotEventQueued;
    double eventTimeoutStart;
    void EventBefore(Vessel craft,Vessel focus){
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
