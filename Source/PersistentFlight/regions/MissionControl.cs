using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PersistentFlight.Regions
{
    public sealed partial class ModuleRegionalAutonomy
    {
        [KSPField(isPersistant=true)] public string missionPhase="Script phase not reported";
        [KSPField(isPersistant=true)] public bool recordFlight;
        [KSPField(isPersistant=true)] public string landingSite="KSC grass";
        [KSPField(isPersistant=true)] public string landingBody="Kerbin";
        [KSPField(isPersistant=true)] public double landingLatitude=-.2187,landingLongitude=-74.5732;
        [KSPField(isPersistant=true)] public string landingVessel="";
        [KSPField(isPersistant=true)] public bool nodeRequested;
        [KSPField(isPersistant=true)] public double nodeUT,nodeInitialDV;
        [KSPField(isPersistant=true)] public string nodeTargetOrbit="",nodeRawDV="",nodeStatus="No maneuver queued";
        [KSPField(isPersistant=true)] public bool completedNodePending;
        [KSPField(isPersistant=true)] public double completedNodeUT;
        [KSPField(isPersistant=true)] public string completedNodeRawDV="";
        public bool pilotReady;
        public float pilotHeartbeat;
        public bool PilotOnline { get { return pilotReady && Time.realtimeSinceStartup-pilotHeartbeat<5; } }
        public void Heartbeat(bool ready) { pilotReady=ready;pilotHeartbeat=Time.realtimeSinceStartup; }
        public void CancelNode(string reason) { bool had=nodeRequested||QueueCount>0;FlightDebriefRecorder.End(this,reason);nodeRequested=false;queuedNodes64="";soiMission=false;MissionIntegrity.Clear(this);soiStatus=reason;nodeStatus=reason;warpReady=false;if(had)FleetEventDirector.Emit(this,"MANEUVER_STOPPED",reason); }
        public override void OnUpdate() {
            base.OnUpdate();
            if(HighLogic.LoadedSceneIsFlight&&vessel==FlightGlobals.ActiveVessel&&completedNodePending)
                MissionControl.CleanupCompleted(this);
        }
    }

    // Snapshot a node's post-burn orbit while its native solver is available.
    // Remaining velocity uses the same calculation as ManeuverNode.GetBurnVector,
    // so the pilot does not depend on a node gizmo surviving vessel focus changes.
    public static class MissionControl
    {
        static readonly CultureInfo C=CultureInfo.InvariantCulture;
        public static ModuleRegionalAutonomy[] Modules(Vessel v) {
            return v==null || !v.loaded ? new ModuleRegionalAutonomy[0] : v.parts.SelectMany(p=>p.Modules.OfType<ModuleRegionalAutonomy>()).ToArray();
        }
        public static ModuleRegionalAutonomy Pilot(Vessel v) { return Modules(v).FirstOrDefault(m=>m.PilotOnline); }
        public static string Text(string value) { return (value??"").Replace('\r',' ').Replace('\n',' ').Substring(0,Math.Min(80,(value??"").Length)); }
        static string Encode(double[] values) { return String.Join("|",values.Select(n=>n.ToString("R",C)).ToArray()); }
        public static string OrbitText(Orbit o) { return Encode(new[]{o.inclination,o.eccentricity,o.semiMajorAxis,o.LAN,o.argumentOfPeriapsis,o.meanAnomalyAtEpoch,o.epoch,(double)o.referenceBody.flightGlobalsIndex}); }
        public static Orbit ReadOrbit(string value) {
            double[] n=value.Split('|').Select(s=>Double.Parse(s,C)).ToArray();
            if(n.Length!=8 || n.Any(x=>!PersistentFlight.BurnMath.Finite(x)) || n[7]!=(int)n[7])throw new FormatException("Invalid maneuver snapshot");
            var body=FlightGlobals.Bodies.FirstOrDefault(b=>b.flightGlobalsIndex==(int)n[7]);
            if(body==null)throw new InvalidOperationException("Maneuver body unavailable");
            return new Orbit(n[0],n[1],n[2],n[3],n[4],n[5],n[6],body);
        }
        public static string RawVector(Vector3d dv) { return Encode(new[]{dv.x,dv.y,dv.z}); }
        public static Vector3d PreviewRemaining(ModuleRegionalAutonomy pilot) {
            if(!pilot.nodeRequested)return Vector3d.zero;
            var target=ReadOrbit(pilot.nodeTargetOrbit);var v=pilot.vessel;
            if(v.mainBody!=target.referenceBody)throw new InvalidOperationException("Body changed");
            return (target.getOrbitalVelocityAtUT(pilot.nodeUT)-v.orbit.getOrbitalVelocityAtUT(pilot.nodeUT)).xzy;
        }
        public static void ValidateQueue(Vessel v,ModuleRegionalAutonomy pilot) {
            if(v==null || v!=FlightGlobals.ActiveVessel || !v.loaded || v.packed || TimeWarp.CurrentRate!=1)throw new InvalidOperationException("Focus this craft at normal time to queue its next node");
            if(pilot==null || pilot.vessel!=v || !pilot.PilotOnline)throw new InvalidOperationException("Start the mission pilot on an idle kOS CPU first");
            if(Modules(v).Any(m=>m.ActiveRoverRoute))throw new InvalidOperationException("Cancel the rover route before queuing a maneuver on this craft");
            if(Modules(v).Any(m=>m.QueueCount>0))throw new InvalidOperationException("A maneuver is already queued");
            if(v.LandedOrSplashed || v.altitude<v.mainBody.atmosphereDepth+1000)throw new InvalidOperationException("Queue maneuvers above the atmosphere");
            if(PersistentFlight.PersistentFlightScenario.Instance!=null) {
                var burn=PersistentFlight.PersistentFlightScenario.Instance.Find(v.id);
                if(burn!=null&&!burn.Finished)throw new InvalidOperationException("Cancel the existing background burn first");
            }
            if(v.patchedConicSolver==null)throw new InvalidOperationException("This craft has no maneuver solver");
            foreach(var m in Modules(v))CleanupCompleted(m);
            v.patchedConicSolver.UpdateFlightPlan();
        }
        public static void QueueNext(Vessel v,ModuleRegionalAutonomy pilot) {
            ValidateQueue(v,pilot);
            var node=v.patchedConicSolver.maneuverNodes.OrderBy(n=>n.UT).FirstOrDefault();
            if(node==null || node.nextPatch==null)throw new InvalidOperationException("Create a maneuver node in map view first");
            if(node.UT<=Planetarium.GetUniversalTime()+5)throw new InvalidOperationException("Move the maneuver at least five seconds into the future");
            if(node.nextPatch.referenceBody!=v.mainBody)throw new InvalidOperationException("This executor requires the burn to stay in the current body's reference frame");
            var vector=node.GetBurnVector(v.orbit);
            if(!PersistentFlight.BurnMath.Finite(vector.magnitude) || vector.magnitude<.5 || vector.magnitude>10000)throw new InvalidOperationException("Use a maneuver between 0.5 and 10000 m/s");
            MissionQueue.ConfigureRecovery(pilot);pilot.missionHold=false;
            pilot.nodeTargetOrbit=OrbitText(node.nextPatch);pilot.nodeRawDV=RawVector(node.DeltaV);
            MissionIntegrity.Capture(pilot);pilot.nodeUT=node.UT;pilot.nodeInitialDV=vector.magnitude;pilot.nodeRequested=true;pilot.nodeStatus="Queued";FlightDebriefRecorder.Queued(pilot);
            pilot.Arm(true);FleetRecovery.Before("Maneuver queued: "+v.vesselName);
        }
        public static Vector3d Remaining(ModuleRegionalAutonomy pilot) {
            if(!pilot.nodeRequested||!MissionIntegrity.Check(pilot)||pilot.missionHold)return Vector3d.zero;
            try {
                var v=pilot.vessel;var target=ReadOrbit(pilot.nodeTargetOrbit);
                if(v.mainBody!=target.referenceBody)throw new InvalidOperationException("Body changed; maneuver stopped");
                if(TimeWarp.CurrentRate!=1)throw new InvalidOperationException("Time warp changed; maneuver stopped");
                if(v==FlightGlobals.ActiveVessel && v.patchedConicSolver!=null) {
                    var node=v.patchedConicSolver.maneuverNodes.FirstOrDefault(n=>Math.Abs(n.UT-pilot.nodeUT)<.001);
                    if(node!=null && RawVector(node.DeltaV)!=pilot.nodeRawDV)throw new InvalidOperationException("Node edited; cancel and queue it again");
                }
                var dv=(target.getOrbitalVelocityAtUT(pilot.nodeUT)-v.orbit.getOrbitalVelocityAtUT(pilot.nodeUT)).xzy;
                if(!PersistentFlight.BurnMath.Finite(dv.magnitude))throw new InvalidOperationException("Invalid remaining burn vector");
                return dv;
            } catch(Exception e) { if(pilot.soiMission)SoiFlightPlan.Hold(pilot,Text(e.Message));else pilot.CancelNode(Text(e.Message)); return Vector3d.zero; }
        }
        public static void CleanupCompleted(ModuleRegionalAutonomy pilot) {
            MissionQueue.Cleanup(pilot);
            var v=pilot.vessel;if(!pilot.completedNodePending||v==null||v.patchedConicSolver==null)return;
            var node=v.patchedConicSolver.maneuverNodes.FirstOrDefault(n=>Math.Abs(n.UT-pilot.completedNodeUT)<.001&&RawVector(n.DeltaV)==pilot.completedNodeRawDV);
            if(node!=null)node.RemoveSelf();
            // Keep the marker: native focus changes can reconstruct nodes from
            // a cached proto-vessel after the live solver has been discarded.
        }
        public static void Finish(ModuleRegionalAutonomy pilot,bool success) {
            if(pilot.missionHold){pilot.nodeStatus="Mission paused; request retained";return;}
            if(success && pilot.nodeRequested) {
                FlightDebriefRecorder.End(pilot,"Complete");
                pilot.completedNodePending=true;pilot.completedNodeUT=pilot.nodeUT;pilot.completedNodeRawDV=pilot.nodeRawDV;
                CleanupCompleted(pilot);
                MissionQueue.Completed(pilot);
                pilot.nodeRequested=false;pilot.nodeStatus="Complete";pilot.warpReady=false;
                FleetEventDirector.Emit(pilot,"MANEUVER_COMPLETED",pilot.nodeInitialDV.ToString("F2",C)+" m/s planned");
                MissionQueue.Advance(pilot);
                if(pilot.QueueCount==0&&pilot.soiMission){pilot.soiMission=false;pilot.soiStatus="SOI plan complete";FleetEventDirector.Emit(pilot,"SOI_PLAN_COMPLETED",pilot.soiStatus);}
            } else if(pilot.nodeRequested)pilot.CancelNode("Stopped; see pilot terminal");
        }
        public static void SetSite(ModuleRegionalAutonomy m,string name,CelestialBody body,double lat,double lon) {
            if(body==null || !PersistentFlight.BurnMath.Finite(lat) || !PersistentFlight.BurnMath.Finite(lon) || lat<-90 || lat>90 || lon<-180 || lon>180)throw new ArgumentException("Use latitude -90 to 90 and longitude -180 to 180");
            m.landingSite=Text(name);m.landingBody=body.bodyName;m.landingLatitude=lat;m.landingLongitude=lon;m.landingVessel="";
            if(body.ocean&&body.TerrainAltitude(lat,lon,false)<1)m.landingTakeover=true;
        }
        public static void SetTargetSite(ModuleRegionalAutonomy m,Vessel target) {
            if(target==null || target==m.vessel || target.mainBody!=m.vessel.mainBody || !target.LandedOrSplashed)throw new InvalidOperationException("Select a landed target vessel on the same body");
            SetSite(m,target.vesselName,target.mainBody,target.latitude,target.longitude);m.landingVessel=target.id.ToString();m.landingTakeover=true;
        }
        public static Vector2d Site(ModuleRegionalAutonomy m) {
            if(!String.IsNullOrEmpty(m.landingVessel)) {
                Guid id;if(!Guid.TryParse(m.landingVessel,out id))throw new InvalidOperationException("Landing target identity is invalid");
                var target=FlightGlobals.Vessels.FirstOrDefault(v=>v!=null&&v.id==id);
                if(target==null || target.mainBody.bodyName!=m.landingBody || !target.LandedOrSplashed)throw new InvalidOperationException("Landing target is unavailable");
                return new Vector2d(target.latitude,target.longitude);
            }
            return new Vector2d(m.landingLatitude,m.landingLongitude);
        }
        public static double Resource(Vessel v,string name,out double capacity) {
            capacity=0;double amount=0;
            if(v.loaded)foreach(var r in v.parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName==name)) { amount+=r.amount;capacity+=r.maxAmount; }
            else if(v.protoVessel!=null)foreach(var r in v.protoVessel.protoPartSnapshots.SelectMany(p=>p.resources).Where(r=>r.resourceName==name)) { amount+=r.amount;capacity+=r.maxAmount; }
            return amount;
        }
    }
}
