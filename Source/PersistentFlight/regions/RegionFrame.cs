using System;
using PersistentFlight;

namespace PersistentFlight.Regions
{
    // An inertial, non-rotating frame. Subtract in double precision BEFORE casting to Unity floats.
    public sealed class RegionFrame
    {
        public DVector Origin { get; private set; }
        public DVector Velocity { get; private set; }
        public double Epoch { get; private set; }
        public RegionFrame(DVector origin, DVector velocity, double epoch)
        {
            Validate(origin); Validate(velocity);
            if (!BurnMath.Finite(epoch)) throw new ArgumentOutOfRangeException("epoch");
            Origin=origin; Velocity=velocity; Epoch=epoch;
        }
        public DVector OriginAt(double time)
        {
            if (!BurnMath.Finite(time)) throw new ArgumentOutOfRangeException("time");
            DVector value=Origin+Velocity*(time-Epoch); Validate(value); return value;
        }
        public DVector ToLocalPosition(DVector world,double time) { Validate(world); return world-OriginAt(time); }
        public DVector ToWorldPosition(DVector local,double time) { Validate(local); return OriginAt(time)+local; }
        public DVector ToLocalVelocity(DVector world) { Validate(world); return world-Velocity; }
        public DVector ToWorldVelocity(DVector local) { Validate(local); return local+Velocity; }
        // Caller translates ALL bodies AND local terrain by the returned shift.
        public DVector Recenter(DVector localCenter,double time)
        {
            Validate(localCenter); Origin=OriginAt(time)+localCenter; Epoch=time; return localCenter*-1;
        }
        static void Validate(DVector value)
        {
            if (!BurnMath.Finite(value.X)||!BurnMath.Finite(value.Y)||!BurnMath.Finite(value.Z))
                throw new ArgumentOutOfRangeException("Nonfinite region coordinate");
        }
    }
}
