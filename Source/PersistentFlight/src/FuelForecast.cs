using System;
using System.Linq;
namespace PersistentFlight {
    public enum FuelForecastState { Unknown, Ready, ReserveRisk, Insufficient }
    public sealed class ForecastFuel {
        public readonly string Name;
        public readonly double Amount, Capacity, Rate, Density;
        public ForecastFuel(string name,double amount,double capacity,double rate,double density){Name=name;Amount=amount;Capacity=capacity;Rate=rate;Density=density;}
    }
    public sealed class FuelForecastResult {
        public FuelForecastState State {get;private set;}
        public string Reason {get;private set;}
        public double PlanDV {get;private set;}
        public double BudgetDV {get;private set;}
        public double AvailableDV {get;private set;}
        public double RemainingDV {get;private set;}
        public double Duration {get;private set;}
        public double FuelMass {get;private set;}
        public double MinimumFraction {get;private set;}
        public string LimitingFuel {get;private set;}
        public bool Known {get{return State!=FuelForecastState.Unknown;}}
        public bool WithinReserve {get{return State==FuelForecastState.Ready;}}
        internal FuelForecastResult(FuelForecastState state,string reason,double plan,double budget,double available,double remaining,double duration,double mass,double fraction,string limiting){State=state;Reason=reason;PlanDV=plan;BudgetDV=budget;AvailableDV=available;RemainingDV=remaining;Duration=duration;FuelMass=mass;MinimumFraction=fraction;LimitingFuel=limiting;}
        public static FuelForecastResult Unknown(string why){return new FuelForecastResult(FuelForecastState.Unknown,why,0,0,0,0,0,0,0,"");}
    }
    // One fixed engine group, constant vacuum Isp and mixture. No staging,
    // tank transfers, RCS, gravity/steering losses or atmospheric landing model.
    public static class FuelForecast {
        public static FuelForecastResult Evaluate(double mass,double thrust,double flow,ForecastFuel[] fuels,double plan,double reserveDV,double margin,double reserveFraction){
            if(!BurnMath.Finite(mass)||mass<=0||!BurnMath.Finite(thrust)||thrust<=0||!BurnMath.Finite(flow)||flow<=0||
                !BurnMath.Finite(plan)||plan<0||plan>320000||!BurnMath.Finite(reserveDV)||reserveDV<0||reserveDV>10000||
                !BurnMath.Finite(margin)||margin<0||margin>1||!BurnMath.Finite(reserveFraction)||reserveFraction<0||reserveFraction>.9||fuels==null||fuels.Length==0||fuels.Length>32)
                return FuelForecastResult.Unknown("Invalid forecast inputs");
            if(fuels.Any(f=>f==null||String.IsNullOrEmpty(f.Name)||!BurnMath.Finite(f.Amount)||!BurnMath.Finite(f.Capacity)||!BurnMath.Finite(f.Rate)||!BurnMath.Finite(f.Density)||f.Amount<0||f.Capacity<f.Amount||f.Rate<=0||f.Density<=0)||fuels.Select(f=>f.Name).Distinct().Count()!=fuels.Length)
                return FuelForecastResult.Unknown("Invalid or duplicate propellant inputs");
            if(Math.Abs(fuels.Sum(f=>f.Rate*f.Density)-flow)>flow*1e-6||fuels.Sum(f=>f.Amount*f.Density)>=mass)
                return FuelForecastResult.Unknown("Propellant mass does not match the engine model");
            var limit=fuels.OrderBy(f=>f.Amount/f.Rate).First();double time=limit.Amount/limit.Rate,used=flow*time;
            double available=thrust/flow*Math.Log(mass/(mass-used));
            double budget=plan*(1+margin),duration=BurnMath.TimeForDeltaV(thrust,flow,mass,budget),cost=flow*duration;
            double fraction=fuels.Min(f=>f.Capacity>0?Math.Max(0,(f.Amount-f.Rate*duration)/f.Capacity):0);
            double remaining=Math.Max(0,available-budget);
            var state=budget>available+1e-7?FuelForecastState.Insufficient:remaining+1e-7<reserveDV||fraction+1e-9<reserveFraction?FuelForecastState.ReserveRisk:FuelForecastState.Ready;
            string reason=state==FuelForecastState.Insufficient?"Plan exceeds connected propellant":state==FuelForecastState.ReserveRisk?"Plan enters the chosen vacuum/fuel reserve":"Plan fits the chosen reserve";
            return new FuelForecastResult(state,reason,plan,budget,available,remaining,duration,cost,fraction,limit.Name);
        }
    }
}
