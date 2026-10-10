using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public string maneuverVessel="",maneuverParts="",maneuverEngines="",interruptionReason="";
        [KSPField(isPersistant=true)] public bool burnInProgress;
        internal float nextIntegrity;
        public override void OnStart(StartState state){base.OnStart(state);if(HighLogic.LoadedSceneIsFlight){
            if(!String.IsNullOrEmpty(interruptionReason))MissionIntegrity.Stop(this);
            else if(nodeRequested&&String.IsNullOrEmpty(maneuverParts)&&vessel.ctrlState.mainThrottle>0)MissionIntegrity.Interrupt(this,"Unverified powered save; cancel and requeue");
        }}
    }
    public static class MissionIntegrity {
        static string Parts(Vessel v){
            string text=String.Join(";",v.parts.OrderBy(p=>p.flightID).Select(p=>p.flightID+":"+(p.parent==null?0:p.parent.flightID)).ToArray());
            using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");
        }
        public static void Capture(ModuleRegionalAutonomy m){m.maneuverVessel=m.vessel.id.ToString();m.maneuverParts=Parts(m.vessel);m.maneuverEngines=String.Join(",",m.vessel.parts.Where(p=>p.Modules.OfType<ModuleEngines>().Any()).Select(p=>p.flightID.ToString()).ToArray());m.interruptionReason="";m.burnInProgress=false;}
        public static void Clear(ModuleRegionalAutonomy m){if(m.interruptionReason!="")m.missionHold=false;m.interruptionReason="";m.maneuverVessel="";m.maneuverParts="";m.maneuverEngines="";m.burnInProgress=false;}
        public static void Loaded(ModuleRegionalAutonomy m){if(m.burnInProgress){m.interruptionReason="Saved during a live burn; cancel and requeue after review";m.missionHold=true;m.burnInProgress=false;m.warpReady=false;}}
        public static void Release(ModuleRegionalAutonomy m){if(m.interruptionReason!="")throw new InvalidOperationException(m.interruptionReason);m.missionHold=false;}
        public static void Stop(ModuleRegionalAutonomy m){
            if(m.vessel==null||!m.vessel.loaded)return;
            m.vessel.ctrlState.mainThrottle=0;
            foreach(var e in m.vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Shutdown();
            // A staged engine can now belong to a CPU-less fragment. Stop the
            // original captured engines there as well, leaving other engines alone.
            var owned=(m.maneuverEngines??"").Split(',');
            if(FlightGlobals.Vessels!=null)foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded&&v!=m.vessel))foreach(var p in v.parts.Where(p=>owned.Contains(p.flightID.ToString()))){v.ctrlState.mainThrottle=0;foreach(var e in p.Modules.OfType<ModuleEngines>())e.Shutdown();}
        }
        public static void Interrupt(ModuleRegionalAutonomy m,string reason){
            FlightDebriefRecorder.Invalidated(m,reason.StartsWith("Craft"));m.interruptionReason=reason;m.HoldMission(reason);Stop(m);
        }
        public static bool Check(ModuleRegionalAutonomy m){
            if(m.interruptionReason!=""){m.missionHold=true;Stop(m);return false;}
            if(m.QueueCount==0||m.vessel==null||!m.vessel.loaded||m.vessel.packed)return !m.missionHold;
            if(m.maneuverParts=="")Capture(m); // Legacy coasting plans adopt a baseline.
            string reason=null;
            if(m.maneuverVessel!=m.vessel.id.ToString())reason="Craft identity changed; cancel and requeue";
            else if(Time.realtimeSinceStartup>=m.nextIntegrity){m.nextIntegrity=Time.realtimeSinceStartup+.25f;if(m.maneuverParts!=Parts(m.vessel))reason="Craft parts or connections changed; cancel and requeue";}
            var cpu=m.part.Modules.OfType<kOS.Module.kOSProcessor>().FirstOrDefault();
            if(reason==null&&m.burnInProgress&&(cpu==null||cpu.ProcessorMode.ToString()!="READY"||!m.PilotOnline))reason="Live burn lost its pilot or CPU power; cancel and requeue";
            if(reason!=null){Interrupt(m,reason);return false;}
            return !m.missionHold;
        }
    }
    [DefaultExecutionOrder(-10000)]
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class MissionIntegrityObserver:MonoBehaviour {
        public void FixedUpdate(){if(!FlightGlobals.ready||FlightGlobals.Vessels==null)return;
            foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded&&!v.packed))foreach(var m in MissionControl.Modules(v)){
                try{MissionIntegrity.Check(m);if(m.missionHold&&m.nodeRequested)MissionIntegrity.Stop(m);}catch(Exception e){MissionIntegrity.Interrupt(m,"Maneuver integrity unavailable: "+MissionControl.Text(e.Message));}
            }
        }
    }
}
