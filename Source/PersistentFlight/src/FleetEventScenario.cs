using System;
using System.Linq;
using System.Globalization;
using System.Threading;
namespace PersistentFlight {
    public static class FleetEventApi {
        public const int Version=1;
        static PersistentFlightScenario Service(){var s=PersistentFlightScenario.Instance;if(s==null)throw new InvalidOperationException("PersistentFlight is not ready");s.CheckEventThread();return s;}
        static FleetEventLedger Ledger(){var s=Service();if(s.eventsHeld)throw new InvalidOperationException("Mission events are held because saved state could not be restored");return s.eventLedger;}
        public static bool IsHeld {get{return Service().eventsHeld;}}
        static double Now(){return Planetarium.GetUniversalTime();}
        public static FleetRuleResult Register(FleetEventRuleRequest r){return Ledger().Register(r,Now());}
        public static FleetRuleResult Get(Guid id){return Ledger().Get(id);}
        public static FleetRuleResult[] List(){return Ledger().List();}
        public static FleetTimelineEntry[] Timeline(){return Ledger().Timeline();}
        public static void Publish(uint cpu,Guid vessel,string name,string detail){Ledger().Publish(cpu,vessel,Now(),name,detail);}
        public static void Tick(){Ledger().Tick(Now());}
        public static bool Begin(Guid id){return Ledger().Begin(id,Now());}
        public static FleetRuleResult Pending(uint cpu){return Ledger().Pending(cpu);}
        public static FleetRuleResult Complete(Guid id,uint cpu,bool success,string detail){FleetEventLedger.Text(detail,160);return Ledger().Complete(id,cpu,Now(),success,detail);}
        public static FleetRuleResult Cancel(Guid id){return Ledger().Cancel(id,Now());}
        public static void Remove(Guid id){Ledger().Remove(id);}
        public static void HoldApplied(Guid id){Ledger().HoldApplied(id,Now());}
    }
    public partial class PersistentFlightScenario {
        internal FleetEventLedger eventLedger=new FleetEventLedger();
        internal bool eventsHeld;ConfigNode rawEvents;
        internal void CheckEventThread(){if(missionThread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Use mission events on KSP's main thread");}
        static readonly CultureInfo EventCulture=CultureInfo.InvariantCulture;
        static double EventNumber(ConfigNode n,string key){double v=Double.Parse(n.GetValue(key),EventCulture);if(!FleetEventLedger.Finite(v)||v<0)throw new FormatException("Invalid event time");return v;}
        void LoadEvents(ConfigNode node){
            eventLedger=new FleetEventLedger();eventsHeld=false;rawEvents=null;
            var roots=node.GetNodes("FLEET_EVENTS");rawEvents=node.CreateCopy();
            try{
                if(node.GetValue("fleetEventsHeld")=="True")throw new FormatException("Saved event ledger is held");
                if(roots.Length==0){if(node.HasValue("fleetEventsVersion"))throw new FormatException("Missing event ledger");rawEvents=null;return;}
                if(roots.Length!=1||node.GetValue("fleetEventsVersion")!="1")throw new FormatException("Unsupported event ledger");
                var root=roots[0];if(root.GetValue("held")=="True")throw new FormatException("Saved event ledger is held");
                if(root.GetValue("version")!="1")throw new FormatException("Unsupported event schema");
                eventLedger.Sequence=Int64.Parse(root.GetValue("sequence"));if(eventLedger.Sequence<0)throw new FormatException("Invalid event sequence");
                var savedTimeline=Guid.Parse(root.GetValue("timeline"));if(savedTimeline==Guid.Empty)throw new FormatException("Invalid timeline");
                foreach(var n in root.GetNodes("RULE")){
                    var q=new FleetEventRuleRequest(Guid.Parse(n.GetValue("id")),n.GetValue("name"),UInt32.Parse(n.GetValue("source")),n.GetValue("event"),UInt32.Parse(n.GetValue("target")),(FleetAction)Enum.Parse(typeof(FleetAction),n.GetValue("action")),n.GetValue("task"),n.GetValue("success"),EventNumber(n,"wait"),EventNumber(n,"run"),Boolean.Parse(n.GetValue("hold")));
                    q.Validate();var r=new EventRule{Request=q,State=(FleetRuleState)Enum.Parse(typeof(FleetRuleState),n.GetValue("state")),CreatedUT=EventNumber(n,"created"),Deadline=EventNumber(n,"deadline"),Detail=n.GetValue("detail"),HoldPending=Boolean.Parse(n.GetValue("holdPending"))};
                    if(!Enum.IsDefined(typeof(FleetRuleState),r.State)||r.Detail==null||r.Detail.Length>240||r.Deadline<r.CreatedUT||r.HoldPending&&!((r.State==FleetRuleState.Failed&&q.HoldOnFailure)||(r.State==FleetRuleState.Cancelled&&q.Action==FleetAction.Signal))||eventLedger.Rules.Count>=FleetEventLedger.RuleLimit)throw new FormatException("Invalid saved rule");
                    eventLedger.Rules.Add(q.Id,r);
                }
                if(Int32.Parse(root.GetValue("rules"))!=eventLedger.Rules.Count)throw new FormatException("Rule count mismatch");
                if(eventLedger.Rules.Values.Where(r=>r.State==FleetRuleState.Running).GroupBy(r=>r.Request.TargetCpu).Any(g=>g.Count()>1))throw new FormatException("Multiple actions own a CPU");
                long previous=0;
                foreach(var n in root.GetNodes("ENTRY")){
                    long seq=Int64.Parse(n.GetValue("seq"));string name=n.GetValue("event"),detail=n.GetValue("detail");FleetEventLedger.Token(name);
                    if(seq<=previous||seq>eventLedger.Sequence||detail==null||detail.Length>240||eventLedger.Entries.Count>=FleetEventLedger.TimelineLimit)throw new FormatException("Invalid timeline entry");
                    Guid timeline=Guid.Parse(n.GetValue("timeline"));if(timeline==Guid.Empty)throw new FormatException("Invalid entry timeline");
                    eventLedger.Entries.Add(new FleetTimelineEntry(seq,timeline,Guid.Parse(n.GetValue("vessel")),Guid.Parse(n.GetValue("rule")),UInt32.Parse(n.GetValue("cpu")),EventNumber(n,"ut"),name,detail));previous=seq;
                }
                if(Int32.Parse(root.GetValue("entries"))!=eventLedger.Entries.Count)throw new FormatException("Timeline count mismatch");
                eventLedger.Record(0,Guid.Empty,Planetarium.GetUniversalTime(),"SAVE_LOADED","Restored timeline "+savedTimeline+"; new branch "+eventLedger.TimelineId,Guid.Empty);
                rawEvents=null;
            }catch(Exception e){eventsHeld=true;UnityEngine.Debug.LogError("[PF-EVENTS] Event ledger held: "+e.Message);}
        }
        void SaveEvents(ConfigNode node){
            node.AddValue("fleetEventsVersion",1);
            node.AddValue("fleetEventsHeld",eventsHeld);
            if(eventsHeld){if(rawEvents!=null)foreach(var n in rawEvents.GetNodes("FLEET_EVENTS"))node.AddNode(n.CreateCopy());return;}
            var root=node.AddNode("FLEET_EVENTS");root.AddValue("version",1);root.AddValue("held",false);root.AddValue("sequence",eventLedger.Sequence);root.AddValue("timeline",eventLedger.TimelineId);
            root.AddValue("rules",eventLedger.Rules.Count);root.AddValue("entries",eventLedger.Entries.Count);
            foreach(var r in eventLedger.Rules.Values){var q=r.Request;var n=root.AddNode("RULE");n.AddValue("id",q.Id);n.AddValue("name",q.Name);n.AddValue("source",q.SourceCpu);n.AddValue("event",q.EventName);n.AddValue("target",q.TargetCpu);n.AddValue("action",q.Action);n.AddValue("task",q.Task);n.AddValue("success",q.SuccessEvent??"");n.AddValue("wait",q.WaitSeconds.ToString("R",EventCulture));n.AddValue("run",q.RunSeconds.ToString("R",EventCulture));n.AddValue("hold",q.HoldOnFailure);n.AddValue("state",r.State);n.AddValue("created",r.CreatedUT.ToString("R",EventCulture));n.AddValue("deadline",r.Deadline.ToString("R",EventCulture));n.AddValue("detail",r.Detail);n.AddValue("holdPending",r.HoldPending);}
            foreach(var e in eventLedger.Entries){var n=root.AddNode("ENTRY");n.AddValue("seq",e.Sequence);n.AddValue("timeline",e.TimelineId);n.AddValue("vessel",e.VesselId);n.AddValue("rule",e.RuleId);n.AddValue("cpu",e.Cpu);n.AddValue("ut",e.UT.ToString("R",EventCulture));n.AddValue("event",e.EventName);n.AddValue("detail",e.Detail);}
        }
        void RecordBurnFinish(BurnJob job,string status){
            if(eventsHeld)return;
            try{
                string name=status=="Complete"?"BURN_COMPLETED":status=="Cancelled"?"BURN_CANCELLED":"BURN_STOPPED",detail=status+"; delivered "+job.DeliveredDV.ToString("F2",EventCulture)+" m/s";
                var v=FlightGlobals.Vessels==null?null:FlightGlobals.Vessels.FirstOrDefault(x=>x!=null&&x.id==job.VesselId);
                uint[] cpus=v==null?new uint[0]:v.loaded?v.parts.Where(p=>p.Modules.Cast<PartModule>().Any(m=>m.moduleName=="ModuleRegionalAutonomy")).Select(p=>p.flightID).ToArray():v.protoVessel==null?new uint[0]:v.protoVessel.protoPartSnapshots.Where(p=>p.modules.Any(m=>m.moduleName=="ModuleRegionalAutonomy")).Select(p=>p.flightID).ToArray();
                if(cpus.Length==0)eventLedger.Publish(0,job.VesselId,Planetarium.GetUniversalTime(),name,detail);else foreach(uint cpu in cpus)eventLedger.Publish(cpu,job.VesselId,Planetarium.GetUniversalTime(),name,detail);
            }catch(Exception e){UnityEngine.Debug.LogError("[PF-EVENTS] Burn record: "+e.Message);}
        }
    }
}
