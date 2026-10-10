using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public double landingReserveDV=0,burnMargin=.1;
        [KSPField(isPersistant=true)] public bool forecastQueue=true;
        public FuelForecastResult fuelForecast;
        public string forecastSource="",forecastEventState="";
        public double forecastUT;
        public float nextFuelForecast;
    }
    public static class FleetFuelForecast {
        public static FuelForecastResult Check(ModuleRegionalAutonomy m,double dv,string source){
            var result=Evaluate(m,dv);m.fuelForecast=result;m.forecastSource=source;m.forecastUT=Planetarium.GetUniversalTime();return result;
        }
        public static FuelForecastResult Evaluate(ModuleRegionalAutonomy m,double dv){
            try {
                var v=m==null?null:m.vessel;
                if(v==null||!v.loaded||v.packed)return FuelForecastResult.Unknown("Load and unpack this craft first");
                if(v.mainBody==null||v.altitude<v.mainBody.atmosphereDepth+1000||v.LandedOrSplashed)return FuelForecastResult.Unknown("Vacuum orbital forecast only");
                var engines=v.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Where(e=>e.isEnabled&&((e.EngineIgnited&&!e.engineShutdown)||(!String.IsNullOrEmpty(m.pilotEngineTag)&&e.part.Modules.OfType<kOS.Module.KOSNameTag>().Any(t=>t.nameTag==m.pilotEngineTag)))).ToArray();
                if(engines.Length==0)return FuelForecastResult.Unknown("Activate engines at zero throttle, or select the pilot engine tag");
                v.UpdateResourceSetsIfDirty();
                var rates=new Dictionary<string,double>();var densities=new Dictionary<string,double>();var pools=new Dictionary<string,uint[]>();
                double thrust=0,flow=0;
                foreach(var e in engines){
                    if((e.GetType()!=typeof(ModuleEngines)&&e.GetType()!=typeof(ModuleEnginesFX))||e.throttleLocked||e.atmChangeFlow||e.useThrustCurve||e.useVelCurve||e.useVelCurveIsp||e.useThrottleIspCurve||e.useEngineResponseTime||e.nonThrustMotor||e.independentThrottle||e.part.Modules.Cast<PartModule>().Any(x=>x.moduleName=="MultiModeEngine"))return FuelForecastResult.Unknown("Engine needs a dedicated forecast adapter");
                    double isp=e.atmosphereCurve.Evaluate(0),force=e.maxThrust*e.thrustPercentage/100.0;
                    if(!BurnMath.Finite(isp)||isp<=0||!BurnMath.Finite(force)||force<=0)return FuelForecastResult.Unknown("Selected engine has no usable vacuum thrust");
                    if(e.propellants.Any(p=>p.resourceDef==null||p.resourceDef.density<=0||!BurnMath.Finite(p.ratio)||p.ratio<=0||p.ignoreForIsp))return FuelForecastResult.Unknown("Massless or special propellant needs an adapter");
                    double mixture=e.propellants.Sum(p=>p.ratio*p.resourceDef.density),mf=force/(isp*BurnMath.G0);
                    if(!BurnMath.Finite(mixture)||mixture<=0)return FuelForecastResult.Unknown("Invalid engine mixture");
                    foreach(var p in e.propellants){
                        IEnumerable<Part> pool;var mode=p.GetFlowMode();
                        if(mode==ResourceFlowMode.NO_FLOW)pool=new[]{e.part};
                        else if(mode==ResourceFlowMode.ALL_VESSEL||mode==ResourceFlowMode.ALL_VESSEL_BALANCE)pool=v.parts;
                        else if(mode==ResourceFlowMode.STACK_PRIORITY_SEARCH||mode==ResourceFlowMode.STAGE_PRIORITY_FLOW||mode==ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE)pool=e.part.crossfeedPartSet.GetParts();
                        else return FuelForecastResult.Unknown("Unsupported propellant flow mode");
                        var ids=pool.Where(part=>part.Resources.Contains(p.name)&&part.Resources[p.name].flowState).Select(part=>part.flightID).Distinct().OrderBy(id=>id).ToArray();
                        if(pools.ContainsKey(p.name)&&!pools[p.name].SequenceEqual(ids))return FuelForecastResult.Unknown("Selected engines draw from different tank networks");
                        pools[p.name]=ids;densities[p.name]=p.resourceDef.density;
                        if(!rates.ContainsKey(p.name))rates[p.name]=0;rates[p.name]+=mf*p.ratio/mixture;
                    }
                    thrust+=force;flow+=mf;
                }
                var fuels=rates.Select(pair=>{var resources=v.parts.Where(p=>pools[pair.Key].Contains(p.flightID)).Select(p=>p.Resources[pair.Key]).ToArray();return new ForecastFuel(pair.Key,resources.Sum(r=>r.amount),resources.Sum(r=>r.maxAmount),pair.Value,densities[pair.Key]);}).ToArray();
                return FuelForecast.Evaluate(v.GetTotalMass(),thrust,flow,fuels,dv,m.landingReserveDV,m.burnMargin,m.fuelWarning);
            }catch(Exception e){return FuelForecastResult.Unknown("Forecast unavailable: "+MissionControl.Text(e.Message));}
        }
        public static FuelForecastResult Queue(ModuleRegionalAutonomy m){
            try {
                if(m.soiMission&&MissionQueue.Read(m.queuedNodes64).Any(n=>MissionControl.ReadOrbit(n.Target).referenceBody!=m.vessel.mainBody))return Check(m,m.nodeRequested?MissionControl.PreviewRemaining(m).magnitude+MissionQueue.Read(m.queuedNodes64).Sum(n=>n.DV):MissionQueue.Read(m.queuedNodes64).Sum(n=>n.DV),"SOI plan nominal budget; recheck on arrival");
                double dv=m.nodeRequested?MissionControl.PreviewRemaining(m).magnitude:0;
                dv+=MissionQueue.Read(m.queuedNodes64).Sum(n=>n.DV);
                return Check(m,dv,"Saved queue remaining");
            }catch(Exception e){var r=FuelForecastResult.Unknown("Saved queue cannot be read: "+MissionControl.Text(e.Message));m.fuelForecast=r;m.forecastSource="Saved queue";m.forecastUT=Planetarium.GetUniversalTime();return r;}
        }
        public static FuelForecastResult MapPlan(ModuleRegionalAutonomy m){
            var v=m.vessel;
            if(v!=FlightGlobals.ActiveVessel||v.patchedConicSolver==null)throw new InvalidOperationException("Focus this craft and create a map plan first");
            v.patchedConicSolver.UpdateFlightPlan();var nodes=v.patchedConicSolver.maneuverNodes.ToArray();
            if(nodes.Length==0)throw new InvalidOperationException("Create a map maneuver first");
            if(nodes.Length>32||nodes.Any(n=>n.UT<=Planetarium.GetUniversalTime()||n.patch==null||n.nextPatch==null||n.patch.referenceBody!=v.mainBody||n.nextPatch.referenceBody!=v.mainBody))throw new InvalidOperationException("Use future nodes within the current body's reference frame");
            return Check(m,nodes.Sum(n=>n.GetBurnVector(n.patch).magnitude),"Current map plan");
        }
        public static void Monitor(ModuleRegionalAutonomy m){
            if(!m.forecastQueue||(!m.nodeRequested&&String.IsNullOrEmpty(m.queuedNodes64))){m.forecastEventState="";return;}
            if(Time.realtimeSinceStartup<m.nextFuelForecast)return;m.nextFuelForecast=Time.realtimeSinceStartup+2;
            var r=Queue(m);string state=r.State.ToString();
            if(state!=m.forecastEventState){
                bool clear=r.WithinReserve&&m.forecastEventState!="";
                if(!r.WithinReserve||clear)FleetEventDirector.Emit(m,clear?"FUEL_FORECAST_CLEAR":"FUEL_FORECAST_WARNING",state+": "+r.Reason);
                m.forecastEventState=state;
            }
        }
    }
}
