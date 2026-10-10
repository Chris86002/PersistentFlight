using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed class SpectatorCard {
        public Guid Id;public string Name,Body,Physics,Phase,Mission,Alert;
        public double Altitude,Speed,Fuel,FuelCapacity,Power,PowerCapacity,Remaining=Double.NaN;
        public bool Focused,Loaded;public SpectatorSituation Situation;
    }
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FleetSpectator:MonoBehaviour {
        public static FleetSpectator Instance;
        public bool Touring {get;private set;}
        public bool FollowFocus=true,Visible;
        public Guid Watched {get;private set;}
        public string Status="Spectator ready";
        public double Interval=15;
        public SpectatorCard[] Cards=new SpectatorCard[0];
        Guid expectedFocus;float nextCapture,nextSwitch;bool leaving;
        public void Awake(){Instance=this;GameEvents.onGameSceneLoadRequested.Add(Leaving);}
        public void OnDestroy(){GameEvents.onGameSceneLoadRequested.Remove(Leaving);if(Instance==this)Instance=null;}
        void Leaving(GameScenes scene){leaving=true;StopTour("Tour stopped for scene change");}
        public static bool InFleet(Vessel v){var s=PersistentFlightScenario.Instance;return v!=null&&(RegionalResidency.Armed(v)||(s!=null&&s.Find(v.id)!=null));}
        static string Saved(ProtoPartModuleSnapshot[] modules,string key,string fallback){return modules.Select(m=>m.moduleValues.GetValue(key)).FirstOrDefault(s=>!String.IsNullOrEmpty(s))??fallback;}
        public static SpectatorCard Capture(Vessel v){
            var sample=FleetSample.Capture(v);var m=MissionControl.Modules(v);var s=PersistentFlightScenario.Instance;var job=s==null?null:s.Find(v.id);
            bool owns=job!=null&&!job.Finished;double next=Double.PositiveInfinity;
            foreach(var cpu in m){
                if(cpu.nodeRequested){var forecast=FleetFuelForecast.Evaluate(cpu,cpu.nodeInitialDV);next=Math.Min(next,forecast.Known?cpu.nodeUT-forecast.Duration:Double.NaN);}
                if(cpu.QueueCount>0&&!cpu.nodeRequested)try{var q=MissionQueue.Read(cpu.queuedNodes64);if(q.Count>0)next=Math.Min(next,q[0].UT);}catch{next=Double.NaN;}
            }
            bool held=m.Any(cpu=>cpu.missionHold);string phase=sample.Phase,mission=sample.NodeState,alert=String.Join(" / ",m.Select(cpu=>cpu.alert).Where(a=>!String.IsNullOrEmpty(a)).Distinct().ToArray());
            if(!v.loaded&&v.protoVessel!=null){
                var saved=v.protoVessel.protoPartSnapshots.SelectMany(p=>p.modules).Where(p=>p.moduleName=="ModuleRegionalAutonomy").ToArray();
                phase="Saved: "+Saved(saved,"missionPhase","Phase unavailable");
                held=saved.Any(p=>p.moduleValues.GetValue("missionHold")=="True");
                if(!owns)mission="Saved: "+Saved(saved,"nodeStatus","No maneuver queued");
                alert=held?"Saved mission hold":"";
            }
            return new SpectatorCard{Id=v.id,Name=sample.Name,Body=sample.Body,Physics=sample.Physics,Phase=phase,Mission=held?"Held / "+mission:mission,Alert=alert,Altitude=sample.Altitude,Speed=sample.Speed,Fuel=sample.Fuel,FuelCapacity=sample.FuelCapacity,Power=sample.Power,PowerCapacity=sample.PowerCapacity,Remaining=owns?Math.Max(0,job.TargetDV-job.DeliveredDV):m.Any(cpu=>cpu.nodeRequested)?sample.RemainingDV:Double.NaN,Focused=sample.Focused,Loaded=v.loaded,
                Situation=new SpectatorSituation{Id=v.id,Armed=RegionalResidency.Armed(v),Loaded=v.loaded,Unpacked=!v.packed,SameBody=FlightGlobals.ActiveVessel!=null&&v.mainBody==FlightGlobals.ActiveVessel.mainBody,Space=v.vesselType!=VesselType.EVA&&!v.LandedOrSplashed&&v.altitude>v.mainBody.atmosphereDepth+1000&&v.orbit.PeA>v.mainBody.atmosphereDepth+1000,Held=held,BackgroundJob=owns,Throttle=v.loaded?v.ctrlState.mainThrottle:0,Thrust=v.loaded?v.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Sum(e=>(double)e.finalThrust):0,NextManeuver=next}};
        }
        public void Refresh(){
            if(FlightGlobals.Vessels==null)return;
            var cards=new List<SpectatorCard>();foreach(var v in FlightGlobals.Vessels.Where(InFleet))try{cards.Add(Capture(v));}catch(Exception e){Status="Spectator sampling: "+e.Message;}
            Cards=cards.OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.Id).ToArray();
        }
        public void Watch(Guid id){if(!Cards.Any(c=>c.Id==id))throw new InvalidOperationException("Craft is no longer in the fleet");FollowFocus=false;Watched=id;}
        public void PinCurrent(){var v=FlightGlobals.ActiveVessel;if(v==null||!InFleet(v))throw new InvalidOperationException("Focus a fleet craft first");StopTour("Pinned current craft");FollowFocus=false;Watched=v.id;}
        public void StopTour(string reason="Tour paused"){Touring=false;Status=reason;}
        string LeavingReason(){
            if(leaving||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||FlightGlobals.ActiveVessel==null)return "Waiting for flight to be ready";
            if(TimeWarp.CurrentRate!=1)return "Waiting for normal time";
            return SpectatorPolicy.Block(Capture(FlightGlobals.ActiveVessel).Situation,Planetarium.GetUniversalTime());
        }
        public void StartTour(){
            string reason=LeavingReason();if(reason!="")throw new InvalidOperationException(reason);
            if(!BurnMath.Finite(Interval)||Interval<5||Interval>120)throw new InvalidOperationException("Use a tour interval from 5 to 120 seconds");
            Refresh();Touring=true;FollowFocus=true;expectedFocus=FlightGlobals.ActiveVessel.id;nextSwitch=Time.realtimeSinceStartup+(float)Interval;Status="Tour started; watching nearby coasting craft";
        }
        public bool NextLocal(int direction){
            string reason=LeavingReason();if(reason!=""){Status=reason;return false;}
            Refresh();Guid next=SpectatorPolicy.Next(Cards.Select(c=>c.Situation).ToArray(),FlightGlobals.ActiveVessel.id,direction,Planetarium.GetUniversalTime());
            if(next==Guid.Empty){Status="No other nearby coasting craft is ready";return false;}
            var target=FlightGlobals.Vessels.FirstOrDefault(v=>v!=null&&v.id==next);
            // Recheck the live candidate at the moment of focus, rather than trusting a stale card.
            if(target==null||SpectatorPolicy.Block(Capture(target).Situation,Planetarium.GetUniversalTime())!=""||RegionFlightRunner.Instance==null||!RegionFlightRunner.Instance.Focus(target)){Status="Craft could not be focused";return false;}
            expectedFocus=next;Watched=next;FollowFocus=true;nextSwitch=Time.realtimeSinceStartup+(float)Interval;Status="Watching "+target.vesselName;return true;
        }
        public void VisitWatched(){
            string reason=LeavingReason();if(reason!="")throw new InvalidOperationException(reason);
            var target=FlightGlobals.Vessels.FirstOrDefault(v=>v!=null&&v.id==Watched);if(target==null||!InFleet(target))throw new InvalidOperationException("Watched craft is unavailable");
            var s=PersistentFlightScenario.Instance;var job=s==null?null:s.Find(target.id);if(job!=null&&!job.Finished)throw new InvalidOperationException("Wait for this craft's background burn to finish before visiting");
            StopTour("Tour paused for a manual visit");FleetDirector.Visit(target);
        }
        public void Tick(){
            if(leaving||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
            if(FollowFocus&&FlightGlobals.ActiveVessel!=null)Watched=FlightGlobals.ActiveVessel.id;
            if((Touring||Visible)&&Time.realtimeSinceStartup>=nextCapture){Refresh();nextCapture=Time.realtimeSinceStartup+.5f;}
            if(!Touring)return;
            if(FlightGlobals.ActiveVessel==null||FlightGlobals.ActiveVessel.id!=expectedFocus){StopTour("Tour paused: focus changed outside the spectator");return;}
            if(Time.realtimeSinceStartup<nextSwitch)return;
            string reason=LeavingReason();if(reason!=""){Status="Tour waiting: "+reason;nextSwitch=Time.realtimeSinceStartup+1;return;}
            if(!NextLocal(1))nextSwitch=Time.realtimeSinceStartup+2;
        }
        public void Update(){try{Tick();}catch(Exception e){StopTour("Spectator paused: "+e.Message);Debug.LogError("[PF-SPECTATOR] "+e);}}
    }
}
