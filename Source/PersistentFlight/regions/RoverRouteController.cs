using System;
using System.Linq;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy : IRoverRouteOwner {
        public bool ActiveRoverRoute {get{var r=RoverRouteController.Get(this);return roverRouteHeld||r!=null&&r.Active;}}
        [KSPField(isPersistant=true)] public string roverRoute64="",roverPreviousBoot="";
        [KSPField(isPersistant=true)] public bool roverRouteHeld,roverRecoveryBoot;
        internal RoverRoute routeCache;internal string routeEncoded;
        internal RoverDecision routeDecision;internal float routeNextTick,routeHeartbeat;
    }
    public static class RoverRouteController {
        public static void Reset(ModuleRegionalAutonomy m){m.routeCache=null;m.routeEncoded=null;m.routeDecision=null;m.routeNextTick=0;m.routeHeartbeat=0;}
        public static RoverRoute Get(ModuleRegionalAutonomy m){
            if(m.roverRouteHeld||String.IsNullOrEmpty(m.roverRoute64))return null;
            if(m.routeCache!=null&&m.routeEncoded==m.roverRoute64)return m.routeCache;
            try{var r=RoverRoute.Load(m.roverRoute64);r.ResetObservation();m.routeCache=r;m.routeEncoded=m.roverRoute64;return r;}
            catch(Exception e){m.roverRouteHeld=true;Debug.LogError("[PF-ROVER-ROUTE] Checkpoint held: "+e.Message);return null;}
        }
        public static void Store(ModuleRegionalAutonomy m,RoverRoute r){m.routeCache=r;m.roverRoute64=r.Save();m.routeEncoded=m.roverRoute64;}
        public static void Configure(ModuleRegionalAutonomy m,RoverRouteRequest request){
            var v=m.vessel;if(v==null||v!=FlightGlobals.ActiveVessel||!v.loaded||v.packed||TimeWarp.CurrentRate!=1)throw new InvalidOperationException("Focus the rover at normal time to save its route");
            if(m.roverRouteHeld)throw new InvalidOperationException("Restore a valid rover checkpoint or explicitly clear the damaged route");
            if(request.Body!=v.mainBody.bodyName||v.mainBody.pqsController==null||v.Splashed)throw new InvalidOperationException("Route needs the current terrain body and a surface rover");
            if(!v.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>()).Any(w=>w.wheelType==WheelType.MOTORIZED))throw new InvalidOperationException("This craft has no stock motorized wheels");
            if(MissionControl.Modules(v).Any(x=>x.QueueCount>0||x.PilotOnline||x!=m&&x.ActiveRoverRoute))throw new InvalidOperationException("A pilot, maneuver or another rover route already owns this craft");
            var service=PersistentFlightScenario.Instance;var burn=service==null?null:service.Find(v.id);if(burn!=null&&!burn.Finished)throw new InvalidOperationException("Cancel the analytic burn before assigning this rover route");
            if(!String.IsNullOrEmpty(m.Checkpoint))throw new InvalidOperationException("Clear the existing guidance checkpoint before assigning this idle CPU a new rover route");
            var prior=Get(m);if(prior!=null&&prior.Active)throw new InvalidOperationException("Cancel the existing route before replacing it");
            if(new RoverPoint(v.latitude,v.longitude).Distance(request.Home,v.mainBody.Radius)>20)throw new InvalidOperationException("Home must be within 20 metres of the rover when the route is saved");
            var cpu=m.part.Modules.OfType<kOS.Module.kOSProcessor>().FirstOrDefault();if(cpu==null)throw new InvalidOperationException("This module has no kOS CPU");
            if(!m.roverRecoveryBoot)m.roverPreviousBoot=cpu.bootFile;
            cpu.bootFile="boot/pf_rover_route_resume.ks";m.roverRecoveryBoot=true;
            Store(m,new RoverRoute(request));m.routeDecision=null;m.roverHold=true;m.Arm(true);
            FleetEventDirector.Emit(m,"ROVER_ROUTE_SAVED",request.Waypoints.Length+" waypoints; home "+request.Home.Latitude+", "+request.Home.Longitude);
        }
        public static void Resume(ModuleRegionalAutonomy m){var r=Get(m);if(r==null)throw new InvalidOperationException("No valid route");if(m.missionHold)throw new InvalidOperationException("Review and release the mission safety hold before resuming this rover route");r.Resume();Store(m,r);m.roverHold=false;FleetEventDirector.Emit(m,"ROVER_ROUTE_RESUMED","Route resumed by player");}
        public static void ReturnHome(ModuleRegionalAutonomy m){var r=Get(m);if(r==null)throw new InvalidOperationException("No valid route");if(r.Hold!=RoverHold.None||m.missionHold)throw new InvalidOperationException("Review the safety hold before requesting return; return does not clear a hazard");r.ReturnHome();Store(m,r);m.routeDecision=null;m.roverHold=false;FleetEventDirector.Emit(m,"ROVER_RETURNING","Return requested by player");}
        public static void Cancel(ModuleRegionalAutonomy m){var r=Get(m);if(r!=null){r.Cancel();Store(m,r);}m.roverHold=true;m.routeDecision=null;Brake(m);FleetEventDirector.Emit(m,"ROVER_ROUTE_CANCELLED","Route cancelled by player");}
        public static void Clear(ModuleRegionalAutonomy m){var r=Get(m);if(r!=null&&r.Active)throw new InvalidOperationException("Cancel the route before clearing it");m.roverRoute64="";m.roverRouteHeld=false;Reset(m);RestoreBoot(m);}
        public static void RestoreBoot(ModuleRegionalAutonomy m){var cpu=m.part.Modules.OfType<kOS.Module.kOSProcessor>().FirstOrDefault();if(cpu!=null&&cpu.bootFile=="boot/pf_rover_route_resume.ks")cpu.bootFile=m.roverPreviousBoot;m.roverRecoveryBoot=false;}
        static void Brake(ModuleRegionalAutonomy m){if(m.vessel!=null&&m.vessel.loaded){m.vessel.ctrlState.wheelThrottle=0;m.vessel.ctrlState.wheelSteer=.0001f;m.vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes,true);}}
        public static string Status(ModuleRegionalAutonomy m){if(m.roverRouteHeld)return "CHECKPOINT_HOLD";var r=Get(m);if(r==null)return m.roverRouteHeld?"CHECKPOINT_HOLD":"NO_ROUTE";return m.routeDecision==null?"SAVED / run rover program":m.routeDecision.Status;}
        public static double DesiredSpeed(ModuleRegionalAutonomy m){return m.routeDecision==null||m.roverHold||m.missionHold||!m.automatic||TimeWarp.CurrentRate!=1?0:m.routeDecision.Speed;}
        public static bool Tick(ModuleRegionalAutonomy m){
            m.routeHeartbeat=Time.realtimeSinceStartup;m.ScriptPulse();
            var r=Get(m);if(r==null){Brake(m);return false;}
            if(Time.realtimeSinceStartup<m.routeNextTick)return true;m.routeNextTick=Time.realtimeSinceStartup+.2f;
            var v=m.vessel;if(v==null||!v.loaded||v.packed||v.mainBody==null)return false;
            var position=new RoverPoint(v.latitude,v.longitude);double capacity,power=MissionControl.Resource(v,"ElectricCharge",out capacity);
            bool paused=m.roverHold||m.missionHold||!m.automatic||TimeWarp.CurrentRate!=1;
            var o=new RoverObservation{Body=v.mainBody.bodyName,Position=position,Radius=v.mainBody.Radius,UT=Planetarium.GetUniversalTime(),Speed=v.horizontalSrfSpeed,Power=power,Capacity=capacity,Paused=paused};
            if(!paused&&r.Active){
                var own=RegionFlightRunner.Instance==null?null:RegionFlightRunner.Instance.Regions.FirstOrDefault(x=>x.Vessel==v);
                o.Supported=(own==null?RegionalWheels.NativeContacts(v):own.GroundedWheels)>=2;
                if(v.mainBody.bodyName!=r.Request.Body||v.mainBody.pqsController==null)o.TerrainValid=false;
                else try{SampleTerrain(v,position,r.Target,o);}catch(Exception e){o.TerrainValid=false;Debug.LogWarning("[PF-ROVER-ROUTE] Terrain sample: "+e.Message);}
            }
            string before=Status(m);m.routeDecision=r.Tick(o);Store(m,r);
            if(m.routeDecision.Speed==0)Brake(m);
            foreach(string name in m.routeDecision.Events)FleetEventDirector.Emit(m,name,"Waypoint "+r.Index+" / "+r.Request.Waypoints.Length+"; "+m.routeDecision.Status);
            if(before!=m.routeDecision.Status){m.missionPhase="Rover: "+m.routeDecision.Status;FleetEventDirector.Emit(m,"ROVER_STATUS",m.missionPhase);}
            return true;
        }
        static void SampleTerrain(Vessel v,RoverPoint current,RoverPoint target,RoverObservation o){
            double bearing=current.Bearing(target),look=Math.Min(25,Math.Max(8,o.Speed*o.Speed/.6+4));
            double prior=v.mainBody.TerrainAltitude(current.Latitude,current.Longitude,false);if(!FleetEventLedger.Finite(prior))throw new InvalidOperationException("Missing current terrain height");
            double priorD=0,max=0;
            foreach(double d in new[]{2.0,look/2,look}){
                var p=current.Offset(bearing,d,v.mainBody.Radius);double centre=v.mainBody.TerrainAltitude(p.Latitude,p.Longitude,false);
                var left=p.Offset(bearing-90,1.5,v.mainBody.Radius);var right=p.Offset(bearing+90,1.5,v.mainBody.Radius);
                double a=v.mainBody.TerrainAltitude(left.Latitude,left.Longitude,false),b=v.mainBody.TerrainAltitude(right.Latitude,right.Longitude,false);
                if(!FleetEventLedger.Finite(centre)||!FleetEventLedger.Finite(a)||!FleetEventLedger.Finite(b))throw new InvalidOperationException("Missing look-ahead terrain height");
                max=Math.Max(max,Math.Atan(Math.Abs(centre-prior)/(d-priorD))*180/Math.PI);max=Math.Max(max,Math.Atan(Math.Max(Math.Abs(a-centre),Math.Abs(b-centre))/1.5)*180/Math.PI);
                if(v.mainBody.ocean&&(centre<=0||a<=0||b<=0||prior<=0))o.Water=true;
                prior=centre;priorD=d;
            }
            o.Slope=max;
        }
    }
}
