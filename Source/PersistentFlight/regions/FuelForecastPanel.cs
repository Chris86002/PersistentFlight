using System;
using System.Globalization;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        string reserveDraft="0";uint reserveCpu;
        void FuelForecastControls(ModuleRegionalAutonomy m){
            GUILayout.Label("Fuel forecast — advisory vacuum estimate");
            if(reserveCpu!=m.part.flightID){reserveCpu=m.part.flightID;reserveDraft=m.landingReserveDV.ToString("F0",CultureInfo.InvariantCulture);}
            GUILayout.BeginHorizontal();GUILayout.Label("Keep delta-v (m/s)",GUILayout.Width(130));reserveDraft=GUILayout.TextField(reserveDraft,12);
            if(GUILayout.Button("Set reserve"))Try(()=>{
                double value;if(!Double.TryParse(reserveDraft,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!BurnMath.Finite(value)||value<0||value>10000)throw new ArgumentException("Reserve must be 0 to 10000 m/s");m.landingReserveDV=value;
                if(m.nodeRequested||!String.IsNullOrEmpty(m.queuedNodes64))FleetFuelForecast.Queue(m);
            },"Vacuum reserve saved.");GUILayout.EndHorizontal();
            GUILayout.Label("Burn margin: "+(m.burnMargin*100).ToString("F0")+"% (extra planned delta-v)");
            m.burnMargin=Math.Round(GUILayout.HorizontalSlider((float)m.burnMargin,0,.5f)*100)/100;
            m.forecastQueue=GUILayout.Toggle(m.forecastQueue,"Refresh saved queue forecast automatically");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Check map plan"))Try(()=>{FleetFuelForecast.MapPlan(m);},"Map plan checked.");
            if(GUILayout.Button("Check saved queue"))Try(()=>{FleetFuelForecast.Queue(m);},"Saved queue checked.");
            GUILayout.EndHorizontal();
            var r=m.fuelForecast;if(r==null){GUILayout.Label("No fuel estimate yet. Reserve also uses the propellant warning fraction above.");return;}
            GUILayout.Label(m.forecastSource+" • age "+Math.Max(0,Planetarium.GetUniversalTime()-m.forecastUT).ToString("F0")+" s");
            GUILayout.Label(r.State+": "+r.Reason);
            if(r.Known){
                GUILayout.Label("Plan "+r.PlanDV.ToString("F1")+" m/s; with margin "+r.BudgetDV.ToString("F1")+" m/s");
                GUILayout.Label("Available "+r.AvailableDV.ToString("F1")+" m/s; after plan "+r.RemainingDV.ToString("F1")+" m/s");
                GUILayout.Label("Estimated full-throttle time "+r.Duration.ToString("F1")+" s; fuel "+r.FuelMass.ToString("F3")+" t");
                GUILayout.Label("Lowest connected tank fraction after plan "+(r.MinimumFraction*100).ToString("F1")+"%; limiting fuel "+r.LimitingFuel);
            }
            GUILayout.Label("Current engine group, no staging. Reserve is vacuum delta-v; landing losses and atmospheric Isp need extra allowance. Estimates can become stale after changes.");
        }
    }
}
