using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public bool eventObserved,eventOrbit,eventLanded;
        [KSPField(isPersistant=true)] public string eventVessel="",eventBody="",eventPhysics="",eventPhase="";
    }
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FleetEventDirector:MonoBehaviour {
        float next;public static string LastError="";
        public void Update(){
            if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||PersistentFlightScenario.Instance==null||Time.realtimeSinceStartup<next)return;
            next=Time.realtimeSinceStartup+.5f;
            try{
                if(FleetEventApi.IsHeld){LastError="Saved mission events need recovery from a verified save";return;}
                FleetEventApi.Tick();
                var modules=FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded).SelectMany(v=>MissionControl.Modules(v)).ToArray();
                foreach(var m in modules.Where(m=>m.automatic))Observe(m);
                var byCpu=modules.GroupBy(m=>m.part.flightID).ToDictionary(g=>g.Key,g=>g.Single());
                foreach(var r in FleetEventApi.List()){
                    ModuleRegionalAutonomy m;if(!byCpu.TryGetValue(r.Request.TargetCpu,out m)||m.vessel.packed)continue;
                    if(r.HoldPending){m.HoldMission("Event mission failed: "+r.Detail);FleetEventApi.HoldApplied(r.Request.Id);}
                    if(r.State!=FleetRuleState.Ready&&r.State!=FleetRuleState.Running)continue;
                    if(r.Request.Action==FleetAction.Signal&&(!m.automatic||m.missionHold))continue;
                    if(r.State==FleetRuleState.Ready&&!FleetEventApi.Begin(r.Request.Id))continue;
                    if(r.Request.Action==FleetAction.Hold){m.HoldMission("Event action: "+r.Request.Task);FleetEventApi.Complete(r.Request.Id,m.part.flightID,true,"Mission held");}
                    else if(r.Request.Action==FleetAction.Notify){ScreenMessages.PostScreenMessage(m.vessel.vesselName+": "+r.Request.Task,7,ScreenMessageStyle.UPPER_CENTER);FleetEventApi.Complete(r.Request.Id,m.part.flightID,true,"Notification displayed");}
                }
                LastError="";
            }catch(Exception e){LastError=e.Message;Debug.LogError("[PF-EVENTS] "+e);}
        }
        public static void Emit(ModuleRegionalAutonomy m,string name,string detail){
            if(PersistentFlightScenario.Instance==null)return;
            try{if(!FleetEventApi.IsHeld)FleetEventApi.Publish(m.part.flightID,m.vessel==null?Guid.Empty:m.vessel.id,name,detail);}catch(Exception e){LastError=e.Message;Debug.LogError("[PF-EVENTS] Event capture: "+e);}
        }
        public static void Observe(ModuleRegionalAutonomy m){
            var v=m.vessel;
            bool orbit=!v.LandedOrSplashed&&v.orbit!=null&&v.orbit.eccentricity<1&&v.orbit.PeA>v.mainBody.atmosphereDepth+1000;
            string physics=!v.loaded?"UNLOADED":v.packed?"PACKED":v==FlightGlobals.ActiveVessel?"FOCUSED":RegionFlightRunner.Instance!=null&&RegionFlightRunner.Instance.Regions.Any(r=>r.Vessel==v)?"REGIONAL":"STOCK";
            if(m.eventObserved){
                if(m.eventVessel!=v.id.ToString())Emit(m,"VESSEL_CHANGED","CPU moved from "+m.eventVessel+" to "+v.id+" (separation, docking or vessel reconstruction)");
                if(m.eventBody!=v.mainBody.bodyName)Emit(m,"BODY_CHANGED",m.eventBody+" -> "+v.mainBody.bodyName);
                if(!m.eventOrbit&&orbit)Emit(m,"ORBIT_ESTABLISHED","Periapsis "+v.orbit.PeA.ToString("F0")+" m");
                if(!m.eventLanded&&v.LandedOrSplashed)Emit(m,"LANDED",v.Splashed?"Splashed down":"Surface contact");
                if(m.eventPhysics!=physics)Emit(m,"PHYSICS_CHANGED",m.eventPhysics+" -> "+physics);
                if(m.eventPhase!=m.missionPhase)Emit(m,"PHASE_CHANGED",m.missionPhase);
            }else Emit(m,"CRAFT_OBSERVED",v.vesselName+" / "+physics);
            m.eventObserved=true;m.eventVessel=v.id.ToString();m.eventBody=v.mainBody.bodyName;m.eventOrbit=orbit;m.eventLanded=v.LandedOrSplashed;m.eventPhysics=physics;m.eventPhase=m.missionPhase;
        }
        public static FleetRuleResult Pending(ModuleRegionalAutonomy m){
            if(PersistentFlightScenario.Instance==null||FleetEventApi.IsHeld||m.missionHold||!m.automatic)return null;
            FleetEventApi.Tick();return FleetEventApi.Pending(m.part.flightID);
        }
        public static bool RunPilotAction(ModuleRegionalAutonomy m){
            var r=Pending(m);if(r==null)return false;
            if(r.Request.Task!="QUEUE_NEXT_MANEUVER"&&r.Request.Task!="QUEUE_FLIGHT_PLAN"&&r.Request.Task!="QUEUE_SOI_PLAN")return false;
            try{
                if(r.Request.Task=="QUEUE_NEXT_MANEUVER")MissionControl.QueueNext(m.vessel,m);else MissionQueue.QueuePlan(m.vessel,m,r.Request.Task=="QUEUE_SOI_PLAN");
                FleetEventApi.Complete(r.Request.Id,m.part.flightID,true,"Maneuver request captured by mission pilot");return true;
            }catch(Exception e){FleetEventApi.Complete(r.Request.Id,m.part.flightID,false,MissionControl.Text(e.Message));return false;}
        }
    }
}
