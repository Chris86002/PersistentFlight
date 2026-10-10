using System;using System.IO;using System.Linq;using PersistentFlight.Regions;
class CircularTransferTests {
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}static void Reject(Action a,string s){bool denied=false;try{a();}catch{denied=true;}Check(denied,s);}
 static MissionTemplate T(double altitude){return new MissionTemplate{Id=Guid.NewGuid(),Name="Reach circular target",Body="Kerbin",SourceSMA=800000,SourceEcc=.1,SourceInclination=0,Goal=TemplateGoal.CircularAltitude,TargetAltitude=altitude};}
 static GoalOrbitInput O(double a=800000,double e=.1){double mu=3.5316e12;return new GoalOrbitInput{Mu=mu,BodyRadius=600000,AtmosphereDepth=70000,SphereOfInfluence=84159286,SMA=a,Eccentricity=e,Period=2*Math.PI*Math.Sqrt(a*a*a/mu),TimeToApoapsis=90,TimeToPeriapsis=500,Now=10000};}
 static void Physical(double a,double e,double altitude){var o=O(a,e);var p=GoalOrbitMath.Calculate(T(altitude),o);Check(p.Burns.Length>=1&&p.Burns.Length<=2,"Unexpected burn count");Check(Math.Abs(p.DV-p.Burns.Sum(b=>Math.Abs(b.Prograde)))<1e-10,"Forecast missed a burn");double oldA=a;double expectedTarget=o.BodyRadius+altitude;double previous=o.Now;
  for(int i=0;i<p.Burns.Length;i++){var b=p.Burns[i];Check(b.UT-previous>=60,"Insufficient preparation time");Check(Math.Abs(b.Prograde)>=.5&&Math.Abs(b.Prograde)<=10000,"Unqueueable burn");double r=i==0?(Math.Abs(b.UT-(o.Now+o.TimeToApoapsis))<.01?a*(1+e):Math.Abs(b.UT-(o.Now+o.TimeToPeriapsis))<.01?a*(1-e):a):expectedTarget;double v=Math.Sqrt(o.Mu*(2/r-1/oldA))+b.Prograde;double newA=1/(2/r-v*v/o.Mu),h=r*v,newE=Math.Sqrt(Math.Max(0,1-h*h/(o.Mu*newA)));Check(Math.Abs(newA*(1-newE)-o.BodyRadius-b.PeriapsisAltitude)<.1&&Math.Abs(newA*(1+newE)-o.BodyRadius-b.ApoapsisAltitude)<.1,"Burn's energy/angular momentum disagree with preview");if(i>0){double halfPeriod=Math.PI*Math.Sqrt(oldA*oldA*oldA/o.Mu);Check(Math.Abs(b.UT-previous-halfPeriod)<.001,"Arrival time differs from transfer half-period");}oldA=newA;previous=b.UT;
  }
  Check(Math.Abs(oldA-expectedTarget)<.001&&Math.Abs(p.ApoapsisAltitude-altitude)<.001&&Math.Abs(p.PeriapsisAltitude-altitude)<.001,"Final orbit not target circle");
 }
 static int Main(){try{
  foreach(double e in new[]{0,.02,.1,.15})foreach(double alt in new[]{100000d,160000d,250000d,400000d,800000d})Physical(800000,e,alt);
  Physical(800000,.1,280000);Physical(800000,.1,120000);
  var high=GoalOrbitMath.Calculate(T(400000),O());Check(high.Burns.Length==2&&high.Burns.All(b=>b.Prograde>0),"Raising target did not use prograde burns");var low=GoalOrbitMath.Calculate(T(100000),O());Check(low.Burns.Length==2&&low.Burns.All(b=>b.Prograde<0),"Lowering target did not use retrograde burns");
  Check(GoalOrbitMath.Calculate(T(280000),O()).Burns.Length==1&&GoalOrbitMath.Calculate(T(120000),O()).Burns.Length==1,"Existing target apsis did not collapse to one burn");
  var encoded=MissionTemplateStore.Encode(T(160000));var copy=MissionTemplateStore.Decode(encoded);Check(copy.Goal==TemplateGoal.CircularAltitude&&copy.TargetAltitude==160000&&copy.Steps.Count==0,"New goal file stored copied burn plan");
  var near=O();near.TimeToPeriapsis=10;var delayed=GoalOrbitMath.Calculate(T(400000),near);Check(Math.Abs(delayed.UT-(10010+near.Period))<.001,"Near starting apsis did not wait an orbit");var circle=O(800000,0);Check(GoalOrbitMath.Calculate(T(400000),circle).UT==10060,"Circle did not use an arbitrary future point");
  var encounter=O();encounter.BodyChangeUT=high.Burns[1].UT-10;Reject(()=>GoalOrbitMath.Calculate(T(400000),encounter),"Encounter between burns accepted");encounter.BodyChangeUT=high.Burns[1].UT+10;Check(GoalOrbitMath.Calculate(T(400000),encounter).Burns.Length==2,"Plan before encounter refused");
  Reject(()=>GoalOrbitMath.Calculate(T(70000),O()),"Atmospheric circle accepted");Reject(()=>GoalOrbitMath.Calculate(T(90000000),O()),"SOI escape circle accepted");Reject(()=>GoalOrbitMath.Calculate(T(200000),O(800000,0)),"Already circular target produced fake burns");Reject(()=>T(0).Validate(),"Missing circular altitude accepted");Reject(()=>T(Double.NaN).Validate(),"NaN target accepted");
  // A small required correction must not be silently dropped from a claimed exact orbit.
  Reject(()=>GoalOrbitMath.Calculate(T(200010),O(800000,0)),"Sub-minimum burns silently dropped");
  var rapid=O(800000,0);rapid.Mu*=1000;rapid.Period/=Math.Sqrt(1000);Reject(()=>GoalOrbitMath.Calculate(T(250000),rapid),"Insufficient time between real burns accepted");
  Console.WriteLine("PASS "+checks+" circular transfer checks");return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
