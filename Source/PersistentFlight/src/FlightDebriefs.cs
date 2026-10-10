using System;
using System.Collections.Generic;
using System.Linq;
namespace PersistentFlight {
    public enum DebriefMode { LiveManeuver, BackgroundBurn, Landing }
    // Pure accounting model. Live impulse is a scalar thrust integral, not target closure.
    public sealed class FlightDebrief {
        public Guid Id,Branch,Vessel;
        public uint Cpu;
        public string LandingTarget="",LandingNote="",Contact="";
        public double Latitude=Double.NaN,Longitude=Double.NaN,TouchdownSpeed=Double.NaN,VerticalSpeed=Double.NaN,HorizontalSpeed=Double.NaN,TargetDistance=Double.NaN;
        public double RemainingLF=Double.NaN,RemainingOx=Double.NaN,RemainingEC=Double.NaN;
        public int InitialParts,LostParts;
        public static double SurfaceDistance(double radius,double lat,double lon,double targetLat,double targetLon){
            double k=Math.PI/180,a=(targetLat-lat)*k,b=(targetLon-lon)*k;
            double h=Math.Sin(a/2)*Math.Sin(a/2)+Math.Cos(lat*k)*Math.Cos(targetLat*k)*Math.Sin(b/2)*Math.Sin(b/2);
            return radius*2*Math.Asin(Math.Sqrt(Math.Max(0,Math.Min(1,h))));
        }
        public string Craft="",Body="",Outcome="Pending";
        public DebriefMode Mode;
        public double PlannedDV,QueuedUT,NodeUT,StartUT,EndUT,ImpulseDV,PoweredSeconds,LastSampleUT;
        public double ResidualDV=Double.NaN,LiquidFuel,Oxidizer,ElectricCharge;
        public double InitialLF,InitialOx,InitialEC;
        public bool Started,Finished,Incomplete,ResourcesKnown;
        public void Integrate(double thrust,double mass,double dt) {
            if(!Finite(thrust)||!Finite(mass)||!Finite(dt)||thrust<0||mass<=0||dt<0||dt>.2){Incomplete=true;return;}
            if(Finished||!Started)return;
            if(thrust>0){ImpulseDV+=thrust/mass*dt;PoweredSeconds+=dt;}
        }
        public static bool Finite(double n){return !Double.IsNaN(n)&&!Double.IsInfinity(n);}
        public void Validate(){
            ValidateLanding();
            if(Id==Guid.Empty||Branch==Guid.Empty||Vessel==Guid.Empty||!Enum.IsDefined(typeof(DebriefMode),Mode))throw new FormatException("Invalid debrief identity");
            foreach(var s in new[]{Craft,Body,Outcome,LandingTarget,LandingNote,Contact})if(s==null||s.Length>240||s.IndexOfAny(new[]{'\r','\n'})>=0)throw new FormatException("Invalid debrief text");
            foreach(var n in new[]{PlannedDV,QueuedUT,NodeUT,StartUT,EndUT,ImpulseDV,PoweredSeconds,LastSampleUT,InitialLF,InitialOx,InitialEC})if(!Finite(n)||n<0)throw new FormatException("Invalid debrief measurement");
            foreach(var n in new[]{LiquidFuel,Oxidizer,ElectricCharge})if(!Finite(n))throw new FormatException("Invalid resource change");
            if(!Double.IsNaN(ResidualDV)&&(!Finite(ResidualDV)||ResidualDV<0))throw new FormatException("Invalid residual");
            if((Mode!=DebriefMode.Landing&&PlannedDV<=0)||PlannedDV>10000||Finished&&(EndUT<QueuedUT||Started&&EndUT<StartUT)||Started&&StartUT<QueuedUT||!Started&&(ImpulseDV!=0||PoweredSeconds!=0))throw new FormatException("Invalid debrief state");
        }
        public static bool LandingField(string name){return new[]{"LandingTarget","LandingNote","Contact","Latitude","Longitude","TouchdownSpeed","VerticalSpeed","HorizontalSpeed","TargetDistance","RemainingLF","RemainingOx","RemainingEC","InitialParts","LostParts"}.Contains(name);}
        public void ValidateLanding(){
            if(InitialParts<0||LostParts<0||LostParts>InitialParts)throw new FormatException("Invalid landing part count");
            foreach(double n in new[]{TouchdownSpeed,HorizontalSpeed,TargetDistance})if(!Double.IsNaN(n)&&(!Finite(n)||n<0))throw new FormatException("Invalid landing measurement");
            foreach(double n in new[]{RemainingLF,RemainingOx,RemainingEC})if(!Double.IsNaN(n)&&(!Finite(n)||n<0))throw new FormatException("Invalid remaining resources");
            if(!Double.IsNaN(Latitude)&&(!Finite(Latitude)||Math.Abs(Latitude)>90)||!Double.IsNaN(Longitude)&&(!Finite(Longitude)||Math.Abs(Longitude)>180)||!Double.IsNaN(VerticalSpeed)&&!Finite(VerticalSpeed))throw new FormatException("Invalid contact coordinates or speed");
        }
        public FlightDebrief Copy(){return (FlightDebrief)MemberwiseClone();}
    }
    public sealed class FlightDebriefLedger {
        public const int Limit=1000;
        public readonly List<FlightDebrief> Records=new List<FlightDebrief>();
        public Guid Branch=Guid.NewGuid();
        public FlightDebrief Find(Guid id){return Records.FirstOrDefault(r=>r.Id==id);}
        public void Add(FlightDebrief r){
            r.Validate();if(Find(r.Id)!=null)throw new FormatException("Duplicate debrief");
            if(Records.Count>=Limit){var old=Records.FirstOrDefault(x=>x.Finished);if(old==null)throw new InvalidOperationException("Debrief capacity reached");Records.Remove(old);}
            Records.Add(r);
        }
        public FlightDebrief[] Snapshot(){return Records.Select(r=>r.Copy()).ToArray();}
    }
}
