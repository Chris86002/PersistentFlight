using System;
namespace PersistentFlight {
    // Native focus eligibility is deliberately separate from observational fleet cards.
    public sealed class SpectatorSituation {
        public Guid Id;public bool Armed,Loaded,Unpacked,Space,SameBody,Held,BackgroundJob;
        public double Throttle,Thrust,NextManeuver=Double.PositiveInfinity;
    }
    public static class SpectatorPolicy {
        public static string Block(SpectatorSituation s,double now){
            if(s==null||s.Id==Guid.Empty)return "Craft unavailable";
            if(!s.Armed)return "Automatic flight is not enabled";
            if(!s.Loaded||!s.Unpacked)return "Craft needs a native visit";
            if(!s.SameBody)return "Craft is on another body";
            if(!s.Space)return "Surface or atmospheric flight needs its current focus";
            if(s.Held)return "Mission is held";
            if(s.BackgroundJob)return "A background burn owns this craft";
            if(!BurnMath.Finite(s.Throttle)||!BurnMath.Finite(s.Thrust)||s.Throttle<0||s.Thrust<0||s.Throttle>.001||s.Thrust>.01)return "Powered flight is in progress";
            if(!BurnMath.Finite(now)||Double.IsNaN(s.NextManeuver)||Double.IsNegativeInfinity(s.NextManeuver)||s.NextManeuver<=now+180)return "A maneuver needs focus soon";
            return "";
        }
        public static Guid Next(SpectatorSituation[] sorted,Guid current,int direction,double now){
            if(sorted==null||sorted.Length==0)return Guid.Empty;
            int start=Array.FindIndex(sorted,s=>s!=null&&s.Id==current);if(start<0)start=direction<0?0:sorted.Length-1;
            int step=direction<0?-1:1;
            for(int n=1;n<=sorted.Length;n++){int i=((start+n*step)%sorted.Length+sorted.Length)%sorted.Length;var s=sorted[i];if(s!=null&&s.Id!=current&&Block(s,now)=="")return s.Id;}
            return Guid.Empty;
        }
    }
}
