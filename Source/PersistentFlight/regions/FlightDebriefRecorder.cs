using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public string debriefId="";
    }
    public static class FlightDebriefRecorder {
        static FlightDebrief Get(ModuleRegionalAutonomy m,bool create){
            var s=PersistentFlightScenario.Instance;if(s==null||m==null||m.vessel==null)return null;
            Guid id;var r=Guid.TryParse(m.debriefId,out id)?s.Debriefs.Find(id):null;
            if(r!=null&&!r.Finished)return r;
            if(!create||!m.nodeRequested)return null;
            r=new FlightDebrief{Id=Guid.NewGuid(),Branch=s.Debriefs.Branch,Vessel=m.vessel.id,Cpu=m.part.flightID,Craft=PersistentFlightScenario.ReportText(m.vessel.vesselName),Body=PersistentFlightScenario.ReportText(m.vessel.mainBody.bodyName),Mode=DebriefMode.LiveManeuver,PlannedDV=m.nodeInitialDV,NodeUT=m.nodeUT,QueuedUT=Planetarium.GetUniversalTime()};
            s.Debriefs.Add(r);m.debriefId=r.Id.ToString();return r;
        }
        static void Observe(Action a){var s=PersistentFlightScenario.Instance;if(s!=null)s.ObserveDebrief(a);}
        static double Resource(Vessel v,string name){double cap;return MissionControl.Resource(v,name,out cap);}
        public static void Invalidated(ModuleRegionalAutonomy m,bool changedResources){Observe(()=>{var r=Get(m,false);if(r!=null){r.Incomplete=true;if(changedResources)r.ResourcesKnown=false;}});}
        public static void Queued(ModuleRegionalAutonomy m){Observe(()=>Get(m,true));}
        public static void Begin(ModuleRegionalAutonomy m){
            if(m==null||m.vessel==null||!m.nodeRequested||!MissionIntegrity.Check(m))return;
            FleetRecovery.Before("Before pilot burn: "+m.vessel.vesselName);m.burnInProgress=true;
            Observe(()=>{
            var r=Get(m,true);if(r==null||r.Started)return;
            r.Started=true;r.StartUT=Planetarium.GetUniversalTime();r.LastSampleUT=r.StartUT;r.ResourcesKnown=true;
            r.InitialLF=Resource(m.vessel,"LiquidFuel");r.InitialOx=Resource(m.vessel,"Oxidizer");r.InitialEC=Resource(m.vessel,"ElectricCharge");r.Outcome="Burning";
        });}
        static void NetResources(ModuleRegionalAutonomy m,FlightDebrief r){
            if(!r.Started||!r.ResourcesKnown)return;
            r.LiquidFuel=r.InitialLF-Resource(m.vessel,"LiquidFuel");r.Oxidizer=r.InitialOx-Resource(m.vessel,"Oxidizer");r.ElectricCharge=r.InitialEC-Resource(m.vessel,"ElectricCharge");
        }
        public static void Sample(ModuleRegionalAutonomy m,double dt){Observe(()=>{
            var r=Get(m,false);if(r==null||!r.Started)return;
            if(r.Vessel!=m.vessel.id){r.Incomplete=true;r.ResourcesKnown=false;End(m,"Interrupted: vessel identity changed");return;}
            if(!m.nodeRequested){End(m,"Interrupted");return;}
            if(m.vessel.packed||TimeWarp.CurrentRate!=1){r.Incomplete=true;return;}
            double now=Planetarium.GetUniversalTime();if(now-r.LastSampleUT>.2||now<r.LastSampleUT)r.Incomplete=true;r.LastSampleUT=now;
            double thrust=m.vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Sum(e=>(double)e.finalThrust);
            r.Integrate(thrust,m.vessel.GetTotalMass(),dt);NetResources(m,r);
        });}
        public static void End(ModuleRegionalAutonomy m,string outcome){if(m!=null)m.burnInProgress=false;Observe(()=>{
            var r=Get(m,false);if(r==null)return;
            NetResources(m,r);
            try{r.ResidualDV=m.nodeRequested&&r.Vessel==m.vessel.id?MissionControl.PreviewRemaining(m).magnitude:Double.NaN;}catch{r.ResidualDV=Double.NaN;}
            r.Outcome=PersistentFlightScenario.ReportText(outcome);r.EndUT=Planetarium.GetUniversalTime();r.Finished=true;m.debriefId="";
        });}
    }
    // Sample after stock physics has published actual engine thrust. No guidance or throttle writes.
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FlightDebriefObserver:MonoBehaviour {
        IEnumerator Start(){
            var wait=new WaitForFixedUpdate();
            while(true){yield return wait;if(!FlightGlobals.ready||FlightGlobals.Vessels==null)continue;
                foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded))foreach(var m in MissionControl.Modules(v))
                    FlightDebriefRecorder.Sample(m,Time.fixedDeltaTime);
            }
        }
    }
}
