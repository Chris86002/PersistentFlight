using System;
using System.Linq;
using kOS;
using kOS.AddOns;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Exceptions;
using PersistentFlight;

[assembly: KSPAssembly("PersistentFlight.Regions",0,25)]
[assembly: KSPAssemblyDependency("kOS",1,6)]
[assembly: KSPAssemblyDependency("PersistentFlight",0,1)]

namespace PersistentFlight.Regions
{
    [kOSAddon("PFR")]
    [kOS.Safe.Utilities.KOSNomenclature("PersistentFlightRegionAddon")]
    public sealed class RegionKosAddon : kOS.Suffixed.Addon
    {
        public RegionKosAddon(SharedObjects shared) : base(shared)
        {
            AddSuffix("ENTER",new NoArgsSuffix<BooleanValue>(Enter));
            AddSuffix("AUTOENABLE",new NoArgsSuffix<BooleanValue>(()=>Automatic(true)));
            AddSuffix("AUTODISABLE",new NoArgsSuffix<BooleanValue>(()=>Automatic(false)));
            AddSuffix("AUTOMATIC",new Suffix<BooleanValue>(()=>AutoModule().automatic));
            AddSuffix("ENTERDESCENT",new NoArgsSuffix<BooleanValue>(EnterDescent));
            AddSuffix("HEIGHT",new Suffix<ScalarValue>(()=>Own()==null ? shared.Vessel.radarAltitude : Own().Height));
            AddSuffix("WHEELCONTACTS",new Suffix<ScalarValue>(()=>Own()==null ? RegionalWheels.NativeContacts(shared.Vessel) : Own().GroundedWheels));
            AddSuffix("WHEELSTEPS",new Suffix<ScalarValue>(()=>Own()==null ? 0 : Own().WheelSteps));
            AddSuffix("TERRAINREBUILDS",new Suffix<ScalarValue>(()=>Own()==null ? 0 : Own().TerrainRebuilds));
            AddSuffix("CONTACTS",new Suffix<ScalarValue>(()=>Own()==null ? 0 : Own().GroundContacts));
            AddSuffix("EXIT",new NoArgsSuffix<BooleanValue>(Exit));
            AddSuffix("ACTIVE",new Suffix<BooleanValue>(()=>Own()!=null));
            AddSuffix("FOCUSED",new Suffix<BooleanValue>(()=>shared.Vessel==FlightGlobals.ActiveVessel));
            AddSuffix("FOCUS",new NoArgsSuffix<BooleanValue>(()=>RegionFlightRunner.Instance!=null && RegionFlightRunner.Instance.Focus(shared.Vessel)));
            AddSuffix("CANENTER",new Suffix<BooleanValue>(()=>CanEnter()));
            AddSuffix("STEPS",new Suffix<ScalarValue>(()=>Own()==null ? 0 : Own().Steps));
            AddSuffix("STATUS",new Suffix<StringValue>(()=>Own()==null ? "Stock physics" : (Own().AllowsDescent ? "Private descent physics" : "Private orbital physics")));
            AddSuffix("CHECKPOINT",new SetSuffix<StringValue>(()=>AutoModule().Checkpoint,v=>AutoModule().Checkpoint=v.ToString()));
            AddSuffix("CHECKPOINTUT",new Suffix<ScalarValue>(()=>AutoModule().checkpointUT));
            AddSuffix("ROVERHOLD",new SetSuffix<BooleanValue>(()=>AutoModule().roverHold,v=>AutoModule().roverHold=v));
            AddSuffix("PHASE",new SetSuffix<StringValue>(()=>AutoModule().missionPhase,v=>{AutoModule().missionPhase=MissionControl.Text(v.ToString());AutoModule().ScriptPulse();}));
            AddSuffix("RECORDING",new SetSuffix<BooleanValue>(()=>AutoModule().recordFlight,v=>AutoModule().recordFlight=v));
            AddSuffix("PILOTREADY",new SetSuffix<BooleanValue>(()=>AutoModule().PilotOnline,v=>{AutoModule().Heartbeat(v);AutoModule().ScriptPulse();}));
            AddSuffix("QUEUENODE",new NoArgsSuffix<BooleanValue>(()=>{try{MissionControl.QueueNext(shared.Vessel,AutoModule());return true;}catch(Exception e){throw new KOSException(e.Message);}}));
            AddSuffix("CANCELNODE",new NoArgsVoidSuffix(()=>AutoModule().CancelNode("Cancelled by script")));
            AddSuffix("NODEQUEUED",new Suffix<BooleanValue>(()=>AutoModule().nodeRequested&&!AutoModule().missionHold));
            AddSuffix("NODEUT",new Suffix<ScalarValue>(()=>AutoModule().nodeUT));
            AddSuffix("NODEDV",new Suffix<ScalarValue>(()=>AutoModule().nodeInitialDV));
            AddSuffix("NODEVECTOR",new Suffix<kOS.Suffixed.Vector>(()=>new kOS.Suffixed.Vector(MissionControl.Remaining(AutoModule()))));
            AddSuffix("NODESTATE",new SetSuffix<StringValue>(()=>AutoModule().nodeStatus,v=>AutoModule().nodeStatus=MissionControl.Text(v.ToString())));
            AddSuffix("BEGINBURN",new NoArgsVoidSuffix(()=>FlightDebriefRecorder.Begin(AutoModule())));
            AddSuffix("FINISHNODE",new OneArgsSuffix<BooleanValue,BooleanValue>(v=>{MissionControl.Finish(AutoModule(),v);return true;}));
            AddSuffix("LANDSITE",new Suffix<StringValue>(()=>AutoModule().landingSite));
            AddSuffix("LANDTARGET",new Suffix<kOS.Suffixed.GeoCoordinates>(LandingTarget));
            AddSuffix("LANDVESSEL",new Suffix<kOS.Suffixed.VesselTarget>(LandingVessel));
            AddSuffix("HAVELANDVESSEL",new Suffix<BooleanValue>(()=>!String.IsNullOrEmpty(AutoModule().landingVessel)));
            AddSuffix("SETLANDSITE",new ThreeArgsSuffix<BooleanValue,StringValue,ScalarValue,ScalarValue>((name,lat,lon)=>{MissionControl.SetSite(AutoModule(),name.ToString(),shared.Vessel.mainBody,lat.GetDoubleValue(),lon.GetDoubleValue());return true;}));
            AddSuffix("QUEUEPLAN",new NoArgsSuffix<BooleanValue>(()=>{MissionQueue.QueuePlan(shared.Vessel,AutoModule());return true;}));
            AddSuffix("QUEUESOIPLAN",new NoArgsSuffix<BooleanValue>(()=>{MissionQueue.QueuePlan(shared.Vessel,AutoModule(),true);return true;}));
            AddSuffix("SOIPLAN",new Suffix<BooleanValue>(()=>AutoModule().soiMission));
            AddSuffix("SOISTATE",new Suffix<StringValue>(()=>AutoModule().soiStatus));
            AddSuffix("SOITAKEOVER",new SetSuffix<BooleanValue>(()=>AutoModule().soiTakeover,v=>AutoModule().soiTakeover=v));
            AddSuffix("COASTUNTIL",new Suffix<ScalarValue>(()=>SoiFlightPlan.CoastUntil(AutoModule())));
            AddSuffix("QUEUECOUNT",new Suffix<ScalarValue>(()=>AutoModule().QueueCount));
            AddSuffix("MISSIONHOLD",new SetSuffix<BooleanValue>(()=>AutoModule().missionHold,v=>{if(v)AutoModule().HoldMission("Paused by script");else MissionIntegrity.Release(AutoModule());}));
            AddSuffix("MONITORED",new SetSuffix<BooleanValue>(()=>AutoModule().monitorMission,v=>{AutoModule().monitorMission=v;AutoModule().ScriptPulse();}));
            AddSuffix("HEARTBEAT",new NoArgsVoidSuffix(()=>AutoModule().ScriptPulse()));
            AddSuffix("ALERT",new Suffix<StringValue>(()=>AutoModule().alert));
            AddSuffix("WARPREADY",new SetSuffix<BooleanValue>(()=>AutoModule().warpReady,v=>AutoModule().warpReady=v));
            AddSuffix("WARPUNTIL",new SetSuffix<ScalarValue>(()=>AutoModule().warpUntil,v=>AutoModule().warpUntil=v.GetDoubleValue()));
            AddSuffix("PILOTENGINETAG",new SetSuffix<StringValue>(()=>AutoModule().pilotEngineTag,v=>AutoModule().pilotEngineTag=MissionControl.Text(v.ToString())));
            AddSuffix("RECOVERYPILOT",new Suffix<BooleanValue>(()=>AutoModule().resumePilot));
            AddSuffix("LANDINGTAKEOVER",new SetSuffix<BooleanValue>(()=>AutoModule().landingTakeover,v=>AutoModule().landingTakeover=v));
            AddSuffix("CPUID",new Suffix<ScalarValue>(()=>shared.KSPPart.flightID));
            AddSuffix("EVENT",new OneArgsSuffix<BooleanValue,StringValue>(v=>{FleetEventApi.Publish(shared.KSPPart.flightID,shared.Vessel.id,v.ToString(),AutoModule().missionPhase);return true;}));
            AddSuffix("ACTION",new Suffix<StringValue>(()=>{var r=FleetEventDirector.Pending(AutoModule());return r==null?"":r.Request.Task;}));
            AddSuffix("ACTIONID",new Suffix<StringValue>(()=>{var r=FleetEventDirector.Pending(AutoModule());return r==null?"":r.Request.Id.ToString();}));
            AddSuffix("RUNPILOTACTION",new NoArgsSuffix<BooleanValue>(()=>FleetEventDirector.RunPilotAction(AutoModule())));
            AddSuffix("FINISHACTION",new TwoArgsSuffix<BooleanValue,StringValue,BooleanValue>((id,success)=>{FleetEventApi.Complete(Guid.Parse(id.ToString()),shared.KSPPart.flightID,success,"Reported by kOS handler");return true;}));
            AddSuffix("ROUTEACTIVE",new Suffix<BooleanValue>(()=>{var r=RoverRouteController.Get(AutoModule());return r!=null&&r.Active;}));
            AddSuffix("ROVERRUN",new NoArgsSuffix<BooleanValue>(()=>RoverRouteController.Tick(AutoModule())));
            AddSuffix("ROUTESPEED",new Suffix<ScalarValue>(()=>RoverRouteController.DesiredSpeed(AutoModule())));
            AddSuffix("ROUTESTATE",new Suffix<StringValue>(()=>RoverRouteController.Status(AutoModule())));
            AddSuffix("ROUTETARGET",new Suffix<kOS.Suffixed.GeoCoordinates>(()=>{var r=RoverRouteController.Get(AutoModule());if(r==null)throw new KOSException("No valid rover route");return new kOS.Suffixed.GeoCoordinates(shared,r.Target.Latitude,r.Target.Longitude);}));
            AddSuffix("ROUTEID",new Suffix<StringValue>(()=>{var r=RoverRouteController.Get(AutoModule());return r==null?"":r.Request.Id.ToString();}));
            AddSuffix("ROUTEINDEX",new Suffix<ScalarValue>(()=>{var r=RoverRouteController.Get(AutoModule());return r==null?0:r.Index;}));
            AddSuffix("ROUTESLOPE",new Suffix<ScalarValue>(()=>AutoModule().routeDecision==null?0:AutoModule().routeDecision.Slope));
            AddSuffix("CHECKFUEL",new OneArgsSuffix<BooleanValue,ScalarValue>(dv=>FleetFuelForecast.Check(AutoModule(),dv.GetDoubleValue(),"Script request").WithinReserve));
            AddSuffix("FUELCHECK",new Suffix<StringValue>(()=>AutoModule().fuelForecast==null?"Not checked":AutoModule().fuelForecast.State+": "+AutoModule().fuelForecast.Reason));
            AddSuffix("FUELKNOWN",new Suffix<BooleanValue>(()=>AutoModule().fuelForecast!=null&&AutoModule().fuelForecast.Known));
            AddSuffix("FUELLEFTDV",new Suffix<ScalarValue>(()=>{var r=AutoModule().fuelForecast;if(r==null||!r.Known)throw new KOSException("Check fuel first; a known engine model is required");return r.RemainingDV;}));
            AddSuffix("FUELRESERVEDV",new SetSuffix<ScalarValue>(()=>AutoModule().landingReserveDV,v=>{double n=v.GetDoubleValue();if(!BurnMath.Finite(n)||n<0||n>10000)throw new KOSException("Reserve must be 0 to 10000 m/s");AutoModule().landingReserveDV=n;}));
            AddSuffix("VERSION",new Suffix<StringValue>(()=>"0.25.0-experimental"));
        }
        kOS.Suffixed.GeoCoordinates LandingTarget() {
            var module=AutoModule();if(shared.Vessel.mainBody.bodyName!=module.landingBody)throw new KOSException("Landing site is on another body; choose a site here");
            try{var site=MissionControl.Site(module);return new kOS.Suffixed.GeoCoordinates(shared,site.x,site.y);}catch(Exception e){throw new KOSException(e.Message);}
        }
        kOS.Suffixed.VesselTarget LandingVessel() {
            var module=AutoModule();MissionControl.Site(module);
            Guid id;if(!Guid.TryParse(module.landingVessel,out id))throw new KOSException("No landing vessel selected");
            return kOS.Suffixed.VesselTarget.CreateOrGetExisting(FlightGlobals.Vessels.First(v=>v.id==id),shared);
        }
        ModuleRegionalAutonomy AutoModule()
        {
            var module=shared.KSPPart.Modules.OfType<ModuleRegionalAutonomy>().FirstOrDefault();
            if(module==null) throw new KOSException("Regional autonomy module is missing on this CPU part");
            return module;
        }
        BooleanValue Automatic(bool enabled) { AutoModule().Arm(enabled); return true; }
        public override BooleanValue Available() { return RegionFlightRunner.Instance!=null; }
        bool CanEnter()
        {
            Vessel v=shared.Vessel;
            return RegionFlightRunner.Instance!=null && v!=null && v.loaded && !v.packed && v!=FlightGlobals.ActiveVessel &&
                !v.Landed && !v.Splashed && v.altitude>v.mainBody.atmosphereDepth+1000 && TimeWarp.CurrentRate==1;
        }
        LiveVesselRegion Own() { return RegionFlightRunner.Instance==null ? null : RegionFlightRunner.Instance.Regions.FirstOrDefault(r=>r.Vessel==shared.Vessel); }
        BooleanValue Enter()
        {
            if(Own()!=null) return true;
            try { if(!CanEnter()) throw new InvalidOperationException("Enter from an unfocused unpacked vacuum vessel at normal time rate"); RegionFlightRunner.Instance.Enroll(shared.Vessel); return true; }
            catch(Exception e) { throw new KOSException("PFR:ENTER: "+e.Message); }
        }
        BooleanValue Exit()
        {
            LiveVesselRegion own=Own(); if(own==null) return false;
            RegionFlightRunner.Instance.Release(own); return true;
        }
        BooleanValue EnterDescent()
        {
            if(Own()!=null) { Own().EnableDescent(); return true; }
            try { Vessel v=shared.Vessel; if(RegionFlightRunner.Instance==null || v==null || !v.loaded || v.packed || v==FlightGlobals.ActiveVessel || v.Splashed || TimeWarp.CurrentRate!=1) throw new InvalidOperationException("Requires an unfocused unpacked non-splashed vessel at normal rate"); RegionFlightRunner.Instance.Enroll(v,true); return true; }
            catch(Exception e) { throw new KOSException("PFR:ENTERDESCENT: "+e.Message); }
        }
    }
}


