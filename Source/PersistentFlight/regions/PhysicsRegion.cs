using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PersistentFlight.Regions
{
    // Experimental isolated solver ownership. No production vessel is enrolled automatically.
    public sealed class PhysicsRegion : IDisposable
    {
        public Scene Scene { get; private set; }
        public PhysicsScene Solver { get; private set; }
        bool disposed;
        public PhysicsRegion(string name)
        {
            Scene=SceneManager.CreateScene(name,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            Solver=Scene.GetPhysicsScene();
            if(!Solver.IsValid() || Solver==Physics.defaultPhysicsScene) throw new InvalidOperationException("Independent physics scene unavailable");
        }
        public void Add(GameObject root)
        {
            if(disposed) throw new ObjectDisposedException("PhysicsRegion");
            if(root==null || root.transform.parent!=null) throw new ArgumentException("Only complete root objects can enter a region");
            SceneManager.MoveGameObjectToScene(root,Scene);
        }
        public void Step(float seconds)
        {
            if(disposed) throw new ObjectDisposedException("PhysicsRegion");
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0||seconds>.05f)
                throw new ArgumentOutOfRangeException("seconds","Use positive fixed steps of at most 50 milliseconds");
            Solver.Simulate(seconds);
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            if(Scene.IsValid() && Scene.isLoaded) SceneManager.UnloadSceneAsync(Scene);
        }
    }

    // Synchronous test transaction: transfer complete actual KSP part trees, test the solver,
    // then restore their original state before another stock callback can run.
    // This is NOT a persistent kOS/terrain adapter and must not survive across game frames.
    public sealed class VesselProbeLease : IDisposable
    {
        sealed class RootState { public GameObject Root; public Scene Scene; public Vector3 Position; public Quaternion Rotation; }
        sealed class BodyState { public Rigidbody Body; public Vector3 Position,Velocity,AngularVelocity; public Quaternion Rotation; public bool Sleeping; }
        readonly List<RootState> roots=new List<RootState>();
        readonly List<BodyState> bodies=new List<BodyState>();
        public Rigidbody[] Bodies { get { return bodies.Select(b=>b.Body).ToArray(); } }
        public VesselProbeLease(Vessel vessel,PhysicsRegion region)
        {
            if(vessel==null || !vessel.loaded || vessel.packed || vessel==FlightGlobals.ActiveVessel)
                throw new InvalidOperationException("Probe requires an unfocused unpacked test vessel");
            GameObject[] trees=vessel.parts.Select(p=>p.transform.root.gameObject).Concat(new[]{vessel.transform.root.gameObject}).Distinct().ToArray();
            HashSet<Rigidbody> owned=new HashSet<Rigidbody>(trees.SelectMany(t=>t.GetComponentsInChildren<Rigidbody>(true)));
            foreach(GameObject tree in trees)
            {
                if(tree.GetComponentsInChildren<Vessel>(true).Any(v=>v!=vessel) || tree.GetComponentsInChildren<Part>(true).Any(p=>p.vessel!=vessel))
                    throw new InvalidOperationException("Shared scene root cannot be isolated");
                foreach(Joint joint in tree.GetComponentsInChildren<Joint>(true))
                    if(joint.connectedBody!=null && !owned.Contains(joint.connectedBody))
                        throw new InvalidOperationException("Joint crosses region boundary");
                roots.Add(new RootState{Root=tree,Scene=tree.scene,Position=tree.transform.position,Rotation=tree.transform.rotation});
            }
            foreach(Rigidbody body in owned)
                bodies.Add(new BodyState{Body=body,Position=body.position,Rotation=body.rotation,Velocity=body.velocity,AngularVelocity=body.angularVelocity,Sleeping=body.IsSleeping()});
            Vector3 center=vessel.rootPart.rb.position;
            Vector3 speed=vessel.rootPart.rb.velocity;
            try
            {
                foreach(RootState state in roots) { region.Add(state.Root); state.Root.transform.position=state.Position-center; }
                foreach(BodyState state in bodies) { state.Body.position=state.Position-center; state.Body.velocity=state.Velocity-speed; state.Body.WakeUp(); }
                Physics.SyncTransforms();
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            // Roll back the diagnostic impulse and local-coordinate conversion.
            foreach(RootState state in roots)
                if(state.Root!=null) { SceneManager.MoveGameObjectToScene(state.Root,state.Scene); state.Root.transform.position=state.Position; state.Root.transform.rotation=state.Rotation; }
            foreach(BodyState state in bodies)
                if(state.Body!=null) { state.Body.position=state.Position; state.Body.rotation=state.Rotation; state.Body.velocity=state.Velocity; state.Body.angularVelocity=state.AngularVelocity; if(state.Sleeping) state.Body.Sleep(); }
            Physics.SyncTransforms(); roots.Clear(); bodies.Clear();
        }
    }
}
