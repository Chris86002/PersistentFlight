using System;
using System.Linq;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy : IManeuverPlanOwner {
        public bool ActiveManeuverPlan {get{return nodeRequested||!String.IsNullOrEmpty(queuedNodes64);}}
        [KSPField(isPersistant=true)] public bool soiMission,soiTakeover=true;
        [KSPField(isPersistant=true)] public int soiDepartureBody=-1;
        [KSPField(isPersistant=true)] public string soiStatus="No SOI plan";
    }
    public static class SoiFlightPlan {
        static float lastVisit=-100;
        public static void Hold(ModuleRegionalAutonomy m,string why){m.soiStatus="Held: "+why;m.HoldMission("SOI plan: "+why);FleetEventDirector.Emit(m,"SOI_PLAN_HELD",why);}
        public static bool PrepareNext(ModuleRegionalAutonomy m,SavedManeuver next){
            var target=MissionControl.ReadOrbit(next.Target);var v=m.vessel;int expected=target.referenceBody.flightGlobalsIndex,current=v.mainBody.flightGlobalsIndex;
            if(current!=expected){
                string reason;var decision=SoiPlanPolicy.Check(m.soiDepartureBody,expected,current,Planetarium.GetUniversalTime(),next.UT,next.DV,0,0,0,out reason);
                if(decision==SoiPlanDecision.Hold)Hold(m,reason);
                else {string status="Waiting for "+target.referenceBody.bodyName;if(m.soiStatus!=status)FleetEventDirector.Emit(m,"SOI_PLAN_WAITING",status);m.soiStatus=status;m.nodeStatus=status;}
                return false;
            }
            if(current==m.soiDepartureBody)return true;
            if(v.packed||!v.loaded||TimeWarp.CurrentRate!=1)return false;
            double now=Planetarium.GetUniversalTime();
            // Reconcile the saved post-burn orbit against the actual incoming
            // patch. Never subtract velocities expressed about different bodies.
            if(v.orbit.referenceBody!=target.referenceBody)return false;
            double residual=(target.getOrbitalVelocityAtUT(next.UT)-v.orbit.getOrbitalVelocityAtUT(next.UT)).magnitude;
            double error=(target.getRelativePositionAtUT(next.UT)-v.orbit.getRelativePositionAtUT(next.UT)).magnitude;
            var fuel=FleetFuelForecast.Evaluate(m,residual);
            if(!fuel.Known||fuel.State==FuelForecastState.Insufficient){Hold(m,"Arrival fuel estimate: "+fuel.Reason);return false;}
            if(target.PeA<target.referenceBody.atmosphereDepth+1000){Hold(m,"Target orbit crosses atmosphere or surface safety altitude");return false;}
            string why;var result=SoiPlanPolicy.Check(m.soiDepartureBody,expected,current,now,next.UT,next.DV,residual,error,fuel.Duration,out why);
            if(result!=SoiPlanDecision.Ready){Hold(m,why);return false;}
            next.DV=residual;m.soiDepartureBody=current;m.soiStatus="Ready at "+v.mainBody.bodyName;
            FleetEventDirector.Emit(m,"SOI_PLAN_READY",m.soiStatus+"; position error "+error.ToString("F1")+" m; remaining "+residual.ToString("F2")+" m/s");return true;
        }
        public static double CoastUntil(ModuleRegionalAutonomy m){
            double now=Planetarium.GetUniversalTime(),wake=now+60;
            if(!m.soiMission||m.nodeRequested)return wake;
            try {
                var next=MissionQueue.Read(m.queuedNodes64).FirstOrDefault();if(next!=null)wake=Math.Min(wake,next.UT-180);
                var orbit=m.vessel.orbit;
                if(orbit.nextPatch!=null&&orbit.nextPatch.referenceBody!=orbit.referenceBody&&BurnMath.Finite(orbit.EndUT)&&orbit.EndUT>now)wake=Math.Min(wake,orbit.EndUT-120);
            }catch{return now;}
            return wake;
        }
        public static bool Handoff(){
            if(TimeWarp.CurrentRate!=1||Time.realtimeSinceStartup-lastVisit<15)return false;
            foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&!v.loaded&&v.protoVessel!=null)){
                foreach(var module in v.protoVessel.protoPartSnapshots.SelectMany(p=>p.modules).Where(m=>m.moduleName=="ModuleRegionalAutonomy")){
                    var n=module.moduleValues;
                    if(n.GetValue("soiMission")!="True"||n.GetValue("soiTakeover")=="False"||n.GetValue("missionHold")=="True"||n.GetValue("nodeRequested")=="True")continue;
                    try {var next=MissionQueue.Read(n.GetValue("queuedNodes64")).FirstOrDefault();
                        if(next==null||next.UT-Planetarium.GetUniversalTime()>180||MissionControl.ReadOrbit(next.Target).referenceBody!=v.mainBody)continue;
                        lastVisit=Time.realtimeSinceStartup;FleetDirector.Visit(v);return true;
                    }catch(Exception e){Debug.LogWarning("[PF-SOI] Handoff unavailable: "+e.Message);}
                }
            }
            return false;
        }
    }
}
