using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public string queuedNodes64="",completedNodes64="",pilotEngineTag="";
        [KSPField(isPersistant=true)] public bool resumePilot,missionHold;
        [KSPField(isPersistant=true)] public string previousBoot="";
        string countedQueue; int countedNodes;
        public int QueueCount { get {
            if(countedQueue!=queuedNodes64) { countedQueue=queuedNodes64; try { countedNodes=MissionQueue.Read(queuedNodes64).Count; } catch { countedNodes=0; } }
            return countedNodes+(nodeRequested?1:0);
        } }
        public override void OnLoad(ConfigNode node){base.OnLoad(node);warpReady=false;pilotReady=false;scriptHeartbeat=UnityEngine.Time.realtimeSinceStartup;RoverRouteController.Reset(this);MissionIntegrity.Loaded(this);}
    }
    public sealed class SavedManeuver {
        public double UT,DV; public string Target,Raw;
        public SavedManeuver(double ut,double dv,string target,string raw){UT=ut;DV=dv;Target=target;Raw=raw;}
    }
    public static class MissionQueue {
        static readonly CultureInfo C=CultureInfo.InvariantCulture;
        public static List<SavedManeuver> Read(string encoded) {
            var result=new List<SavedManeuver>();if(String.IsNullOrEmpty(encoded))return result;
            if(encoded.Length>131072)throw new FormatException("Mission queue exceeds its size limit");
            foreach(var line in Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('\n')){
                var n=line.Split(';');double ut,dv;
                if(n.Length!=4 || !Double.TryParse(n[0],NumberStyles.Float,C,out ut) || !Double.TryParse(n[1],NumberStyles.Float,C,out dv) ||
                    !PersistentFlight.BurnMath.Finite(ut)||!PersistentFlight.BurnMath.Finite(dv)||dv<.5||dv>10000)throw new FormatException("Invalid saved mission queue");
                MissionControl.ReadOrbit(n[2]);result.Add(new SavedManeuver(ut,dv,n[2],n[3]));
            }
            if(result.Count>32)throw new FormatException("Use at most 32 maneuvers");return result;
        }
        public static string Write(IEnumerable<SavedManeuver> nodes) {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join("\n",nodes.Select(n=>n.UT.ToString("R",C)+";"+n.DV.ToString("R",C)+";"+n.Target+";"+n.Raw).ToArray())));
        }
        public static void Activate(ModuleRegionalAutonomy m,SavedManeuver n) {
            m.nodeUT=n.UT;m.nodeInitialDV=n.DV;m.nodeTargetOrbit=n.Target;m.nodeRawDV=n.Raw;m.nodeRequested=true;m.nodeStatus="Queued";FlightDebriefRecorder.Queued(m);
        }
        public static void Advance(ModuleRegionalAutonomy m) {
            if(m.nodeRequested||m.missionHold||!MissionIntegrity.Check(m))return;
            var queue=Read(m.queuedNodes64);if(queue.Count==0)return;
            var next=queue[0];
            if(next.UT<=Planetarium.GetUniversalTime()+5){m.missionHold=true;m.nodeStatus="Queue paused: next maneuver is overdue";return;}
            if(m.soiMission&&!SoiFlightPlan.PrepareNext(m,next))return;
            if(MissionControl.ReadOrbit(next.Target).referenceBody!=m.vessel.mainBody){m.missionHold=true;m.nodeStatus="Queue paused: maneuver belongs to another body";return;}
            queue.RemoveAt(0);m.queuedNodes64=Write(queue);Activate(m,next);
        }
        public static void QueuePlan(Vessel v,ModuleRegionalAutonomy m) {
            QueuePlan(v,m,false);
        }
        public static void QueuePlan(Vessel v,ModuleRegionalAutonomy m,bool acrossSoi) {
            // Validate every node before changing any persisted state.
            MissionControl.ValidateQueue(v,m);
            var nodes=v.patchedConicSolver.maneuverNodes.OrderBy(n=>n.UT).ToArray();
            if(nodes.Length==0||nodes.Length>32)throw new InvalidOperationException("Create between 1 and 32 future maneuvers first");
            var saved=new List<SavedManeuver>();double previous=Planetarium.GetUniversalTime()+5;
            foreach(var n in nodes){
                if(n.nextPatch==null||n.patch==null||n.patch.referenceBody!=n.nextPatch.referenceBody||(!acrossSoi&&n.patch.referenceBody!=v.mainBody))throw new InvalidOperationException("Use a supported maneuver patch; choose SOI plan for burns on later bodies");
                double dv=n.GetBurnVector(n.patch).magnitude;
                if(n.UT<=previous||!PersistentFlight.BurnMath.Finite(dv)||dv<.5||dv>10000)throw new InvalidOperationException("Use chronological future maneuvers from 0.5 to 10000 m/s");
                saved.Add(new SavedManeuver(n.UT,dv,MissionControl.OrbitText(n.nextPatch),MissionControl.RawVector(n.DeltaV)));previous=n.UT;
            }
            ConfigureRecovery(m);MissionIntegrity.Capture(m);m.missionHold=false;m.soiMission=acrossSoi;m.soiDepartureBody=v.mainBody.flightGlobalsIndex;m.soiStatus=acrossSoi?"SOI plan saved":"No SOI plan";
            m.queuedNodes64=Write(saved);m.Arm(true);Advance(m);FleetRecovery.Before("Flight plan queued: "+v.vesselName);
        }
        public static void ConfigureRecovery(ModuleRegionalAutonomy m) {
            var cpu=m.part.Modules.OfType<kOS.Module.kOSProcessor>().FirstOrDefault();
            if(cpu==null)throw new InvalidOperationException("Mission recovery needs a kOS CPU");
            if(!m.resumePilot)m.previousBoot=cpu.bootFile;
            cpu.bootFile="boot/pf_mission_resume.ks";m.resumePilot=true;
        }
        public static void RestoreBoot(ModuleRegionalAutonomy m) {
            var cpu=m.part.Modules.OfType<kOS.Module.kOSProcessor>().FirstOrDefault();
            if(cpu!=null && cpu.bootFile=="boot/pf_mission_resume.ks")cpu.bootFile=m.previousBoot;
            m.resumePilot=false;
        }
        public static void Completed(ModuleRegionalAutonomy m) {
            var history=Read(m.completedNodes64);history.Add(new SavedManeuver(m.nodeUT,m.nodeInitialDV,m.nodeTargetOrbit,m.nodeRawDV));
            m.completedNodes64=Write(history.Skip(Math.Max(0,history.Count-32)));
        }
        public static void Cleanup(ModuleRegionalAutonomy m) {
            if(m.vessel==null||m.vessel.patchedConicSolver==null)return;
            foreach(var n in Read(m.completedNodes64)){
                var match=m.vessel.patchedConicSolver.maneuverNodes.FirstOrDefault(x=>Math.Abs(x.UT-n.UT)<.001&&MissionControl.RawVector(x.DeltaV)==n.Raw);
                if(match!=null)match.RemoveSelf();
            }
        }
    }
}
