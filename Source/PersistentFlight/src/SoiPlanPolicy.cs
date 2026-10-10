using System;
namespace PersistentFlight {
    public interface IManeuverPlanOwner { bool ActiveManeuverPlan {get;} }
    public enum SoiPlanDecision { Wait, Ready, Hold }
    public static class SoiPlanPolicy {
        public static SoiPlanDecision Check(int departure,int expected,int current,double now,double burnUT,double plannedDV,double residualDV,double positionError,double duration,out string reason){
            reason="";
            if(!BurnMath.Finite(now)||!BurnMath.Finite(burnUT)||burnUT<=now+5){reason="Next maneuver is overdue";return SoiPlanDecision.Hold;}
            if(current!=expected){
                if(current==departure){reason="Waiting for planned body";return SoiPlanDecision.Wait;}
                reason="Entered an unexpected body";return SoiPlanDecision.Hold;
            }
            if(!BurnMath.Finite(plannedDV)||plannedDV<.5||!BurnMath.Finite(residualDV)||residualDV<.5||residualDV>Math.Max(plannedDV*1.5,plannedDV+50)||residualDV>10000){reason="Arrival needs a revised maneuver";return SoiPlanDecision.Hold;}
            if(!BurnMath.Finite(positionError)||positionError>20000||positionError<0){reason="Arrival position differs by more than 20 km";return SoiPlanDecision.Hold;}
            if(!BurnMath.Finite(duration)||duration<0||burnUT-now<duration+60){reason="Too little time to align and burn after arrival";return SoiPlanDecision.Hold;}
            reason="Arrival checked; corrected remaining burn";return SoiPlanDecision.Ready;
        }
    }
}
