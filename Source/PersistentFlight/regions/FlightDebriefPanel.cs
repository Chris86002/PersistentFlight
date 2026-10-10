using System;
using System.Linq;
using System.IO;
using System.Text;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        bool showDebriefs;Vector2 debriefScroll;int debriefPage;
        static string Metric(double n,string unit){return Double.IsNaN(n)?"Unavailable":n.ToString("F2",C)+unit;}
        void DrawDebriefs(){
            GUILayout.Label("Flight debrief â€” saved burn history");
            var s=PersistentFlightScenario.Instance;if(s==null){GUILayout.Label("Waiting for save state");return;}
            if(s.DebriefError!="")GUILayout.Label(s.DebriefError);
            GUILayout.Label("Live: estimated thrust impulse and net vessel fuel changes.\nBackground: modelled delta-v and recorded fuel withdrawals.\nNegative resource use means a net gain.",new GUIStyle(GUI.skin.label){wordWrap=true});
            if(GUILayout.Button("Export flight debrief CSV"))Try(()=>{GUIUtility.systemCopyBuffer=ExportFlightDebriefs();},"Debrief exported; path copied.");
            var rows=s.Debriefs.Snapshot().Reverse().ToArray();int pages=Math.Max(1,(rows.Length+9)/10);debriefPage=Math.Min(debriefPage,pages-1);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Previous"))debriefPage=Math.Max(0,debriefPage-1);GUILayout.Label((debriefPage+1)+" / "+pages+" â€” "+rows.Length+" / 1,000 reports");if(GUILayout.Button("Next"))debriefPage=Math.Min(pages-1,debriefPage+1);GUILayout.EndHorizontal();
            foreach(var r in rows.Skip(debriefPage*10).Take(10)){
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label(r.Craft+" / "+r.Body+" / "+r.Outcome);
                if(r.Mode==DebriefMode.Landing){
                    GUILayout.Label("Landing / "+r.Contact+" / "+r.LandingTarget);
                    GUILayout.Label("Contact "+Metric(r.Latitude,"°")+", "+Metric(r.Longitude,"°")+"   Target distance "+Metric(r.TargetDistance," m"));
                    GUILayout.Label("Last pre-contact speed "+Metric(r.TouchdownSpeed," m/s")+"   Vertical "+Metric(r.VerticalSpeed," m/s")+"   Horizontal "+Metric(r.HorizontalSpeed," m/s"));
                    GUILayout.Label("Remaining LF "+Metric(r.RemainingLF,"")+"   Ox "+Metric(r.RemainingOx,"")+"   EC "+Metric(r.RemainingEC,"")+"   Lost parts "+r.LostParts+" / "+r.InitialParts);
                    if(r.LandingNote!="")GUILayout.Label(r.LandingNote);
                    if(r.Incomplete)GUILayout.Label("Partial approach observation; contact speed or outcome may be incomplete.");
                    GUILayout.EndVertical();continue;
                }
                GUILayout.Label((r.Mode==DebriefMode.LiveManeuver?"Live maneuver":"Background burn")+" â€” node UT "+r.NodeUT.ToString("F1",C));
                GUILayout.Label("Planned "+Metric(r.PlannedDV," m/s")+"   "+(r.Mode==DebriefMode.LiveManeuver?"Estimated impulse ":"Modelled impulse ")+Metric(r.ImpulseDV," m/s"));
                GUILayout.Label("Final residual "+Metric(r.ResidualDV," m/s")+"   Powered time "+Metric(r.PoweredSeconds," s"));
                GUILayout.Label(r.ResourcesKnown?"LF "+Metric(r.LiquidFuel,"")+"   Ox "+Metric(r.Oxidizer,"")+"   EC "+Metric(r.ElectricCharge,"")+" units":"Resource use unavailable (no measured burn window)");
                if(r.Incomplete)GUILayout.Label("Partial measurement: save reload or an observation gap occurred.");
                if(!r.Started)GUILayout.Label(r.Finished?"No powered burn window was observed.":"Engine burn has not started.");
                GUILayout.EndVertical();
            }
            if(rows.Length==0)GUILayout.Label("Queue a maneuver or background burn to start recording. The craft recording toggle controls the separate sample log.");
        }
        public static string ExportFlightDebriefs(){
            var s=PersistentFlightScenario.Instance;if(s==null)throw new InvalidOperationException("Save state unavailable");
            string folder=Path.Combine(KSPUtil.ApplicationRootPath,"PersistentFlight/FlightLogs/Debriefs");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"debrief-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".csv");
            var csv=new StringBuilder("report_id,origin_branch,export_branch,vessel_id,cpu_id,craft,body,mode,outcome,finished,queued_ut,node_ut,burn_start_ut,end_ut,planned_dv,impulse_dv,impulse_basis,final_residual_dv,powered_seconds,lf_units,oxidizer_units,ec_units,resource_basis,incomplete,contact,latitude,longitude,pre_contact_speed,vertical_speed,horizontal_speed,target,target_distance_m,remaining_lf,remaining_oxidizer,remaining_ec,initial_parts,lost_parts,landing_note\n");
            foreach(var r in s.Debriefs.Snapshot())csv.AppendLine(String.Join(",",new[]{r.Id.ToString(),r.Branch.ToString(),s.Debriefs.Branch.ToString(),r.Vessel.ToString(),r.Cpu.ToString(C),FleetSample.Quote(r.Craft),FleetSample.Quote(r.Body),r.Mode.ToString(),FleetSample.Quote(r.Outcome),r.Finished.ToString(),Number(r.QueuedUT),Number(r.NodeUT),r.Started?Number(r.StartUT):"",r.Finished?Number(r.EndUT):"",Number(r.PlannedDV),Number(r.ImpulseDV),r.Mode==DebriefMode.Landing?"not_applicable":r.Mode==DebriefMode.LiveManeuver?"estimated_thrust_integral":"analytic_model",Number(r.ResidualDV),Number(r.PoweredSeconds),r.ResourcesKnown?Number(r.LiquidFuel):"",r.ResourcesKnown?Number(r.Oxidizer):"",r.ResourcesKnown?Number(r.ElectricCharge):"",r.Mode==DebriefMode.Landing?"remaining_at_contact":r.Mode==DebriefMode.LiveManeuver?"net_vessel_change":"recorded_model_withdrawal",r.Incomplete.ToString(),FleetSample.Quote(r.Contact),Number(r.Latitude),Number(r.Longitude),Number(r.TouchdownSpeed),Number(r.VerticalSpeed),Number(r.HorizontalSpeed),FleetSample.Quote(r.LandingTarget),Number(r.TargetDistance),r.Mode==DebriefMode.Landing?Number(r.RemainingLF):"",r.Mode==DebriefMode.Landing?Number(r.RemainingOx):"",r.Mode==DebriefMode.Landing?Number(r.RemainingEC):"",r.Mode==DebriefMode.Landing?r.InitialParts.ToString(C):"",r.Mode==DebriefMode.Landing?r.LostParts.ToString(C):"",FleetSample.Quote(r.LandingNote)}));
            File.WriteAllText(path,csv.ToString(),new UTF8Encoding(false));return path;
        }
        static string Number(double n){return Double.IsNaN(n)?"":n.ToString("R",C);}
    }
}
