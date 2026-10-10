using System;
using System.Linq;
using System.Globalization;
using UnityEngine;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        string remoteDelay="120",remoteDV="25";int remoteDirection;
        static readonly string[] Directions={"PROGRADE","RETROGRADE","NORMAL","ANTINORMAL","RADIALOUT","RADIALIN"};
        void ExtendedActions(Vessel v,ModuleRegionalAutonomy[] modules){
            var m=modules.FirstOrDefault(x=>x.QueueCount>0)??MissionControl.Pilot(v)??modules.First();bool enabled=GUI.enabled;
            GUILayout.Label("Mission queue: "+modules.Sum(x=>x.QueueCount)+" maneuvers");
            GUILayout.BeginHorizontal();GUI.enabled=v==FlightGlobals.ActiveVessel&&TimeWarp.CurrentRate==1&&MissionControl.Pilot(v)!=null&&!modules.Any(x=>x.QueueCount>0);
            if(GUILayout.Button("Queue entire flight plan"))Try(()=>MissionQueue.QueuePlan(v,MissionControl.Pilot(v)),"Flight plan captured; recovery boot enabled on the pilot CPU.");
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Queue flight plan across SOI changes"))Try(()=>MissionQueue.QueuePlan(v,MissionControl.Pilot(v),true),"SOI plan saved with arrival checks and recovery.");
            GUI.enabled=enabled;
            if(m.soiMission){GUILayout.Label("SOI plan: "+m.soiStatus);m.soiTakeover=GUILayout.Toggle(m.soiTakeover,"Visit this craft automatically near its arrival burn");}
            if(!m.nodeRequested&&m.QueueCount>0&&GUILayout.Button("Cancel waiting plan"))m.CancelNode("Cancelled waiting plan");
            GUILayout.BeginHorizontal();if(GUILayout.Button(m.missionHold?"Resume mission":"Pause mission"))Try(()=>{
                if(m.missionHold){
                    if(m.nodeRequested&&m.nodeUT<Planetarium.GetUniversalTime()+5)throw new InvalidOperationException("This maneuver is overdue. Cancel it and create a new flight plan.");
                    foreach(var x in modules){MissionIntegrity.Release(x);x.roverHold=false;x.ScriptPulse();}
                }else foreach(var x in modules)x.HoldMission("Paused by player");
            },"Mission hold changed.");GUILayout.EndHorizontal();
            foreach(var x in modules.Where(x=>!String.IsNullOrEmpty(x.alert)))GUILayout.Label("Alert: "+x.alert);
            if(GUILayout.Button("Acknowledge current alerts"))foreach(var x in modules)x.acknowledgedAlert=x.alert;
            bool abort=GUILayout.Toggle(m.abortOnCritical,"Stop engines and pause on critical reserve or heartbeat loss");foreach(var x in modules)x.abortOnCritical=abort;
            GUILayout.Label("Propellant warning: "+(m.fuelWarning*100).ToString("F0")+"%    Power warning: "+(m.powerWarning*100).ToString("F0")+"%");
            double fw=GUILayout.HorizontalSlider((float)m.fuelWarning,.01f,.5f),pw=GUILayout.HorizontalSlider((float)m.powerWarning,.01f,.5f);
            foreach(var x in modules){x.fuelWarning=fw;x.powerWarning=pw;}
            FuelForecastControls(m);
            bool guard=GUILayout.Toggle(m.guardWarp,"Guard time warp while this mission is armed");foreach(var x in modules)x.guardWarp=guard;
            foreach(var x in modules)x.landingReports=GUILayout.Toggle(x.landingReports,"Record approach and touchdown report");
            bool landing=GUILayout.Toggle(m.landingTakeover,"Switch to this craft for native landing contact below 6 km");foreach(var x in modules)x.landingTakeover=landing;
            GUILayout.Label("Landing handoff uses KSP's surface, water and vessel collisions. Guidance must target a suitable pad or deck.");
            if(m.resumePilot){GUILayout.Label("Recovery boot enabled; saved queue restarts when this CPU loads.");
                if(GUILayout.Button("Restore previous CPU boot program"))Try(()=>{foreach(var x in modules)MissionQueue.RestoreBoot(x);},"Previous boot selection restored.");}
            GUILayout.Label("Distant mission: analytic vacuum burn (runs after this craft unloads)");
            GUILayout.BeginHorizontal();GUILayout.Label("Start in seconds",GUILayout.Width(105));remoteDelay=GUILayout.TextField(remoteDelay,12);GUILayout.Label("Delta-v",GUILayout.Width(55));remoteDV=GUILayout.TextField(remoteDV,12);GUILayout.EndHorizontal();
            remoteDirection=GUILayout.SelectionGrid(remoteDirection,Directions,3);
            GUI.enabled=v==FlightGlobals.ActiveVessel&&TimeWarp.CurrentRate==1&&!modules.Any(x=>x.QueueCount>0);
            if(GUILayout.Button("Queue distant burn and release live physics"))Try(()=>QueueDistant(v,modules),"Burn saved. Visit another distant craft to let it unload and execute.");
            GUI.enabled=enabled;
        }
        static void QueueDistant(Vessel v,ModuleRegionalAutonomy[] modules,string delay,string dv,string direction){
            double seconds,delta;
            if(!Double.TryParse(delay,NumberStyles.Float,CultureInfo.InvariantCulture,out seconds)||!PersistentFlight.BurnMath.Finite(seconds)||seconds<10 ||
                !Double.TryParse(dv,NumberStyles.Float,CultureInfo.InvariantCulture,out delta))throw new ArgumentException("Enter a delay of at least 10 seconds and a valid delta-v");
            var service=PersistentFlight.PersistentFlightScenario.Instance;if(service==null)throw new InvalidOperationException("Background service unavailable");
            service.Queue(v,Planetarium.GetUniversalTime()+seconds,delta,direction);
            foreach(var x in modules){x.automatic=false;x.monitorMission=false;x.guardWarp=false;x.warpReady=false;}
            foreach(var keep in v.parts.SelectMany(p=>p.Modules.OfType<ModulePersistentFlight>()))keep.armed=false;
        }
        void QueueDistant(Vessel v,ModuleRegionalAutonomy[] modules){QueueDistant(v,modules,remoteDelay,remoteDV,Directions[remoteDirection]);}
        static bool InFleet(Vessel v){
            if(v==null)return false;if(RegionalResidency.Armed(v))return true;
            var service=PersistentFlight.PersistentFlightScenario.Instance;var job=service==null?null:service.Find(v.id);return job!=null;
        }
    }
}
