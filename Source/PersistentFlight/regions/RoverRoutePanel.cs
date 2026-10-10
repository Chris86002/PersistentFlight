using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        bool showRoutes;Vector2 routeScroll;uint routeCpu,routeDraftCpu;
        readonly List<RoverPoint> routeDraft=new List<RoverPoint>();
        string routeLat="0",routeLon="0",routeCruise="3",routeRadius="5",routeSlope="15",routeReserve="25",routeStuck="60";bool routeAutoReturn=true;
        void DrawRoverRoutes(){
            GUILayout.Label("Rover exploration routes");
            var cpus=FlightGlobals.Vessels.Where(craft=>craft!=null&&craft.loaded&&craft.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>()).Any(w=>w.wheelType==WheelType.MOTORIZED)).SelectMany(craft=>MissionControl.Modules(craft)).ToArray();
            if(cpus.Length==0){GUILayout.Label("Load a rover with stock motorized wheels and a kOS CPU.");return;}
            if(!cpus.Any(unit=>unit.part.flightID==routeCpu))routeCpu=cpus[0].part.flightID;
            CpuPicker("Rover CPU",cpus,ref routeCpu);var m=cpus.First(x=>x.part.flightID==routeCpu);var v=m.vessel;
            if(routeDraftCpu!=routeCpu){routeDraftCpu=routeCpu;routeDraft.Clear();routeLat=v.latitude.ToString("R",C);routeLon=v.longitude.ToString("R",C);}
            var r=RoverRouteController.Get(m);
            GUILayout.Label(RoverRouteController.Status(m));
            GUILayout.Label(Time.realtimeSinceStartup-m.routeHeartbeat<3?"Rover program is responding":"Run pf_rover_route.ks on this idle CPU to drive the saved route.");
            if(m.roverRouteHeld){GUILayout.Label("Damaged checkpoint held. Restore a complete save to recover it.");if(GUILayout.Button("Clear damaged route and discard its checkpoint"))Try(()=>RoverRouteController.Clear(m),"Damaged route explicitly cleared.");return;}
            if(r!=null){
                GUILayout.Label("Waypoint "+r.Index+" / "+r.Request.Waypoints.Length+" | "+r.Mode+" | "+r.Hold);
                GUILayout.Label("Travelled "+r.DistanceDriven.ToString("F1",C)+" m | measured net drain "+r.EnergyPerMetre.ToString("F3",C)+" EC/m");
                if(m.routeDecision!=null)GUILayout.Label("Target "+m.routeDecision.Distance.ToString("F1",C)+" m | look-ahead slope "+m.routeDecision.Slope.ToString("F1",C)+"° | retraced return "+m.routeDecision.ReturnDistance.ToString("F1",C)+" m");
                GUILayout.BeginHorizontal();
                if(r.Active){if(GUILayout.Button("Pause")){m.roverHold=true;}if(GUILayout.Button("Resume route"))Try(()=>RoverRouteController.Resume(m),"Route resumed; terrain and power checks still apply.");}
                if(r.Mode!=RoverMode.Home&&r.Mode!=RoverMode.Cancelled&&GUILayout.Button("Return home"))Try(()=>RoverRouteController.ReturnHome(m),"Returning along recorded travel points.");
                if(r.Active&&GUILayout.Button("Cancel route"))Try(()=>RoverRouteController.Cancel(m),"Route cancelled and rover brakes applied.");GUILayout.EndHorizontal();
                if(!r.Active&&GUILayout.Button("Clear route and restore previous boot program"))Try(()=>RoverRouteController.Clear(m),"Route cleared; previous boot selection restored.");
            }
            GUILayout.Label("New route: add decimal latitude/longitude stops on "+v.mainBody.bodyName+".");
            routeLat=Field("Latitude",routeLat);routeLon=Field("Longitude",routeLon);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Add waypoint"))Try(()=>AddRoutePoint(new RoverPoint(Double.Parse(routeLat,C),Double.Parse(routeLon,C))),"Waypoint added to draft.");
            if(GUILayout.Button("Add 100 m east"))Try(()=>AddRoutePoint((routeDraft.Count==0?new RoverPoint(v.latitude,v.longitude):routeDraft.Last()).Offset(90,100,v.mainBody.Radius)),"Waypoint added east of the last point.");GUILayout.EndHorizontal();
            foreach(var item in routeDraft.Select((p,i)=>new{Point=p,Index=i}).ToArray()){
                GUILayout.BeginHorizontal();GUILayout.Label((item.Index+1)+": "+item.Point.Latitude.ToString("F6",C)+", "+item.Point.Longitude.ToString("F6",C));if(GUILayout.Button("Remove",GUILayout.Width(70)))routeDraft.RemoveAt(item.Index);GUILayout.EndHorizontal();
            }
            routeCruise=Field("Cruise speed (m/s)",routeCruise);routeRadius=Field("Arrival radius (m)",routeRadius);routeSlope=Field("Maximum terrain slope (°)",routeSlope);routeReserve=Field("Battery reserve (%)",routeReserve);routeStuck=Field("Stuck timeout (game s)",routeStuck);
            routeAutoReturn=GUILayout.Toggle(routeAutoReturn,"Return along recorded travel points at the battery reserve");
            GUILayout.Label("Terrain checks sample heights ahead; they do not detect every rock or building. Return energy is estimated from observed net drain.");
            if(GUILayout.Button("Save route, paused, and enable recovery boot"))Try(()=>RoverRouteController.Configure(m,new RoverRouteRequest(Guid.NewGuid(),v.mainBody.bodyName,new RoverPoint(v.latitude,v.longitude),routeDraft,Double.Parse(routeCruise,C),Double.Parse(routeRadius,C),Double.Parse(routeSlope,C),Double.Parse(routeReserve,C)/100,Double.Parse(routeStuck,C),routeAutoReturn)),"Route saved paused. Run pf_rover_route.ks on this CPU, then Resume route.");
        }
        void AddRoutePoint(RoverPoint p){if(routeDraft.Count>=64)throw new InvalidOperationException("Use at most 64 waypoints");routeDraft.Add(p);}
    }
}
