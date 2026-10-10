using System;

namespace PersistentFlight
{
    // All orbital state stays in double precision; no Unity types cross this boundary.
    public struct DVector
    {
        public double X, Y, Z;
        public DVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public DVector Unit { get { double n = Length; if (n < 1e-12) throw new InvalidOperationException("Zero direction vector"); return this / n; } }
        public static DVector operator +(DVector a, DVector b) { return new DVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static DVector operator -(DVector a, DVector b) { return new DVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static DVector operator *(DVector a, double b) { return new DVector(a.X * b, a.Y * b, a.Z * b); }
        public static DVector operator /(DVector a, double b) { return a * (1 / b); }
        public static DVector Cross(DVector a, DVector b) { return new DVector(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X); }
    }

    public enum BurnDirection { PROGRADE, RETROGRADE, NORMAL, ANTINORMAL, RADIALOUT, RADIALIN }
    public struct OrbitalState
    {
        public DVector Position, Velocity;
        public OrbitalState(DVector position, DVector velocity) { Position = position; Velocity = velocity; }
    }

    public static class BurnMath
    {
        public const double G0 = 9.80665;
        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static DVector Direction(OrbitalState s, BurnDirection direction)
        {
            switch (direction)
            {
                case BurnDirection.PROGRADE: return s.Velocity.Unit;
                case BurnDirection.RETROGRADE: return s.Velocity.Unit * -1;
                case BurnDirection.NORMAL: return DVector.Cross(s.Position, s.Velocity).Unit;
                case BurnDirection.ANTINORMAL: return DVector.Cross(s.Position, s.Velocity).Unit * -1;
                case BurnDirection.RADIALOUT: return s.Position.Unit;
                case BurnDirection.RADIALIN: return s.Position.Unit * -1;
                default: throw new ArgumentOutOfRangeException("direction");
            }
        }
        private static DVector Acceleration(OrbitalState s, double mu, double thrust, double mass, BurnDirection direction)
        {
            double r = s.Position.Length;
            return s.Position * (-mu / (r*r*r)) + Direction(s, direction) * (thrust / mass);
        }
        // Fourth-order integration includes gravity during the finite burn, and mass loss.
        public static OrbitalState Step(OrbitalState s, double mu, double thrust, double mass, double massFlow, double dt, BurnDirection direction)
        {
            if (!(dt > 0) || !(mass > massFlow * dt) || !(mu > 0) || !(thrust >= 0) || !(massFlow >= 0)
                || !Finite(dt) || !Finite(mu) || !Finite(thrust) || !Finite(mass) || !Finite(massFlow))
                throw new ArgumentOutOfRangeException("Invalid integration input");
            DVector k1r = s.Velocity, k1v = Acceleration(s, mu, thrust, mass, direction);
            OrbitalState b = new OrbitalState(s.Position + k1r*(dt/2), s.Velocity + k1v*(dt/2));
            DVector k2r = b.Velocity, k2v = Acceleration(b, mu, thrust, mass-massFlow*dt/2, direction);
            OrbitalState c = new OrbitalState(s.Position + k2r*(dt/2), s.Velocity + k2v*(dt/2));
            DVector k3r = c.Velocity, k3v = Acceleration(c, mu, thrust, mass-massFlow*dt/2, direction);
            OrbitalState d = new OrbitalState(s.Position + k3r*dt, s.Velocity + k3v*dt);
            DVector k4r = d.Velocity, k4v = Acceleration(d, mu, thrust, mass-massFlow*dt, direction);
            OrbitalState result = new OrbitalState(s.Position + (k1r+k2r*2+k3r*2+k4r)*(dt/6), s.Velocity + (k1v+k2v*2+k3v*2+k4v)*(dt/6));
            if (!Finite(result.Position.Length) || !Finite(result.Velocity.Length)) throw new InvalidOperationException("Nonfinite orbital state");
            return result;
        }
        public static double DeltaV(double thrust, double massFlow, double mass, double dt)
        {
            return massFlow > 0 ? thrust/massFlow * Math.Log(mass/(mass-massFlow*dt)) : thrust/mass*dt;
        }
        public static double TimeForDeltaV(double thrust, double massFlow, double mass, double deltaV)
        {
            if (thrust <= 0 || mass <= 0 || deltaV < 0) throw new ArgumentOutOfRangeException("Invalid burn");
            return massFlow > 0 ? mass/massFlow*(1-Math.Exp(-deltaV*massFlow/thrust)) : deltaV*mass/thrust;
        }
    }
}
