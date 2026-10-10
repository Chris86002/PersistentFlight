using System;
using System.Collections.Generic;
namespace PersistentFlight.Regions {
    // Resource thresholds are fractions, never assumptions about a particular
    // tank size. Missing resources have no capacity and do not generate alerts.
    public static class FleetSafety {
        public static string Evaluate(double fuel,double fuelCapacity,double power,double powerCapacity,bool stalled,double fuelWarning,double powerWarning){
            var alerts=new List<string>();
            if(fuelCapacity>0&&fuel/fuelCapacity<=fuelWarning)alerts.Add("Low propellant reserve");
            if(powerCapacity>0&&power/powerCapacity<=powerWarning)alerts.Add("Low electric power");
            if(stalled)alerts.Add("Script heartbeat lost");return String.Join("; ",alerts.ToArray());
        }
        public static bool Critical(double fuel,double capacity,double power,double powerCapacity,bool stalled){return (capacity>0&&fuel/capacity<=.01)||(powerCapacity>0&&power/powerCapacity<=.01)||stalled;}
    }
}
