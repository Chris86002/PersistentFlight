using System;
using UnityEngine;

namespace PersistentFlight.Regions
{
    // Sample body-fixed PQS heights into a small tangent mesh, independent of the active camera.
    // This is a sampled collider, not stock PQS streaming, oceans, scatters, or buildings.
    public sealed class TerrainPatch : IDisposable
    {
        public GameObject Root { get; private set; }
        public Mesh Mesh { get; private set; }
        public Vector3d BodyFixedOrigin { get; private set; }
        public Vector3d East { get; private set; }
        public Vector3d Up { get; private set; }
        public Vector3d North { get; private set; }
        public TerrainPatch(PhysicsRegion region,CelestialBody body,double latitude,double longitude,int cells,double spacing)
        {
            if(body==null || body.pqsController==null) throw new ArgumentException("A solid PQS body is required");
            if(!BurnMath.Finite(latitude)||latitude < -90 || latitude > 90 || !BurnMath.Finite(longitude)
                || cells<2||cells>128||cells%2!=0||!BurnMath.Finite(spacing)||spacing<=0||spacing*cells>10000)
                throw new ArgumentOutOfRangeException("terrain patch dimensions");
            Up=body.GetRelSurfaceNVector(latitude,longitude).normalized;
            Vector3d reference=Math.Abs(Up.y)<.99 ? Vector3d.up : new Vector3d(0,0,1);
            East=Vector3d.Cross(reference,Up).normalized; North=Vector3d.Cross(East,Up).normalized;
            double radius=body.pqsController.GetSurfaceHeight(Up);
            if(!BurnMath.Finite(radius)||radius<=0) throw new InvalidOperationException("Invalid PQS surface radius");
            BodyFixedOrigin=Up*radius;
            int width=cells+1;
            Vector3[] vertices=new Vector3[width*width];
            int[] triangles=new int[cells*cells*6];
            for(int z=0;z<width;z++) for(int x=0;x<width;x++)
            {
                Vector3d direction=(BodyFixedOrigin+East*((x-cells/2)*spacing)+North*((z-cells/2)*spacing)).normalized;
                double height=body.pqsController.GetSurfaceHeight(direction);
                if(!BurnMath.Finite(height)||height<=0) throw new InvalidOperationException("Invalid PQS sample");
                Vector3d delta=direction*height-BodyFixedOrigin;
                vertices[z*width+x]=new Vector3((float)Vector3d.Dot(delta,East),(float)Vector3d.Dot(delta,Up),(float)Vector3d.Dot(delta,North));
            }
            int t=0;
            for(int z=0;z<cells;z++) for(int x=0;x<cells;x++)
            {
                int i=z*width+x;
                triangles[t++]=i; triangles[t++]=i+width; triangles[t++]=i+1;
                triangles[t++]=i+1; triangles[t++]=i+width; triangles[t++]=i+width+1;
            }
            Mesh=new Mesh(); Mesh.name="PF sampled "+body.bodyName+" terrain"; Mesh.vertices=vertices; Mesh.triangles=triangles; Mesh.RecalculateNormals(); Mesh.RecalculateBounds();
            Root=new GameObject(Mesh.name);
            try { region.Add(Root); Root.AddComponent<MeshCollider>().sharedMesh=Mesh; }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if(Root!=null) UnityEngine.Object.Destroy(Root);
            if(Mesh!=null) UnityEngine.Object.Destroy(Mesh);
            Root=null; Mesh=null;
        }
    }
}
