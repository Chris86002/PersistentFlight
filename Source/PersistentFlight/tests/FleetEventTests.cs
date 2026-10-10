using System;
using System.Linq;
using PersistentFlight;
class FleetEventTests {
    static int checks;
    static void Check(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static void Throws(Action a,string why){bool threw=false;try{a();}catch(ArgumentException){threw=true;}catch(InvalidOperationException){threw=true;}Check(threw,why);}
    static FleetEventRuleRequest Rule(uint source,uint target,string trigger,string success,double wait,double run,bool hold){return new FleetEventRuleRequest(Guid.NewGuid(),"Test",source,trigger,target,FleetAction.Signal,"TASK",success,wait,run,hold);}
    static void Main(){try{
        var l=new FleetEventLedger();var q=Rule(1,2,"ARRIVED","DONE",100,20,true);
        l.Publish(1,Guid.NewGuid(),0,"ARRIVED","Old event");l.Register(q,1);Check(l.Get(q.Id).State==FleetRuleState.Waiting,"Historical event replayed");
        l.Register(q,1);Check(l.List().Length==1,"Retry duplicated rule");
        Throws(()=>l.Register(new FleetEventRuleRequest(q.Id,"Different",1,"ARRIVED",2,FleetAction.Signal,"TASK","DONE",100,20,true),1),"Conflicting request accepted");
        l.Publish(3,Guid.Empty,2,"ARRIVED","");l.Publish(1,Guid.Empty,2,"OTHER","");Check(l.Get(q.Id).State==FleetRuleState.Waiting,"Wrong source/event triggered");
        l.Publish(1,Guid.NewGuid(),3,"ARRIVED","");Check(l.Get(q.Id).State==FleetRuleState.Ready,"Event did not trigger");Check(l.Begin(q.Id,4),"Dispatch failed");Check(!l.Begin(q.Id,4),"Duplicate dispatch");
        Check(l.Pending(2).Request.Id==q.Id&&l.Pending(1)==null,"Task assigned to wrong CPU");
        Throws(()=>l.Complete(q.Id,3,5,true,"wrong"),"Wrong CPU completed task");
        var follow=Rule(2,4,"DONE","",100,20,false);l.Register(follow,4);
        l.Complete(q.Id,2,5,true,"ok");Check(l.Get(follow.Id).State==FleetRuleState.Ready,"Completion did not trigger dependent rule");
        long seq=l.Timeline().Last().Sequence;l.Complete(q.Id,2,6,true,"ok");Check(seq==l.Timeline().Last().Sequence,"Completion retry emitted another event");
        Throws(()=>l.Complete(q.Id,2,6,false,"wrong"),"Contradictory completion accepted");
        l.Publish(1,Guid.Empty,6,"ARRIVED","");Check(l.Get(q.Id).State==FleetRuleState.Complete,"Trigger repeated completed task");
        var busy=Rule(2,4,"DONE","",100,20,false);l.Register(busy,6);l.Publish(2,Guid.Empty,7,"DONE","");Check(l.Begin(follow.Id,7),"First CPU lease failed");Check(!l.Begin(busy.Id,7),"Two signals own one CPU");
        l.Complete(follow.Id,4,8,true,"ok");Check(l.Begin(busy.Id,8),"CPU lease not released");
        l.Cancel(busy.Id,9);Check(l.Get(busy.Id).State==FleetRuleState.Cancelled&&l.Get(busy.Id).HoldPending,"Running cancellation did not request hold");
        Throws(()=>l.Remove(busy.Id),"Pending cancellation hold erased");l.HoldApplied(busy.Id,9);l.Remove(busy.Id);Check(l.Get(busy.Id)==null,"Terminal remove failed");
        var timed=Rule(1,3,"START","NEVER",5,2,true);l.Register(timed,10);l.Publish(1,Guid.Empty,15,"START","");Check(l.Get(timed.Id).State==FleetRuleState.Failed,"Deadline-boundary event fired late");
        Check(l.Get(timed.Id).HoldPending,"Timeout fallback not requested");
        var running=Rule(1,2,"START","NEVER",30,2,false);l.Register(running,16);l.Publish(1,Guid.Empty,17,"START","");l.Begin(running.Id,17);
        Throws(()=>l.Complete(running.Id,2,19,true,"late"),"Late success accepted");Check(l.Get(running.Id).State==FleetRuleState.Failed&&!l.Get(running.Id).HoldPending,"Action timeout wrong");
        var backwards=Rule(1,2,"START","",30,10,true);l.Register(backwards,20);l.Tick(20);l.Tick(18);Check(l.Get(backwards.Id).State==FleetRuleState.Failed,"Backward time continued mission");
        Throws(()=>l.Register(Rule(0,2,"START","",1,1,false),20),"Zero source accepted");Throws(()=>l.Publish(1,Guid.Empty,20,"bad token", ""),"Invalid token accepted");Throws(()=>l.Tick(Double.NaN),"NaN time accepted");
        var capacity=new FleetEventLedger();for(int i=0;i<FleetEventLedger.RuleLimit;i++)capacity.Register(Rule(1,2,"START","",10000,10,false),0);
        Throws(()=>capacity.Register(Rule(1,2,"START","",10000,10,false),0),"Rule cap ignored");
        for(int i=0;i<2500;i++)capacity.Publish(1,Guid.Empty,0,"TRACE","x");Check(capacity.Timeline().Length==FleetEventLedger.TimelineLimit&&capacity.List().Length==FleetEventLedger.RuleLimit,"Timeline rollover discarded rules");
        for(int seed=0;seed<200;seed++){
            var rng=new Random(seed);var ledger=new FleetEventLedger();var requests=Enumerable.Range(0,20).Select(i=>Rule((uint)(i+1),(uint)(i+101),"GO","",100,30,true)).ToArray();
            foreach(var r in requests)ledger.Register(r,0);
            double cursor=0;foreach(var r in requests.OrderBy(x=>rng.Next())){
                cursor+=4;ledger.Publish(r.SourceCpu,Guid.NewGuid(),cursor,"GO","");Check(ledger.Get(r.Id).State==FleetRuleState.Ready,"Random event lost");Check(ledger.Begin(r.Id,cursor+1),"Random dispatch lost");
                bool good=rng.Next(2)==0;ledger.Complete(r.Id,r.TargetCpu,cursor+2,good,good?"ok":"failed");
                Check(ledger.Get(r.Id).State==(good?FleetRuleState.Complete:FleetRuleState.Failed),"Random completion wrong");Check(ledger.Get(r.Id).HoldPending==!good,"Random fallback wrong");
                ledger.Publish(r.SourceCpu,Guid.NewGuid(),cursor+3,"GO","");Check(ledger.Get(r.Id).State==(good?FleetRuleState.Complete:FleetRuleState.Failed),"Random replay");
            }
        }
        Console.WriteLine("PASS "+checks+" event mission checks");
    }catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.Exit(1);}}
}
