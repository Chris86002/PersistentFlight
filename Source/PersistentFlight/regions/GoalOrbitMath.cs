using System;using System.Linq;using System.Collections.Generic;
namespace PersistentFlight.Regions {
 public sealed class GoalOrbitInput {
  public double Mu,BodyRadius,AtmosphereDepth,SphereOfInfluence,SMA,Eccentricity,Period,TimeToApoapsis,TimeToPeriapsis,Now,BodyChangeUT;
 }
 public sealed class GoalOrbitBurn {
  public double UT,Prograde,PeriapsisAltitude,ApoapsisAltitude;
 }
 public sealed class GoalOrbitPreview {
  public double UT,Prograde,PeriapsisAltitude,ApoapsisAltitude;
  public GoalOrbitBurn[] Burns=new GoalOrbitBurn[0];
  public double DV {get{return Burns.Length==0?Math.Abs(Prograde):Burns.Sum(b=>Math.Abs(b.Prograde));}}
 }
 public static class GoalOrbitMath {
  static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
  public static bool AtApoapsis(TemplateGoal goal){return goal==TemplateGoal.CircularizeApoapsis||goal==TemplateGoal.OppositeFromApoapsis;}
  public static string Label(TemplateGoal goal){switch(goal){case TemplateGoal.CircularizeApoapsis:return "Circularize at apoapsis";case TemplateGoal.CircularizePeriapsis:return "Circularize at periapsis";case TemplateGoal.OppositeFromApoapsis:return "Set periapsis — burn at apoapsis";case TemplateGoal.OppositeFromPeriapsis:return "Set apoapsis — burn at periapsis";case TemplateGoal.CircularAltitude:return "Reach circular orbit at altitude";default:return "Maneuver sequence";}}
  static double TimeAt(GoalOrbitInput o,bool ap){double wait=o.Eccentricity==0?60:(ap?o.TimeToApoapsis:o.TimeToPeriapsis);if(!Finite(wait)||wait<0||wait>o.Period+1)throw new InvalidOperationException("Next apsis timing is unavailable");if(wait<60)wait+=Math.Ceiling((60-wait)/o.Period)*o.Period;return o.Now+wait;}
  static void SafeTime(GoalOrbitInput o,double ut){if(!Finite(ut)||ut-o.Now>31536000||ut<=o.Now||ut-o.Now<60)throw new InvalidOperationException("Apsis cannot be scheduled safely within one year");if(o.BodyChangeUT!=0&&(!Finite(o.BodyChangeUT)||o.BodyChangeUT<=o.Now||ut>=o.BodyChangeUT-1))throw new InvalidOperationException("A body encounter occurs before this plan finishes; use the SOI flight planner");}
  static GoalOrbitPreview Result(List<GoalOrbitBurn> burns,double pe,double ap){if(burns.Count==0)throw new InvalidOperationException("Orbit already meets this goal within maneuver tolerance");return new GoalOrbitPreview{UT=burns[0].UT,Prograde=burns[0].Prograde,PeriapsisAltitude=pe,ApoapsisAltitude=ap,Burns=burns.ToArray()};}
  static void AddBurn(List<GoalOrbitBurn> burns,GoalOrbitInput o,double ut,double dv,double pe,double ap){if(!Finite(dv)||Math.Abs(dv)>10000)throw new InvalidOperationException("Calculated burn exceeds the supported delta-v range");if(Math.Abs(dv)<=1e-6)return;if(Math.Abs(dv)<.5)throw new InvalidOperationException("A required burn is below 0.5 m/s; choose a different target altitude");SafeTime(o,ut);if(burns.Count>0&&ut-burns[burns.Count-1].UT<60)throw new InvalidOperationException("Transfer burns need at least 60 seconds between them");burns.Add(new GoalOrbitBurn{UT=ut,Prograde=dv,PeriapsisAltitude=pe,ApoapsisAltitude=ap});}
  static GoalOrbitPreview CircularCandidate(GoalOrbitInput o,bool atAp,double radius,double target){
   var burns=new List<GoalOrbitBurn>();double transferSMA=(radius+target)/2;
   double first=Math.Sqrt(o.Mu*(2/radius-1/transferSMA))-Math.Sqrt(o.Mu*(2/radius-1/o.SMA));
   double second=Math.Sqrt(o.Mu/target)-Math.Sqrt(o.Mu*(2/target-1/transferSMA));
   double firstUT=TimeAt(o,atAp),halfPeriod=Math.PI*Math.Sqrt(transferSMA*transferSMA*transferSMA/o.Mu);
   double secondUT=Math.Abs(first)<=1e-6?TimeAt(o,!atAp):firstUT+halfPeriod;
   if(!Finite(halfPeriod)||halfPeriod<=0)throw new InvalidOperationException("Transfer timing is unavailable");
   AddBurn(burns,o,firstUT,first,Math.Min(radius,target)-o.BodyRadius,Math.Max(radius,target)-o.BodyRadius);
   AddBurn(burns,o,secondUT,second,target-o.BodyRadius,target-o.BodyRadius);
   return Result(burns,target-o.BodyRadius,target-o.BodyRadius);
  }
  static GoalOrbitPreview CircularAltitude(MissionTemplate t,GoalOrbitInput o,double pe,double ap,double floor,double ceiling){
   double target=o.BodyRadius+t.TargetAltitude;if(!Finite(target)||target<floor||target>=ceiling)throw new InvalidOperationException("Target altitude must clear the atmosphere or surface and remain inside this body's sphere of influence");
   var candidates=new List<GoalOrbitPreview>();string failure="No supported apsis transfer is available";
   foreach(bool atAp in new[]{true,false}){double radius=atAp?ap:pe;if((atAp&&target>radius)||(!atAp&&target<radius))continue;try{candidates.Add(CircularCandidate(o,atAp,radius,target));}catch(InvalidOperationException e){failure=e.Message;}}
   if(candidates.Count==0)throw new InvalidOperationException(failure);
   return candidates.OrderBy(p=>p.DV).ThenBy(p=>p.Burns[p.Burns.Length-1].UT).First();
  }
  public static GoalOrbitPreview Calculate(MissionTemplate template,GoalOrbitInput o){
   template.Validate();if(template.Goal==TemplateGoal.Sequence)throw new InvalidOperationException("Select an orbital goal");
   if(o==null||!Finite(o.Mu)||o.Mu<=0||!Finite(o.BodyRadius)||o.BodyRadius<=0||!Finite(o.AtmosphereDepth)||o.AtmosphereDepth<0||!Finite(o.SphereOfInfluence)||o.SphereOfInfluence<=o.BodyRadius||!Finite(o.SMA)||o.SMA<=0||!Finite(o.Eccentricity)||o.Eccentricity<0||o.Eccentricity>=1||!Finite(o.Period)||o.Period<=0||!Finite(o.Now)||o.Now<0)throw new InvalidOperationException("Use a finite, bound elliptical orbit");
   double pe=o.SMA*(1-o.Eccentricity),ap=o.SMA*(1+o.Eccentricity),floor=o.BodyRadius+Math.Max(1000,o.AtmosphereDepth+1000),ceiling=o.SphereOfInfluence*.95;
   if(pe<floor||ap>=ceiling)throw new InvalidOperationException("Current orbit must clear the atmosphere or surface and remain inside this body's sphere of influence");
   if(template.Goal==TemplateGoal.CircularAltitude)return CircularAltitude(template,o,pe,ap,floor,ceiling);
   bool atAp=AtApoapsis(template.Goal);double radius=atAp?ap:pe;
   bool circle=template.Goal==TemplateGoal.CircularizeApoapsis||template.Goal==TemplateGoal.CircularizePeriapsis;
   double opposite=circle?radius:o.BodyRadius+template.TargetAltitude;
   if(!Finite(opposite)||opposite<floor||opposite>=ceiling)throw new InvalidOperationException("Target altitude must clear the atmosphere or surface and remain inside this body's sphere of influence");
   if((atAp&&opposite>radius)||(!atAp&&opposite<radius))throw new InvalidOperationException(atAp?"Target periapsis cannot be above the current apoapsis":"Target apoapsis cannot be below the current periapsis");
   double ut=TimeAt(o,atAp);SafeTime(o,ut);
   double targetSMA=(radius+opposite)/2,dv=Math.Sqrt(o.Mu*(2/radius-1/targetSMA))-Math.Sqrt(o.Mu*(2/radius-1/o.SMA));
   if(!Finite(dv)||Math.Abs(dv)>10000)throw new InvalidOperationException("Calculated burn exceeds the supported delta-v range");
   if(Math.Abs(dv)<.5)throw new InvalidOperationException("Orbit already meets this goal within the 0.5 m/s maneuver tolerance");
   var burns=new List<GoalOrbitBurn>{new GoalOrbitBurn{UT=ut,Prograde=dv,PeriapsisAltitude=Math.Min(radius,opposite)-o.BodyRadius,ApoapsisAltitude=Math.Max(radius,opposite)-o.BodyRadius}};return Result(burns,Math.Min(radius,opposite)-o.BodyRadius,Math.Max(radius,opposite)-o.BodyRadius);
  }
 }
}
