using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed class ReadinessRow {
        public string Item,Detail;public bool Attention;
        public ReadinessRow(string item,string detail,bool attention){Item=item;Detail=detail;Attention=attention;}
    }
    // Inspection only: never starts a pilot, activates an engine, captures a
    // map plan, or invokes the guidance methods that cancel invalid requests.
    public static class FlightReadiness {
        public static ReadinessRow[] Inspect(Vessel v){
            var rows=new List<ReadinessRow>();
            if(v==null)return new[]{new ReadinessRow("Craft","Select a craft",true)};
            bool live=v.loaded&&!v.packed;
            rows.Add(new ReadinessRow("Flight state",live?"Live flight":"Saved craft: visit it to check live guidance",!live));
            rows.Add(new ReadinessRow("Time",TimeWarp.CurrentRate==1?"Normal time":"Return to normal time for live maneuvers",TimeWarp.CurrentRate!=1));
            double cap,power=MissionControl.Resource(v,"ElectricCharge",out cap);
            bool valid=BurnMath.Finite(power)&&BurnMath.Finite(cap)&&power>=0&&cap>0;
            rows.Add(new ReadinessRow("Power",!valid?"No usable electric-charge storage":power.ToString("F1")+" / "+cap.ToString("F1")+" (current amount; endurance unknown)",!valid||power<=0||power/cap<=.1));
            var service=PersistentFlightScenario.Instance;var job=service==null?null:service.Find(v.id);
            rows.Add(new ReadinessRow("Background burn",job!=null&&!job.Finished?"Owns this craft: finish or cancel it before a live maneuver":"No unfinished background burn",job!=null&&!job.Finished));
            if(!live)return rows.ToArray();
            var modules=MissionControl.Modules(v);
            rows.Add(new ReadinessRow("Automatic flight",modules.Any(m=>m.automatic)?"Enabled":"Enable automatic flight on a kOS CPU",!modules.Any(m=>m.automatic)));
            if(modules.Length==0){rows.Add(new ReadinessRow("Pilot","No PersistentFlight CPU installed",true));return rows.ToArray();}
            foreach(var m in modules){
                string label="CPU "+m.part.flightID;
                rows.Add(new ReadinessRow(label+" pilot",m.PilotOnline?"Mission pilot responding":"Run pf_mission_pilot.ks on an idle CPU; wait for its heartbeat",!m.PilotOnline));
                rows.Add(new ReadinessRow(label+" ownership",m.missionHold?"Mission held: "+m.nodeStatus:m.ActiveRoverRoute?"Rover route controls this craft":"Available for maneuver guidance",m.missionHold||m.ActiveRoverRoute));
                try{
                    var queue=MissionQueue.Read(m.queuedNodes64);double dv=queue.Sum(n=>n.DV);
                    if(m.nodeRequested){dv+=MissionControl.PreviewRemaining(m).magnitude;rows.Add(new ReadinessRow(label+" current burn",m.nodeUT<=Planetarium.GetUniversalTime()?"Node time has passed; check burn status":"Saved target matches the current body",m.nodeUT<=Planetarium.GetUniversalTime()));}
                    bool deferred=queue.Any(n=>MissionControl.ReadOrbit(n.Target).referenceBody!=v.mainBody);
                    bool overdue=queue.Any(n=>n.UT<=Planetarium.GetUniversalTime());
                    rows.Add(new ReadinessRow(label+" plan",(queue.Count+(m.nodeRequested?1:0))+" saved maneuvers"+(overdue?"; overdue node: revise or cancel the plan":deferred?"; later-body targets need arrival checks":""),overdue||deferred&&!m.soiMission));
                    if(!m.nodeRequested&&queue.Count==0){
                        var nodes=v==FlightGlobals.ActiveVessel&&v.patchedConicSolver!=null?v.patchedConicSolver.maneuverNodes.ToArray():null;
                        if(nodes!=null&&nodes.Length>0){dv=nodes.Sum(n=>n.patch==null?Double.NaN:n.GetBurnVector(n.patch).magnitude);rows.Add(new ReadinessRow(label+" map",nodes.Length+" map nodes inspected; not queued",nodes.Any(n=>n.UT<=Planetarium.GetUniversalTime()+5)));}
                        else rows.Add(new ReadinessRow(label+" map","No saved plan; focus this craft to inspect map nodes",false));
                    }
                    var f=FleetFuelForecast.Evaluate(m,dv);
                    rows.Add(new ReadinessRow(label+" engines / fuel",f.State+": "+f.Reason+(f.Known?"; "+f.AvailableDV.ToString("F0")+" m/s available, "+f.BudgetDV.ToString("F0")+" budget":"")+(deferred?" (nominal SOI budget; recheck on arrival)":""),!f.WithinReserve));
                }catch(Exception e){rows.Add(new ReadinessRow(label+" plan","Could not validate: "+MissionControl.Text(e.Message),true));}
            }
            rows.Add(new ReadinessRow("Scope","Maneuver checks only. Terrain, landing control and long-term power generation need separate checks.",false));
            return rows.ToArray();
        }
    }
}
