using System;
using PersistentFlight;
public static class SpectatorTests {
    static int checks;static void Check(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static SpectatorSituation Craft(){return new SpectatorSituation{Id=Guid.NewGuid(),Armed=true,Loaded=true,Unpacked=true,SameBody=true,Space=true};}
    public static int Main(){try{
        var a=Craft();var b=Craft();var c=Craft();var fleet=new[]{a,b,c};
        Check(SpectatorPolicy.Next(fleet,a.Id,1,100)==b.Id,"Forward cycle");Check(SpectatorPolicy.Next(fleet,a.Id,-1,100)==c.Id,"Reverse wrap");
        b.BackgroundJob=true;Check(SpectatorPolicy.Next(fleet,a.Id,1,100)==c.Id,"Skipped analytic job");c.Loaded=false;Check(SpectatorPolicy.Next(fleet,a.Id,1,100)==Guid.Empty,"No eligible target");
        Check(SpectatorPolicy.Next(new[]{a},a.Id,1,100)==Guid.Empty,"Single craft reselected");
        var random=new Random(19019);
        for(int i=0;i<10000;i++){
            var s=Craft();double now=random.NextDouble()*1e8;double lead=181+random.NextDouble()*5000;s.NextManeuver=now+lead;
            Check(SpectatorPolicy.Block(s,now)=="","Coast blocked");s.NextManeuver=now+random.NextDouble()*180;Check(SpectatorPolicy.Block(s,now)!="","Imminent maneuver switched");
            s.NextManeuver=Double.PositiveInfinity;s.Throttle=.0011+random.NextDouble();Check(SpectatorPolicy.Block(s,now)!="","Powered flight switched");
        }
        foreach(double n in new[]{Double.NaN,Double.NegativeInfinity,100}){var s=Craft();s.NextManeuver=n;Check(SpectatorPolicy.Block(s,100)!="","Invalid/overdue deadline accepted");}
        foreach(double n in new[]{Double.NaN,Double.PositiveInfinity}){var s=Craft();s.Thrust=n;Check(SpectatorPolicy.Block(s,100)!="","Invalid thrust accepted");}
        foreach(Action<SpectatorSituation> set in new Action<SpectatorSituation>[]{s=>s.Armed=false,s=>s.Loaded=false,s=>s.Unpacked=false,s=>s.Space=false,s=>s.SameBody=false,s=>s.Held=true,s=>s.BackgroundJob=true}){var s=Craft();set(s);Check(SpectatorPolicy.Block(s,100)!="","Unsafe situation accepted");}
        Console.WriteLine("PASS "+checks+" spectator checks");return 0;
    }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
