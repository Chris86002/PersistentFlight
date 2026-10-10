using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
namespace PersistentFlight.Regions
{
    public sealed class FleetSample
    {
        public Guid Id;public string Name,Body,Physics,Phase,NodeState;public bool Focused,Recording;
        public double UT,Altitude,Speed,Apoapsis,Periapsis,Throttle,Fuel,FuelCapacity,Power,PowerCapacity,Oxidizer,RemainingDV;
        public int Steps;
        public static FleetSample Capture(Vessel v) {
            var modules=MissionControl.Modules(v);var runner=RegionFlightRunner.Instance;
            var owned=runner==null?null:runner.Regions.FirstOrDefault(r=>r.Vessel==v);
            var phases=modules.Select(m=>m.missionPhase).Where(s=>s!="Script phase not reported").Distinct().ToArray();
            var pending=modules.FirstOrDefault(m=>m.nodeRequested);
            var sample=new FleetSample {Id=v.id,Name=v.vesselName,Body=v.mainBody.bodyName,UT=Planetarium.GetUniversalTime(),
                Focused=v==FlightGlobals.ActiveVessel,Recording=modules.Any(m=>m.recordFlight),Altitude=v.altitude,Speed=v.obt_speed,
                Apoapsis=v.orbit.ApA,Periapsis=v.orbit.PeA,Throttle=v.loaded?v.ctrlState.mainThrottle:0,
                Steps=owned==null?0:owned.Steps,Phase=phases.Length==0?"Script phase not reported":String.Join(" / ",phases),
                Physics=!v.loaded?"Unloaded / orbit propagation":v.packed?"On rails":owned!=null?"Background physics":v==FlightGlobals.ActiveVessel?"Focused physics":"Stock physics",
                NodeState=pending==null?modules.Select(m=>m.nodeStatus).FirstOrDefault(s=>s!="No maneuver queued")??"No maneuver queued":pending.PilotOnline?pending.nodeStatus:"Pilot offline; request saved"};
            var service=PersistentFlight.PersistentFlightScenario.Instance;var job=service==null?null:service.Find(v.id);
            if(job!=null&&!job.Finished)sample.NodeState="Distant burn: "+job.Status+" ("+job.DeliveredDV.ToString("F1")+" / "+job.TargetDV.ToString("F1")+" m/s)";
            sample.Fuel=MissionControl.Resource(v,"LiquidFuel",out sample.FuelCapacity);
            sample.Power=MissionControl.Resource(v,"ElectricCharge",out sample.PowerCapacity);
            double unused;sample.Oxidizer=MissionControl.Resource(v,"Oxidizer",out unused);
            // Sampling is observational. It must not cancel or mutate a queued burn.
            if(pending!=null)try{sample.RemainingDV=MissionControl.PreviewRemaining(pending).magnitude;}catch{sample.RemainingDV=Double.NaN;}
            return sample;
        }
        static string Number(double n){return n.ToString("R",CultureInfo.InvariantCulture);}
        public static string Quote(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
        public string Csv(){return String.Join(",",new[]{DateTime.UtcNow.ToString("o"),Number(UT),Id.ToString(),Quote(Name),Quote(Body),Focused?"1":"0",Quote(Physics),Quote(Phase),Number(Altitude),Number(Speed),Number(Apoapsis),Number(Periapsis),Number(Throttle),Number(Fuel),Number(Oxidizer),Number(Power),Steps.ToString(CultureInfo.InvariantCulture),Quote(NodeState),Number(RemainingDV)});}
        public const string Header="utc,ut,vessel_id,vessel_name,body,focused,physics,phase,altitude_m,orbit_speed_m_s,apoapsis_m,periapsis_m,main_throttle,liquid_fuel,oxidizer,electric_charge,regional_steps,node_state,node_remaining_dv_m_s";
    }
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FleetRecorder:MonoBehaviour
    {
        public static FleetRecorder Instance;
        public string LastError="",LogDirectory="";
        public readonly Dictionary<Guid,System.Collections.Generic.List<FleetSample>> History=new Dictionary<Guid,System.Collections.Generic.List<FleetSample>>();
        public readonly Dictionary<Guid,FleetSample> Current=new Dictionary<Guid,FleetSample>();
        StreamWriter writer;float nextCapture,nextFlush;
        readonly HashSet<Guid> present=new HashSet<Guid>();readonly List<Guid> absent=new List<Guid>();
        public void Awake(){Instance=this;}
        public void Update(){
            if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||Time.realtimeSinceStartup<nextCapture)return;
            long stamp=FleetPerformance.Start();try{CaptureFleet();}finally{FleetPerformance.Capture.Record(stamp);}
        }
        void CaptureFleet(){
            nextCapture=Time.realtimeSinceStartup+1;
            var fleet=FlightGlobals.Vessels.Where(v=>v!=null&&(RegionalResidency.Armed(v)||(PersistentFlight.PersistentFlightScenario.Instance!=null&&PersistentFlight.PersistentFlightScenario.Instance.Find(v.id)!=null))).ToArray();
            present.Clear();absent.Clear();foreach(var v in fleet)present.Add(v.id);
            foreach(var id in Current.Keys)if(!present.Contains(id))absent.Add(id);
            foreach(var id in absent)Current.Remove(id);
            bool any=false;
            foreach(var v in fleet)try {
                var sample=FleetSample.Capture(v);Current[v.id]=sample;
                if(!sample.Recording)continue;any=true;
                if(writer==null) {
                    if(!String.IsNullOrEmpty(LastError))continue;
                    LogDirectory=Path.Combine(KSPUtil.ApplicationRootPath,"PersistentFlight/FlightLogs/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
                    Directory.CreateDirectory(LogDirectory);writer=new StreamWriter(Path.Combine(LogDirectory,"fleet.csv"),false,new UTF8Encoding(false));writer.WriteLine(FleetSample.Header);
                }
                writer.WriteLine(sample.Csv());
                System.Collections.Generic.List<FleetSample> list;if(!History.TryGetValue(v.id,out list)){list=new System.Collections.Generic.List<FleetSample>();History.Add(v.id,list);}
                list.Add(sample);if(list.Count>1200)list.RemoveAt(0);
            } catch(Exception e){LastError="Recording failed: "+e.Message;Close();Debug.LogError("[PF-RECORDER] "+e);}
            if(writer!=null&&!any)Close();
            else if(writer!=null&&Time.realtimeSinceStartup>=nextFlush)try{writer.Flush();nextFlush=Time.realtimeSinceStartup+5;}catch(Exception e){LastError="Recording failed: "+e.Message;Close();Debug.LogError("[PF-RECORDER] "+e);}
        }
        public void Retry(){LastError="";}
        void Close(){var previous=writer;writer=null;if(previous!=null)try{previous.Dispose();}catch(Exception e){LastError="Recording failed: "+e.Message;Debug.LogError("[PF-RECORDER] "+e);}}
        public void OnDestroy(){Close();if(Instance==this)Instance=null;}
    }
}
