using System;
using PersistentFlight;
class FuelForecastTests {
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Near(double a,double b,string why){Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),why);}
    static ForecastFuel[] Fuel(double amount){return new[]{new ForecastFuel("LiquidFuel",amount,100,.45,.005),new ForecastFuel("Oxidizer",amount*11/9,100.0*11/9,.55,.005)};}
    static FuelForecastResult Run(double dv,double reserve,double margin,double fraction){return FuelForecast.Evaluate(5,20,.005,Fuel(90),dv,reserve,margin,fraction);}
    static void Main(){try{
        double available=4000*Math.Log(5.0/4.0);
        var r=Run(100,0,0,0);Check(r.WithinReserve,"Small plan rejected");Near(r.AvailableDV,available,"Rocket equation");Near(r.Duration,1000*(1-Math.Exp(-100.0/4000)),"Duration");Near(r.FuelMass,r.Duration*.005,"Mass budget");Near(r.RemainingDV,available-100,"Remaining delta-v");
        Check(Run(available+1,0,0,0).State==FuelForecastState.Insufficient,"Exhaustion not warned");Check(Run(available,0,0,0).WithinReserve,"Exact exhaustion boundary");
        Check(Run(available-50,100,0,0).State==FuelForecastState.ReserveRisk,"DV reserve not protected");Check(Run(available-100,100,0,0).WithinReserve,"Reserve boundary");
        Check(Run(available-1,0,.1,0).State==FuelForecastState.Insufficient,"Margin ignored");Check(Run(available-1,0,0,.1).State==FuelForecastState.ReserveRisk,"Fraction reserve ignored");
        var low=Fuel(90);low[1]=new ForecastFuel("Oxidizer",10,100.0*11/9,.55,.005);r=FuelForecast.Evaluate(5,20,.005,low,100,0,0,0);Check(r.State==FuelForecastState.Insufficient&&r.LimitingFuel=="Oxidizer","Mixture limiting fuel");
        var none=Fuel(90);none[1]=new ForecastFuel("Oxidizer",0,0,.55,.005);r=FuelForecast.Evaluate(5,20,.005,none,1,0,0,0);Check(r.State==FuelForecastState.Insufficient&&r.AvailableDV==0,"Missing propellant");
        var half=FuelForecast.Evaluate(5,10,.0025,new[]{new ForecastFuel("LiquidFuel",90,100,.225,.005),new ForecastFuel("Oxidizer",110,100.0*11/9,.275,.005)},100,0,0,0);Near(half.AvailableDV,Run(100,0,0,0).AvailableDV,"Limiter changed available DV");Near(half.Duration,Run(100,0,0,0).Duration*2,"Limiter duration");
        Check(!FuelForecast.Evaluate(5,20,.005,null,0,0,0,0).Known,"Null fuel accepted");Check(!FuelForecast.Evaluate(5,20,.005,new[]{Fuel(90)[0],Fuel(90)[0]},0,0,0,0).Known,"Duplicates accepted");
        foreach(double bad in new[]{Double.NaN,Double.PositiveInfinity,-1.0}){Check(!Run(bad,0,0,0).Known,"Bad DV accepted");Check(!Run(1,bad,0,0).Known,"Bad reserve accepted");Check(!Run(1,0,bad,0).Known,"Bad margin accepted");}
        Check(!FuelForecast.Evaluate(5,20,.01,Fuel(90),1,0,0,0).Known,"Mass/rate mismatch accepted");Check(!FuelForecast.Evaluate(1,20,.005,Fuel(90),1,0,0,0).Known,"No dry mass accepted");
        var random=new Random(816);
        for(int i=0;i<10000;i++){
            double dry=1+random.NextDouble()*20,prop=.01+random.NextDouble()*10,mass=dry+prop,ve=1000+random.NextDouble()*4000,flow=.001+random.NextDouble()*.1,limitDV=ve*Math.Log(mass/dry);
            double dv=random.NextDouble()*limitDV*.8,reserve=random.NextDouble()*limitDV*.1;
            r=FuelForecast.Evaluate(mass,ve*flow,flow,new[]{new ForecastFuel("Fuel",prop,prop,flow,1)},dv,reserve,0,0);
            Check(r.WithinReserve,"Random sufficient plan rejected");Near(r.AvailableDV,limitDV,"Random available DV");
            double after=mass*Math.Exp(-dv/ve);Near(r.FuelMass,mass-after,"Random consumed mass");Near(r.RemainingDV,ve*Math.Log(after/dry),"Random remaining DV");
        }
        Console.WriteLine("PASS "+checks+" fuel forecast checks");
    }catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.Exit(1);}}
}
