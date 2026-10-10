using System;
using UnityEngine;

namespace PersistentFlight.Regions
{
    public sealed class GroundContactRecorder : MonoBehaviour
    {
        public int Contacts;
        public float MaximumImpactSpeed;
        public void OnCollisionEnter(Collision c) { Contacts++; MaximumImpactSpeed=Mathf.Max(MaximumImpactSpeed,c.relativeVelocity.magnitude); }
        public void OnCollisionStay(Collision c) { Contacts++; }
    }
    // The terrain patch lives in the same private solver as the actual vessel.
    public sealed class RegionalSurface : IDisposable
    {
        readonly PhysicsRegion region;
        readonly CelestialBody body;
        TerrainPatch patch;
        Rigidbody ground;
        Vector3d worldPoint,up;
        Quaternion worldRotation;
        Vector3d solverOrigin,frameVelocity;
        bool inSolver;
        public int Rebuilds { get; private set; }
        public int Contacts { get { return patch==null ? 0 : patch.Root.GetComponent<GroundContactRecorder>().Contacts; } }
        public float MaximumImpactSpeed { get { return patch==null ? 0 : patch.Root.GetComponent<GroundContactRecorder>().MaximumImpactSpeed; } }
        readonly bool rover;
        public RegionalSurface(PhysicsRegion region,CelestialBody body,bool rover=false) { this.region=region; this.body=body; this.rover=rover; }
        public void Prepare(Vessel vessel,Vector3d solverOrigin,float dt,Vector3d frameVelocity,Vector3d terrainCenter)
        {
            this.solverOrigin=solverOrigin+frameVelocity*dt; this.frameVelocity=frameVelocity;
            // The wheel solver's frame anchor stays fixed. Its terrain tile must
            // follow the craft as a boostback carries it across that frame.
            Vector3d at=terrainCenter;
            // Preserve the established surface anchor for nearby/stationary legs.
            // At distant float publication coordinates, re-deriving its latitude
            // from a raised root introduces avoidable terrain-frame differences.
            Vector3d anchorNormal=(solverOrigin-body.position).normalized;
            Vector3d anchorOffset=terrainCenter-solverOrigin;
            if(patch==null && (anchorOffset-anchorNormal*Vector3d.Dot(anchorOffset,anchorNormal)).magnitude<=180)
                at=solverOrigin;
            double lat=body.GetLatitude(at),lon=body.GetLongitude(at);
            double height=body.TerrainAltitude(lat,lon,false);
            if(body.ocean && height<1) throw new InvalidOperationException("Regional ocean contact is not implemented");
            if(patch!=null)
            {
                Vector3d center=body.GetWorldSurfacePosition(patchLatitude,patchLongitude,0);
                Vector3d offset=terrainCenter-center;
                Vector3d normal=(center-body.position).normalized;
                if((offset-normal*Vector3d.Dot(offset,normal)).magnitude>180) DisposePatch();
            }
            if(patch==null)
            {
                patchLatitude=lat; patchLongitude=lon;
                patch=new TerrainPatch(region,body,lat,lon,rover ? 64:32,rover ? 10:20);
                patch.Root.layer=15; // Stock KSP Terrain layer: Part's native landed collision checks.
                patch.Root.AddComponent<GroundContactRecorder>();
                ground=patch.Root.AddComponent<Rigidbody>(); ground.isKinematic=true; ground.useGravity=false;
                var material=new PhysicMaterial("PF terrain contact"); material.dynamicFriction=.8f; material.staticFriction=.8f; material.bounciness=0;
                patch.Root.GetComponent<MeshCollider>().sharedMaterial=material;
                Rebuilds++;
            }
            double surfaceHeight=patch.BodyFixedOrigin.magnitude-body.Radius;
            worldPoint=body.GetWorldSurfacePosition(patchLatitude,patchLongitude,surfaceHeight);
            // Use the same celestial basis as GetWorldSurfacePosition across frame changes.
            up=body.BodyFrame.LocalToWorld(patch.Up.xzy).xzy;
            Vector3 north=(Vector3)body.BodyFrame.LocalToWorld(patch.North.xzy).xzy;
            worldRotation=Quaternion.LookRotation(north,(Vector3)up);
            ground.rotation=worldRotation;
            ground.position=(Vector3)(worldPoint-solverOrigin);
            // In KSP's rotating frame the surface is stationary before Krakensbane subtraction.
            // In an inertial frame its rotation contributes a physical surface velocity.
            Vector3d speed=(FlightGlobals.RefFrameIsRotating ? Vector3d.zero : body.getRFrmVel(worldPoint))-Krakensbane.GetFrameVelocity();
            speed-=frameVelocity;
            ground.velocity=(Vector3)speed;
            ground.MovePosition((Vector3)(worldPoint-solverOrigin+speed*dt));
            inSolver=true;
        }
        double patchLatitude,patchLongitude;
        public void Publish()
        {
            if(ground!=null && inSolver) { ground.position=(Vector3)(solverOrigin+(Vector3d)ground.position); ground.rotation=worldRotation; ground.velocity=(Vector3)(frameVelocity+(Vector3d)ground.velocity); inSolver=false; }
        }
        public double Height(Vessel vessel)
        {
            Vector3d position=vessel.rootPart.rb.position;
            Vector3d normal=(position-body.position).normalized;
            RaycastHit hit;
            if(patch!=null && region.Solver.Raycast((Vector3)position,(Vector3)(normal*-1),out hit,10000,1<<15)) return hit.distance;
            double lat=body.GetLatitude(position),lon=body.GetLongitude(position);
            return (position-body.position).magnitude-body.Radius-body.TerrainAltitude(lat,lon,false);
        }
        void DisposePatch()
        {
            if(patch!=null)
            {
                if(patch.Root!=null)
                {
                    var collider=patch.Root.GetComponent<MeshCollider>();
                    if(collider!=null) { var material=collider.sharedMaterial; collider.enabled=false; if(material!=null) UnityEngine.Object.Destroy(material); }
                }
                patch.Dispose();
            }
            patch=null; ground=null;
        }
        public void Dispose() { DisposePatch(); }
    }
}



