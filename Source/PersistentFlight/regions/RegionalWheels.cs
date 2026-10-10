using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VehiclePhysics;

namespace PersistentFlight.Regions
{
    // Stock VPP consumes WheelCollider hits in FixedUpdate. Those hits belong to
    // the private physics scene and must be consumed before publishing world poses.
    public sealed class RegionalWheels : IDisposable
    {
        static readonly FieldInfo controllerField=typeof(ModuleWheelBase).GetField("wheel",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        static readonly FieldInfo pauseField=typeof(VehicleBase).GetField("m_paused",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo fixedMethod=typeof(VehicleBase).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo inputMethod=typeof(KSPWheelController).GetMethod("OnUpdate",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        readonly ModuleWheelBase[] modules;
        readonly KSPWheelController[] controllers;
        readonly System.Collections.Generic.HashSet<KSPWheelController> driven=new System.Collections.Generic.HashSet<KSPWheelController>();
        readonly System.Collections.Generic.List<Vector3d> entryContacts=new System.Collections.Generic.List<Vector3d>();
        bool entryAligned;
        public bool Motorized { get { return driven.Count>0; } }
        readonly bool[] originalPause;
        readonly PhysicsRegion region;
        bool disposed,anchored,firstSolve=true;
        WheelSnapshot[] suspended;
        VehicleBase.WheelStateVars[][] suspendedDynamics;
        readonly System.Collections.Generic.List<Vector3> publishedContactPoints=new System.Collections.Generic.List<Vector3>();
        VehicleBehaviour[] suspendedBehaviours; bool[] behaviourEnabled,controllerEnabled;
        sealed class WheelSnapshot
        {
            public VPWheelCollider Wheel; public WheelCollider Old; public bool Enabled;
            public Vector3 Center; public float Radius,Travel,Mass,Damping,ForcePoint,Sprung;
            public JointSpring Spring; public WheelFrictionCurve Forward,Sideways;
            public float RuntimeSpring,RuntimeDamper,RuntimeTravel,Extra;
        }
        double latitude,longitude;
        public int Steps { get; private set; }
        public int GroundedWheels { get; private set; }
        public int ContactSamples { get; private set; }
        public double MaximumHitDistance { get; private set; }
        public float MaximumCompression { get; private set; }
        public int VisualContacts { get; private set; }
        public static int NativeContacts(Vessel vessel)
        {
            int count=0;
            foreach(var module in vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>())) {
                var controller=(KSPWheelController)controllerField.GetValue(module);
                if(controller==null || !controller.initialized) continue;
                foreach(var state in controller.wheelState) {
                    WheelHit hit; var collider=state.wheelCol.GetWheelCollider();
                    if(collider!=null && collider.enabled && collider.GetGroundHit(out hit) && hit.collider!=null &&
                        hit.collider.gameObject.scene.GetPhysicsScene()==Physics.defaultPhysicsScene) count++;
                }
            }
            return count;
        }
        public static bool Ready(Vessel vessel)
        {
            return vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>()).All(m=>{
                var controller=(KSPWheelController)controllerField.GetValue(m);
                return Compatible(controller,m.wheelType==WheelType.LEG);
            });
        }
        static bool Compatible(KSPWheelController controller,bool stowedLeg)
        {
            // KSP initializes a retracted leg's wheel data directly while its
            // VPP behaviour is disabled. VehicleBase.initialized remains false
            // until deployment enables that behaviour. Those stowed legs are
            // valid passengers, not a reason to deny the whole vessel ownership.
            return controller!=null && (controller.initialized ||
                (stowedLeg && !controller.enabled && controller.wheelState!=null && controller.wheelState.Length>0 &&
                 controller.wheelState.All(s=>s!=null && s.wheelCol!=null && s.wheelCol.GetWheelCollider()!=null)));
        }
        public static void ResumeFocused(Vessel vessel)
        {
            foreach(var module in vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>())) {
                var controller=(KSPWheelController)controllerField.GetValue(module);
                if(controller!=null && controller.initialized) pauseField.SetValue(controller,false);
            }
        }
        public Vector3 NativeGroundAdjustment(Vessel vessel)
        {
            if(!vessel.Landed || GroundedWheels<3) return Vector3.zero;
            Vector3 up=(Vector3)(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized;
            float raise=0;
            foreach(var foot in publishedContactPoints) {
                RaycastHit native;
                if(Physics.Raycast(foot+up*20,-up,out native,40,1<<15))
                    raise=Mathf.Max(raise,Vector3.Dot(native.point-foot,up));
            }
            if(raise>5) throw new InvalidOperationException("Native terrain differs by more than five metres; ground focus transfer refused");
            if(raise>.01f) Debug.Log("[PF-FOCUS] Aligning supported legs with native terrain: "+raise+" metres");
            return up*(raise>.01f ? raise+.02f : 0);
        }
        public bool NativeTerrainReady(Vessel vessel)
        {
            if(!vessel.Landed || GroundedWheels<3) return true;
            Vector3 up=(Vector3)(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized;
            int covered=0;
            foreach(var foot in publishedContactPoints) {
                RaycastHit hit;
                if(Physics.Raycast(foot+up*20,-up,out hit,40,1<<15) && Math.Abs(Vector3.Dot(hit.point-foot,up))<5) covered++;
            }
            return covered>=3;
        }
        public void Suspend()
        {
            if(suspended!=null) return;
            suspendedDynamics=controllers.Select(c=>c!=null && c.initialized && driven.Contains(c) ? c.GetWheelStateVars():null).ToArray();
            // Tear down every wheel vehicle BEFORE its chassis changes physics scene.
            suspended=controllers.Where(c=>c!=null).SelectMany(c=>c.wheelState).Where(s=>s.wheelCol!=null && s.wheelCol.GetWheelCollider()!=null).Select(s=> {
                var w=s.wheelCol; var c=w.GetWheelCollider();
                return new WheelSnapshot { Wheel=w,Old=c,Enabled=c.enabled,Center=c.center,Radius=c.radius,
                    Travel=c.suspensionDistance,Mass=c.mass,Damping=c.wheelDampingRate,ForcePoint=c.forceAppPointDistance,
                    Sprung=c.sprungMass,Spring=c.suspensionSpring,Forward=c.forwardFriction,Sideways=c.sidewaysFriction,
                    RuntimeSpring=w.runtimeSpringRate,RuntimeDamper=w.runtimeDamperRate,RuntimeTravel=w.runtimeSuspensionTravel,Extra=w.runtimeExtraDownforce };
            }).ToArray();
            suspendedBehaviours=controllers.Where(c=>c!=null).SelectMany(c=>c.GetComponentsInChildren<VehicleBehaviour>(true)).Distinct().ToArray();
            behaviourEnabled=suspendedBehaviours.Select(b=>b.enabled).ToArray(); controllerEnabled=controllers.Select(c=>c!=null && c.enabled).ToArray();
            // Unregister VPP behaviours while their native colliders still exist.
            foreach(var b in suspendedBehaviours) b.enabled=false;
            foreach(var c in controllers.Where(c=>c!=null)) c.enabled=false;
            foreach(var s in suspended) { s.Old.enabled=false; UnityEngine.Object.DestroyImmediate(s.Old); }
        }
        public void Bind()
        {
            if(suspended==null) return;
            foreach(var s in suspended) if(s.Wheel!=null) s.Wheel.ResetWheelCollider();
            for(int i=0;i<suspendedBehaviours.Length;i++) if(suspendedBehaviours[i]!=null) suspendedBehaviours[i].enabled=behaviourEnabled[i];
            for(int i=0;i<controllers.Length;i++) if(controllers[i]!=null) controllers[i].enabled=controllerEnabled[i];
            for(int i=0;i<controllers.Length;i++) if(controllers[i]!=null && controllers[i].initialized && suspendedDynamics[i]!=null) {
                // Keep wheel angular momentum through a solver handoff. Terrain
                // contact caches belong to the old scene and must be reacquired.
                var vars=suspendedDynamics[i];
                for(int n=0;n<vars.Length;n++) { vars[n].contactDepth=0; vars[n].lastTireForce=Vector2.zero; }
                controllers[i].SetWheelStateVars(vars);
            }
            foreach(var s in suspended) {
                if(s.Wheel==null) continue;
                var c=s.Wheel.GetWheelCollider();
                c.center=s.Center; c.radius=s.Radius; c.suspensionDistance=s.Travel;
                c.mass=s.Mass; c.wheelDampingRate=s.Damping; c.forceAppPointDistance=s.ForcePoint;
                c.suspensionSpring=s.Spring; c.forwardFriction=s.Forward; c.sidewaysFriction=s.Sideways;
                c.sprungMass=s.Sprung; c.ConfigureVehicleSubsteps(1000,1,1); c.enabled=s.Enabled;
                s.Wheel.runtimeSpringRate=s.RuntimeSpring; s.Wheel.runtimeDamperRate=s.RuntimeDamper;
                s.Wheel.runtimeSuspensionTravel=s.RuntimeTravel; s.Wheel.runtimeExtraDownforce=s.Extra;
            }
            suspended=null;
            suspendedDynamics=null;
            foreach(var c in controllers.Where(c=>c!=null)) foreach(var state in c.wheelState) { state.grounded=false; state.hit=new WheelHit(); }
        }
        public RegionalWheels(Vessel vessel,PhysicsRegion region)
        {
            this.region=region;
            modules=vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleWheelBase>()).ToArray();
            if(modules.Any(m=>m.wheelType!=WheelType.LEG && m.wheelType!=WheelType.MOTORIZED)) throw new InvalidOperationException("Regional wheel bridge supports stock landing legs and motorized rover wheels");
            if(controllerField==null || pauseField==null || fixedMethod==null) throw new InvalidOperationException("Stock VPP bridge API differs from this KSP build");
            controllers=modules.Select(m=>(KSPWheelController)controllerField.GetValue(m)).ToArray();
            for(int i=0;i<modules.Length;i++) if(modules[i].wheelType==WheelType.MOTORIZED) driven.Add(controllers[i]);
            if(controllers.Where((w,i)=>!Compatible(w,modules[i].wheelType==WheelType.LEG)).Any() || pauseField==null || fixedMethod==null )
                throw new InvalidOperationException("Stock wheel controllers are not initialized or compatible");
            originalPause=controllers.Select(w=>w.paused).ToArray();
            foreach(var c in controllers) foreach(var state in c.wheelState) {
                WheelHit hit;var wc=state.wheelCol.GetWheelCollider();
                if(wc!=null && wc.GetGroundHit(out hit) && hit.collider!=null && hit.collider.gameObject.scene.GetPhysicsScene()==Physics.defaultPhysicsScene) entryContacts.Add((Vector3d)hit.point);
            }
            Suspend();
            Debug.Log("[PF-WHEELS] Acquire CPU vessel="+vessel.id+" native pause="+String.Join(",",originalPause.Select(p=>p.ToString()).ToArray()));
            // Write the scheduling gate directly: the region owns timing, rather
            // than entering/leaving VPP's interactive pause mode fifty times/sec.
            foreach(var controller in controllers) pauseField.SetValue(controller,true);
        }
        public void ResetFrame() { anchored=false; }
        public Vector3d AlignEntry(Vessel vessel,Vector3d origin)
        {
            if(entryAligned) return Vector3d.zero;
            entryAligned=true;
            if(!Motorized || entryContacts.Count<3) return Vector3d.zero;
            Vector3d up=(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized;
            double raise=0;
            Physics.SyncTransforms();
            foreach(var foot in entryContacts) {
                RaycastHit hit;
                if(region.Solver.Raycast((Vector3)(foot-origin+up*20),(Vector3)(-up),out hit,40,1<<15))
                    raise=Math.Max(raise,Vector3d.Dot(origin+(Vector3d)hit.point-foot,up));
            }
            if(raise>5) throw new InvalidOperationException("Regional terrain differs by more than five metres; rover transfer refused");
            if(raise>.01) Debug.Log("[PF-WHEELS] Aligning native rover contacts with regional terrain: "+raise+" metres");
            return up*(raise>.01 ? raise+.02:0);
        }
        public Vector3d Origin(CelestialBody body,Vector3d rootPosition)
        {
            if(!anchored) { latitude=body.GetLatitude(rootPosition); longitude=body.GetLongitude(rootPosition); anchored=true; }
            return body.GetWorldSurfacePosition(latitude,longitude,0);
        }
        public void BeforeSolve()
        {
            if(firstSolve) { firstSolve=false; Steps++; return; } // Native scheduling may already have run in the transfer tick.
            foreach(var controller in controllers)
            {
                if(controller==null) throw new InvalidOperationException("Wheel controller changed during region ownership");
                // Match native scheduling: disabled stowed legs have no VPP
                // dynamics. Deployment will enable and initialize them normally.
                if(!controller.enabled || !controller.initialized) continue;
                pauseField.SetValue(controller,false);
                try {
                    // VPP normally advances steering in its render Update. That
                    // callback is paused under regional ownership. Consume the
                    // native module inputs once in this deterministic physics tick.
                    if(inputMethod==null) throw new InvalidOperationException("Stock wheel input callback is unavailable");
                    if(driven.Contains(controller)) {
                        inputMethod.Invoke(controller,null);
                        foreach(var state in controller.wheelState) state.wheelCol.steerAngle=state.steerAngle;
                    }
                    fixedMethod.Invoke(controller,null);
                }
                finally { pauseField.SetValue(controller,true); }
            }
            Steps++;
        }
        public void AfterSolve(Vector3d origin)
        {
            GroundedWheels=0;
            publishedContactPoints.Clear();
            foreach(var controller in controllers)
            {
                foreach(var state in controller.wheelState)
                {
                    WheelHit hit;
                    WheelCollider collider=state.wheelCol.GetWheelCollider();
                    if(collider!=null && collider.GetGroundHit(out hit))
                    {
                        if(hit.collider==null || hit.collider.gameObject.scene!=region.Scene)
                            throw new InvalidOperationException("Wheel contact escaped the private scene");
                        double distance=(hit.point-collider.transform.position).magnitude;
                        MaximumHitDistance=Math.Max(MaximumHitDistance,distance);
                        if(distance>10 || double.IsNaN(distance)) throw new InvalidOperationException("Wheel contact coordinates are inconsistent");
                        GroundedWheels++; ContactSamples++;
                        publishedContactPoints.Add((Vector3)(origin+(Vector3d)hit.point));
                    }
                    MaximumCompression=Math.Max(MaximumCompression,state.suspensionCompression);
                    UpdateLegVisual(state.wheelCol);
                    if(driven.Contains(controller)) {
                        var w=state.wheelCol;
                        w.angularVelocity=controller.invertVisualWheelSpinDirection ? -state.angularVelocity : state.angularVelocity;
                        w.angularPosition+=w.angularVelocity*Time.fixedDeltaTime;
                        if(w.updateCaliper && w.caliperTransform!=null) w.caliperTransform.localRotation=Quaternion.Euler(0,state.steerAngle,0);
                        if(w.updateWheel && w.wheelTransform!=null) w.wheelTransform.localRotation=Quaternion.Euler(w.angularPosition*Mathf.Rad2Deg,w.wheelTransform.parent==w.caliperTransform ? 0:state.steerAngle,0);
                    }
                    // ModuleWheelBase's ordinary callbacks need the same contact
                    // in stock coordinates, e.g. damage and native landed detection.
                    if(state.grounded) { WheelHit published=state.hit; published.point=(Vector3)(origin+(Vector3d)published.point); state.hit=published; }
                }
            }
        }
        void UpdateLegVisual(VPWheelCollider wheel)
        {
            if(!wheel.isActiveAndEnabled) return;
            WheelCollider collider=wheel.GetWheelCollider();
            if(collider==null || !collider.enabled) return;
            Transform frame=wheel.cachedTransform;
            float travel=Math.Max(0,wheel.lastRuntimeSuspensionTravel);
            Vector3 start=frame.TransformPoint(collider.center)+frame.up*wheel.radius;
            RaycastHit hit;
            float extension=travel;
            if(region.Solver.Raycast(start,-frame.up,out hit,travel+2*wheel.radius,wheel.layerMask))
            {
                extension=Mathf.Clamp(hit.distance-2*wheel.radius,0,travel);
                VisualContacts++;
            }
            if(wheel.disableSuspensionMovement) return;
            Vector3 point=frame.position-frame.up*(extension+wheel.groundPenetration-wheel.suspensionDistance*(1-wheel.suspensionAnchor)+wheel.suspensionOffset);
            if(wheel.updateSuspension && wheel.suspensionTransform!=null) wheel.suspensionTransform.position=point;
            if(wheel.updateCaliper && wheel.caliperTransform!=null) wheel.caliperTransform.position=point;
            if(wheel.updateWheel && wheel.wheelTransform!=null) wheel.wheelTransform.position=point;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            Bind();
            for(int i=0;i<controllers.Length;i++) if(controllers[i]!=null)
            {
                foreach(var state in controllers[i].wheelState ?? new VehicleBase.WheelState[0])
                {
                    if(state==null) continue;
                    state.grounded=false; state.hit=new WheelHit();
                }
                pauseField.SetValue(controllers[i],originalPause[i]);
            }
        }
    }
}






