using System;
using System.Linq;
using PersistentFlight;
public static class FlightDebriefTests {
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static FlightDebrief Report(){return new FlightDebrief{Id=Guid.NewGuid(),Branch=Guid.NewGuid(),Vessel=Guid.NewGuid(),PlannedDV=25,QueuedUT=100,NodeUT=150,StartUT=110,Started=true};}
    public static int Main(){try{
        var random=new Random(18018);
        for(int i=0;i<10000;i++){
            var r=Report();double thrust=random.NextDouble()*1000,mass=1+random.NextDouble()*100,dt=.01+random.NextDouble()*.09;
            r.Integrate(thrust,mass,dt);Check(Math.Abs(r.ImpulseDV-thrust/mass*dt)<1e-12,"Impulse units/integration");
            r.Integrate(thrust,mass,1);Check(r.Incomplete&&Math.Abs(r.ImpulseDV-thrust/mass*dt)<1e-12,"Gap invented impulse");
            r.Finished=true;r.EndUT=160;double dv=r.ImpulseDV;r.Integrate(thrust,mass,dt);Check(r.ImpulseDV==dv,"Closed record changed");r.Validate();
        }
        foreach(double n in new[]{Double.NaN,Double.PositiveInfinity,-1}){var r=Report();r.Integrate(n,1,.02);Check(r.Incomplete&&r.ImpulseDV==0,"Invalid thrust accepted");}
        var fresh=Report();fresh.Started=false;fresh.StartUT=0;fresh.Integrate(100,10,.02);Check(fresh.ImpulseDV==0,"Preburn counted");
        var ledger=new FlightDebriefLedger();var pending=Report();ledger.Add(pending);
        for(int i=0;i<FlightDebriefLedger.Limit*3;i++){var r=Report();r.Finished=true;r.EndUT=200;ledger.Add(r);}
        Check(ledger.Records.Count==1000&&ledger.Find(pending.Id)==pending,"Retention evicted active or grew unbounded");
        var snapshot=ledger.Snapshot();snapshot[0].Outcome="Changed";Check(ledger.Records[0].Outcome!="Changed","Snapshot mutated ledger");
        bool duplicate=false;try{ledger.Add(pending.Copy());}catch(FormatException){duplicate=true;}Check(duplicate,"Duplicate history admitted");
        var full=new FlightDebriefLedger();for(int i=0;i<1000;i++)full.Add(Report());bool bounded=false;try{full.Add(Report());}catch(InvalidOperationException){bounded=true;}Check(bounded&&full.Records.Count==1000,"Full active ledger grew");
        var refuel=Report();refuel.LiquidFuel=-3;refuel.Validate();Check(refuel.LiquidFuel==-3,"Refuelling hidden");
        var bad=Report();bad.ResidualDV=-1;bool invalid=false;try{bad.Validate();}catch(FormatException){invalid=true;}Check(invalid,"Negative residual accepted");
        var land=new FlightDebrief{Id=Guid.NewGuid(),Branch=Guid.NewGuid(),Vessel=Guid.NewGuid(),Mode=DebriefMode.Landing,QueuedUT=100,StartUT=100,Started=true,InitialParts=3};land.Validate();
        Check(Math.Abs(FlightDebrief.SurfaceDistance(600000,0,0,0,90)-300000*Math.PI)<1e-7,"Quarter-body distance");
        Check(Math.Abs(FlightDebrief.SurfaceDistance(600000,0,179,0,-179)-600000*Math.PI/90)<1e-7,"Longitude wrap distance");
        Check(FlightDebrief.SurfaceDistance(600000,90,0,90,180)<1e-7,"Pole distance");
        land.LostParts=4;bool badLanding=false;try{land.Validate();}catch(FormatException){badLanding=true;}Check(badLanding,"Impossible part loss accepted");
        land.LostParts=0;land.TargetDistance=Double.PositiveInfinity;badLanding=false;try{land.Validate();}catch(FormatException){badLanding=true;}Check(badLanding,"Infinite miss distance accepted");
        Console.WriteLine("PASS "+checks+" flight debrief checks");return 0;
    }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
