using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace PersistentFlight {
    public enum FleetAction { Signal, Notify, Hold }
    public enum FleetRuleState { Waiting, Ready, Running, Complete, Failed, Cancelled }
    public sealed class FleetEventRuleRequest {
        public readonly Guid Id;
        public readonly string Name, EventName, Task, SuccessEvent;
        public readonly uint SourceCpu, TargetCpu;
        public readonly FleetAction Action;
        public readonly double WaitSeconds, RunSeconds;
        public readonly bool HoldOnFailure;
        public FleetEventRuleRequest(Guid id,string name,uint sourceCpu,string eventName,uint targetCpu,FleetAction action,string task,string successEvent,double waitSeconds,double runSeconds,bool holdOnFailure) {
            Id=id;Name=name;SourceCpu=sourceCpu;EventName=eventName;TargetCpu=targetCpu;Action=action;Task=task;SuccessEvent=successEvent;WaitSeconds=waitSeconds;RunSeconds=runSeconds;HoldOnFailure=holdOnFailure;
        }
        internal void Validate() {
            if(Id==Guid.Empty||SourceCpu==0||TargetCpu==0||!Enum.IsDefined(typeof(FleetAction),Action))throw new ArgumentException("Use a rule ID and CPU part identities");
            FleetEventLedger.Text(Name,80);FleetEventLedger.Token(EventName);FleetEventLedger.Text(Task,80);
            if(!String.IsNullOrEmpty(SuccessEvent))FleetEventLedger.Token(SuccessEvent);
            if(!FleetEventLedger.Finite(WaitSeconds)||WaitSeconds<1||WaitSeconds>31557600||!FleetEventLedger.Finite(RunSeconds)||RunSeconds<1||RunSeconds>31557600)throw new ArgumentException("Timeouts must be 1 second to one year of game time");
        }
        internal string Signature { get { return String.Join("|",new[]{Name,SourceCpu.ToString(),EventName,TargetCpu.ToString(),Action.ToString(),Task,SuccessEvent??"",WaitSeconds.ToString("R",CultureInfo.InvariantCulture),RunSeconds.ToString("R",CultureInfo.InvariantCulture),HoldOnFailure.ToString()}); } }
    }
    public sealed class FleetRuleResult {
        public readonly FleetEventRuleRequest Request;
        public readonly FleetRuleState State;
        public readonly double CreatedUT,Deadline;
        public readonly string Detail;
        public readonly bool HoldPending;
        internal FleetRuleResult(EventRule r){Request=r.Request;State=r.State;CreatedUT=r.CreatedUT;Deadline=r.Deadline;Detail=r.Detail;HoldPending=r.HoldPending;}
    }
    public sealed class FleetTimelineEntry {
        public readonly long Sequence; public readonly Guid TimelineId,VesselId,RuleId;
        public readonly uint Cpu; public readonly double UT;
        public readonly string EventName,Detail;
        public FleetTimelineEntry(long sequence,Guid timeline,Guid vessel,Guid rule,uint cpu,double ut,string name,string detail){Sequence=sequence;TimelineId=timeline;VesselId=vessel;RuleId=rule;Cpu=cpu;UT=ut;EventName=name;Detail=detail;}
    }
    internal sealed class EventRule {
        internal FleetEventRuleRequest Request;internal FleetRuleState State;internal double CreatedUT,Deadline;
        internal string Detail="Waiting for event";internal bool HoldPending;
    }
    // Pure state machine. Only the regional adapter can apply actions to a live craft.
    public sealed class FleetEventLedger {
        public const int RuleLimit=512,TimelineLimit=2000;
        internal readonly Dictionary<Guid,EventRule> Rules=new Dictionary<Guid,EventRule>();
        internal readonly List<FleetTimelineEntry> Entries=new List<FleetTimelineEntry>();
        internal long Sequence;internal Guid TimelineId=Guid.NewGuid();internal double LastUT=Double.NaN;
        public static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
        public static void Text(string x,int max){if(String.IsNullOrWhiteSpace(x)||x.Length>max||x.Any(c=>Char.IsControl(c)||c=='|'||c=='{'||c=='}'||c=='='))throw new ArgumentException("Use plain text without control characters, braces, equals or pipes");}
        public static void Token(string x){if(String.IsNullOrEmpty(x)||x.Length>64||x.Any(c=>!(c>='A'&&c<='Z')&&!(c>='0'&&c<='9')&&c!='_'))throw new ArgumentException("Event names use uppercase letters, digits and underscores, up to 64 characters");}
        static void Time(double now){if(!Finite(now)||now<0)throw new ArgumentException("Invalid game time");}
        public FleetRuleResult Register(FleetEventRuleRequest request,double now) {
            Time(now);if(request==null)throw new ArgumentNullException("request");request.Validate();EventRule prior;
            if(Rules.TryGetValue(request.Id,out prior)){if(prior.Request.Signature!=request.Signature)throw new InvalidOperationException("Rule ID belongs to another request");return new FleetRuleResult(prior);}
            if(Rules.Count>=RuleLimit)throw new InvalidOperationException("Rule storage full; export the timeline and remove terminal rules before adding more");
            var r=new EventRule{Request=request,CreatedUT=now,Deadline=now+request.WaitSeconds};Rules.Add(request.Id,r);
            Record(request.TargetCpu,Guid.Empty,now,"RULE_ARMED",request.Name,request.Id);return new FleetRuleResult(r);
        }
        public FleetRuleResult Get(Guid id){EventRule r;return Rules.TryGetValue(id,out r)?new FleetRuleResult(r):null;}
        public FleetRuleResult[] List(){return Rules.Values.OrderBy(r=>r.CreatedUT).ThenBy(r=>r.Request.Id).Select(r=>new FleetRuleResult(r)).ToArray();}
        public FleetTimelineEntry[] Timeline(){return Entries.ToArray();}
        public void Publish(uint cpu,Guid vessel,double now,string name,string detail){
            Time(now);Token(name);if(detail==null)detail="";if(detail.Length>240)detail=detail.Substring(0,240);detail=detail.Replace('\r',' ').Replace('\n',' ');
            Tick(now);Record(cpu,vessel,now,name,detail,Guid.Empty);
            foreach(var r in Rules.Values.Where(r=>r.State==FleetRuleState.Waiting&&r.Request.SourceCpu==cpu&&r.Request.EventName==name).ToArray()) {
                r.State=FleetRuleState.Ready;r.Deadline=now+r.Request.RunSeconds;r.Detail="Triggered by "+name;
                Record(r.Request.TargetCpu,vessel,now,"RULE_TRIGGERED",r.Request.Name,r.Request.Id);
            }
        }
        internal void Record(uint cpu,Guid vessel,double now,string name,string detail,Guid rule){
            if(Sequence==Int64.MaxValue)throw new InvalidOperationException("Timeline sequence exhausted");
            Entries.Add(new FleetTimelineEntry(++Sequence,TimelineId,vessel,rule,cpu,now,name,detail));
            if(Entries.Count>TimelineLimit)Entries.RemoveRange(0,Entries.Count-TimelineLimit);
        }
        public void Tick(double now){
            Time(now);
            if(Finite(LastUT)&&now<LastUT-.01)foreach(var r in Rules.Values.Where(r=>!Terminal(r.State)).ToArray())Fail(r,now,"Game time moved backwards; replan mission");
            LastUT=now;
            foreach(var r in Rules.Values.Where(r=>!Terminal(r.State)&&now>=r.Deadline).ToArray())Fail(r,now,r.State==FleetRuleState.Waiting?"Event wait timed out":"Action timed out");
        }
        public bool Begin(Guid id,double now){
            Tick(now);EventRule r;if(!Rules.TryGetValue(id,out r)||r.State!=FleetRuleState.Ready)return false;
            if(Rules.Values.Any(x=>x!=r&&x.State==FleetRuleState.Running&&x.Request.TargetCpu==r.Request.TargetCpu))return false;
            r.State=FleetRuleState.Running;r.Detail="Action dispatched";Record(r.Request.TargetCpu,Guid.Empty,now,"ACTION_STARTED",r.Request.Name+": "+r.Request.Task,id);return true;
        }
        public FleetRuleResult Pending(uint cpu){return Rules.Values.Where(r=>r.Request.TargetCpu==cpu&&r.State==FleetRuleState.Running&&r.Request.Action==FleetAction.Signal).Select(r=>new FleetRuleResult(r)).FirstOrDefault();}
        public FleetRuleResult Complete(Guid id,uint cpu,double now,bool success,string detail){
            Time(now);Tick(now);EventRule r;if(!Rules.TryGetValue(id,out r)||r.Request.TargetCpu!=cpu)throw new InvalidOperationException("Action does not belong to this CPU");
            if((success&&r.State==FleetRuleState.Complete)||(!success&&r.State==FleetRuleState.Failed))return new FleetRuleResult(r);
            if(r.State!=FleetRuleState.Running)throw new InvalidOperationException("Action is not running");
            if(!success)Fail(r,now,detail);else{r.State=FleetRuleState.Complete;r.Detail=detail;Record(cpu,Guid.Empty,now,"ACTION_COMPLETED",r.Request.Name+": "+detail,id);
                if(!String.IsNullOrEmpty(r.Request.SuccessEvent))Publish(cpu,Guid.Empty,now,r.Request.SuccessEvent,r.Request.Name);}
            return new FleetRuleResult(r);
        }
        void Fail(EventRule r,double now,string why){r.State=FleetRuleState.Failed;r.Detail=why;r.HoldPending=r.Request.HoldOnFailure;Record(r.Request.TargetCpu,Guid.Empty,now,"ACTION_FAILED",r.Request.Name+": "+why,r.Request.Id);}
        public void HoldApplied(Guid id,double now){EventRule r;if(!Rules.TryGetValue(id,out r)||!r.HoldPending)return;r.HoldPending=false;Record(r.Request.TargetCpu,Guid.Empty,now,"FAILURE_HOLD",r.Request.Name,id);}
        public FleetRuleResult Cancel(Guid id,double now){Time(now);EventRule r;if(!Rules.TryGetValue(id,out r))return null;if(!Terminal(r.State)){r.HoldPending=r.State==FleetRuleState.Running&&r.Request.Action==FleetAction.Signal;r.State=FleetRuleState.Cancelled;r.Detail="Cancelled by player";Record(r.Request.TargetCpu,Guid.Empty,now,"RULE_CANCELLED",r.Request.Name,id);}return new FleetRuleResult(r);}
        public void Remove(Guid id){EventRule r;if(Rules.TryGetValue(id,out r)){if(!Terminal(r.State)||r.HoldPending)throw new InvalidOperationException("Cancel or finish the rule, and apply any pending hold before removing it");Rules.Remove(id);}}
        internal static bool Terminal(FleetRuleState state){return state==FleetRuleState.Complete||state==FleetRuleState.Failed||state==FleetRuleState.Cancelled;}
    }
}
