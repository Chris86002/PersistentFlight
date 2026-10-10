using System;
using PersistentFlight;
using PersistentFlight.Regions;
class RegionFrameTests
{
    static int count;
    static void Check(bool ok,string label) { count++; if(!ok) throw new Exception(label); }
    static void Main()
    {
        var frame=new RegionFrame(new DVector(1e11,-1e11,2e11),new DVector(30000,-2000,1),100);
        var point=new DVector(1e11+.125,-1e11+.25,2e11+.5);
        var local=frame.ToLocalPosition(point,100);
        Check(local.X==.125 && local.Y==.25 && local.Z==.5,"submeter coordinates at interplanetary scale");
        Check((frame.ToWorldPosition(local,100)-point).Length==0,"world/local roundtrip");
        Check((frame.ToLocalVelocity(new DVector(30000.125,-1999.5,2))-new DVector(.125,.5,1)).Length==0,"velocity frame subtraction");
        Check((frame.ToWorldVelocity(new DVector(.125,.5,1))-new DVector(30000.125,-1999.5,2)).Length==0,"velocity roundtrip");
        var worldBefore=frame.ToWorldPosition(local,102);
        var shift=frame.Recenter(new DVector(10,20,30),102);
        Check((frame.ToWorldPosition(local+shift,102)-worldBefore).Length==0,"recenter preserves absolute position");
        Check((frame.OriginAt(103)-frame.OriginAt(102)-frame.Velocity).Length==0,"moving frame remains consistent");
        bool rejected=false; try { frame.ToLocalPosition(new DVector(double.NaN,0,0),102); } catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"nonfinite input rejected");
        rejected=false; try { frame.OriginAt(double.PositiveInfinity); } catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"invalid epoch rejected");
        Console.WriteLine("PASS "+count+" region-frame checks");
    }
}
