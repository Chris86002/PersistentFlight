using System;
using System.Linq;
using UnityEngine;

namespace PersistentFlight.Regions
{
    // The opt-in follows the CPU part across vessel GUID changes at separation.
    public sealed partial class ModuleRegionalAutonomy : PartModule
    {
        [KSPField(isPersistant=true,guiActive=true,guiActiveEditor=true,guiName="Automatic regional physics"), UI_Toggle(enabledText="Armed",disabledText="Off")]
        public bool automatic=false;
        [KSPField(guiActive=true,guiName="Regional autonomy")]
        public string status="Off";
        [KSPField(isPersistant=true,guiActive=true,guiName="Rover route"), UI_Toggle(enabledText="Paused",disabledText="Driving")]
        public bool roverHold=false;
        [KSPField(isPersistant=true)] public string checkpoint64="";
        [KSPField(isPersistant=true)] public double checkpointUT;
        public string Checkpoint
        {
            get { return String.IsNullOrEmpty(checkpoint64) ? "" : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(checkpoint64)); }
            set {
                byte[] bytes=System.Text.Encoding.UTF8.GetBytes(value ?? "");
                if(bytes.Length>65536) throw new ArgumentException("Checkpoint exceeds 64 KiB");
                checkpoint64=Convert.ToBase64String(bytes); checkpointUT=Planetarium.GetUniversalTime();
            }
        }
        internal Vessel observed;
        internal string signature;
        internal float stableSince,retryAfter;
        internal bool leased;
        internal bool requested;
        public void Arm(bool value)
        {
            automatic=value;
            // The core keeps the new stage live while stock finishes separation.
            if(value) foreach(var m in part.Modules.OfType<ModulePersistentFlight>()) { m.armed=true; m.rangeKm=60; }
        }
    }
    public static class RegionalAutonomy
    {
        sealed class StartupHold {
            public Vessel Vessel;
            public Rigidbody[] Bodies;
            public RigidbodyConstraints[] Constraints;
            public void Freeze(){foreach(var rb in Bodies)if(rb!=null)rb.constraints=RigidbodyConstraints.FreezeAll;}
            public void Resume(){for(int i=0;i<Bodies.Length;i++)if(Bodies[i]!=null){
                Bodies[i].constraints=Constraints[i];
                if(!Bodies[i].isKinematic){
                    Bodies[i].velocity=(Vector3)((FlightGlobals.RefFrameIsRotating ? Vector3d.zero : Vessel.mainBody.getRFrmVel(Bodies[i].position))-Krakensbane.GetFrameVelocity());
                    Bodies[i].angularVelocity=Vector3.zero;
                }
            }}
        }
        static readonly System.Collections.Generic.Dictionary<Vessel,StartupHold> startupHolds=new System.Collections.Generic.Dictionary<Vessel,StartupHold>();
        public static void ReleaseStartupHolds()
        {
            foreach(var vessel in startupHolds.Keys.ToArray()) if(vessel!=null && vessel.loaded) { if(!vessel.packed)vessel.GoOnRails();startupHolds[vessel].Resume(); }
            startupHolds.Clear();
        }
        static void HoldStartup(Vessel vessel)
        {
            if(startupHolds.ContainsKey(vessel))return;
            var bodies=vessel.parts.SelectMany(p=>p.GetComponentsInChildren<Rigidbody>()).Distinct().ToArray();
            // Keep the chassis dynamic so native wheels calculate their sprung
            // mass normally; constrain motion only until initialization finishes.
            var hold=new StartupHold{Vessel=vessel,Bodies=bodies,Constraints=bodies.Select(r=>r.constraints).ToArray()};
            startupHolds.Add(vessel,hold);hold.Freeze();
        }
        static bool UnpackRegionalRover(Vessel vessel)
        {
            if(!vessel.packed || !vessel.Landed || vessel.Splashed || vessel==FlightGlobals.ActiveVessel ||
                TimeWarp.CurrentRate!=1 || vessel.mainBody.pqsController==null ||
                !vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>()).Any(w=>w.wheelType==WheelType.MOTORIZED) ||
                vessel.vesselRanges.landed.unpack<1998000 ||
                Vector3d.Distance(vessel.GetWorldPos3D(),FlightGlobals.ActiveVessel.GetWorldPos3D())>=vessel.vesselRanges.landed.unpack) return false;
            // Stock rejects a saved landed craft if its old PQS detail levels
            // differ from the current camera and no default-scene ground exists.
            // This opted-in rover will use its private sampled terrain instead.
            int min=vessel.PQSminLevel,max=vessel.PQSmaxLevel;
            bool skip=vessel.skipGroundPositioning;
            bool landed=vessel.Landed;
            bool inertialGround=landed && !FlightGlobals.RefFrameIsRotating;
            try {
                vessel.skipGroundPositioning=true;
                vessel.PQSminLevel=vessel.mainBody.pqsController.minLevel;
                vessel.PQSmaxLevel=vessel.mainBody.pqsController.maxLevel;
                // GoOffRails refuses every landed craft in a nonrotating
                // frame, even after the native startup hold has expired. The
                // active orbiter uses that frame; our ground lives elsewhere.
                // Scope this bypass to this rover, then restore its surface
                // state and frame velocity before any physics can run.
                if(inertialGround) vessel.Landed=false;
                vessel.GoOffRails();
                vessel.Landed=landed;
                if(inertialGround && !vessel.packed) {
                    Vector3d groundVelocity=vessel.mainBody.getRFrmVel(vessel.GetWorldPos3D())-Krakensbane.GetFrameVelocity();
                    foreach(var rb in vessel.parts.SelectMany(p=>p.GetComponentsInChildren<Rigidbody>()).Distinct()) {
                        rb.velocity=(Vector3)groundVelocity;
                        rb.angularVelocity=Vector3.zero;
                    }
                    Debug.Log("[PF-AUTO] Cold rover pose: altitude="+vessel.altitude+" native terrain="+vessel.terrainAltitude+" root altitude="+vessel.mainBody.GetAltitude(vessel.rootPart.rb.position)+" sample="+vessel.mainBody.TerrainAltitude(vessel.latitude,vessel.longitude,false)+" landed="+vessel.Landed);
                }
                if(!vessel.packed)Debug.Log("[PF-AUTO] Native unpack for regional rover terrain: "+vessel.vesselName);
                if(!vessel.packed)HoldStartup(vessel);
                return !vessel.packed;
            }
            finally { vessel.Landed=landed;vessel.PQSminLevel=min;vessel.PQSmaxLevel=max;vessel.skipGroundPositioning=skip; }
        }
        public static void Reconcile(RegionFlightRunner runner)
        {
            if(FlightGlobals.fetch==null || !FlightGlobals.ready) return;
            foreach(var vessel in startupHolds.Keys.ToArray()) {
                if(vessel==null || !vessel.loaded){startupHolds.Remove(vessel);continue;}
                if(TimeWarp.CurrentRate!=1 || vessel==FlightGlobals.ActiveVessel || !vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleRegionalAutonomy>()).Any(m=>m.automatic)) {
                    vessel.GoOnRails();startupHolds[vessel].Resume();startupHolds.Remove(vessel);
                }
            }
            foreach(var region in runner.Regions.ToArray())
                if(!region.MembershipValid || (region.Vessel==FlightGlobals.ActiveVessel && !region.FocusedTerrainBridge) || TimeWarp.CurrentRate!=1)
                    runner.Release(region);
            foreach(Vessel vessel in FlightGlobals.Vessels.ToArray())
            {
                if(vessel==null || !vessel.loaded) continue;
                var modules=vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleRegionalAutonomy>()).ToArray();
                var own=runner.Regions.FirstOrDefault(r=>r.Vessel==vessel);
                if(own!=null && modules.Any(m=>m.leased) && !modules.Any(m=>m.automatic)) { runner.Release(own); own=null; }
                string signature=String.Join(",",vessel.parts.Select(p=>p.flightID).OrderBy(x=>x).Select(x=>x.ToString()).ToArray());
                foreach(var module in modules)
                {
                    if(module.observed!=vessel || module.signature!=signature)
                    { module.observed=vessel; module.signature=signature; module.stableSince=Time.realtimeSinceStartup; }
                    if(module.requested!=module.automatic)
                    { module.requested=module.automatic; module.stableSince=Time.realtimeSinceStartup; }
                    if(!module.automatic) { module.leased=false; module.status="Off"; continue; }
                    module.Arm(true);
                    if(own!=null) { module.leased=true; module.status="Running"; continue; }
                    module.leased=false;
                    if(vessel==FlightGlobals.ActiveVessel) { module.status="Armed; focused"; continue; }
                    if((startupHolds.ContainsKey(vessel) || (vessel.packed && Time.realtimeSinceStartup-module.stableSince>=.25f && UnpackRegionalRover(vessel)))) {
                        // Finish ownership in this same post-stock tick so no
                        // default physics step runs without the regional ground.
                        if(!RegionalWheels.Ready(vessel)){module.status="Waiting for wheel startup";continue;}
                        var hold=startupHolds[vessel];
                        try { hold.Resume();own=runner.Enroll(vessel,true);startupHolds.Remove(vessel);module.leased=true;module.status="Running"; }
                        catch(Exception e) { hold.Freeze();module.status="Waiting: "+e.Message;Debug.LogWarning("[PF-AUTO] "+module.status); }
                        continue;
                    }
                    if(vessel.packed || vessel.Splashed || TimeWarp.CurrentRate!=1) { module.status="Waiting for live physics"; continue; }
                    // Give the core's 0.2 s scanner time to record its baseline
                    // before the region expands the vessel's own range object.
                    if(Time.realtimeSinceStartup-module.stableSince<.25f || Time.realtimeSinceStartup<module.retryAfter)
                    { module.status="Waiting for separation"; continue; }
                    // The core prepares load bounds, then physics bounds on its
                    // following scan. Capture the completed bounds for later release.
                    if(module.part.Modules.OfType<ModulePersistentFlight>().Any() &&
                        (vessel.vesselRanges.flying.pack<61000 || vessel.vesselRanges.flying.unpack<60000))
                    { module.status="Waiting for keepalive bounds"; continue; }
                    try
                    {
                        own=runner.Enroll(vessel,true); module.leased=true; module.status="Running";
                        Debug.Log("[PF-AUTO] Enrolled "+vessel.vesselName+" CPU="+module.part.flightID+" parts="+vessel.parts.Count);
                    }
                    catch(Exception e)
                    {
                        module.status="Waiting: "+e.Message; module.retryAfter=Time.realtimeSinceStartup+2;
                        Debug.LogWarning("[PF-AUTO] "+module.status);
                    }
                }
            }
        }
    }
}
