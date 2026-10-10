using System;
using System.Diagnostics;

namespace PersistentFlight
{
    public sealed class TimingSnapshot
    {
        public readonly int Samples;
        public readonly double MeanMs, P95Ms, MaximumMs;
        public TimingSnapshot(int samples,double mean,double p95,double maximum) { Samples=samples; MeanMs=mean; P95Ms=p95; MaximumMs=maximum; }
    }
    // Fixed memory; snapshot/sorting happens only when the dashboard refreshes.
    public sealed class TimingWindow
    {
        readonly double[] values=new double[256]; int cursor,count;
        public void Record(long start) { Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency); }
        public void Add(double milliseconds)
        {
            if(double.IsNaN(milliseconds)||double.IsInfinity(milliseconds)||milliseconds<0)return;
            values[cursor]=milliseconds; cursor=(cursor+1)%values.Length; count=Math.Min(count+1,values.Length);
        }
        public TimingSnapshot Snapshot()
        {
            if(count==0)return new TimingSnapshot(0,0,0,0);
            double[] copy=new double[count]; Array.Copy(values,copy,count); Array.Sort(copy);
            double sum=0; foreach(double x in copy)sum+=x;
            return new TimingSnapshot(count,sum/count,copy[Math.Max(0,(int)Math.Ceiling(count*.95)-1)],copy[count-1]);
        }
        public void Clear() { cursor=count=0; }
    }
    public static class FleetPerformance
    {
        public static readonly TimingWindow CoreTick=new TimingWindow(), RangeScan=new TimingWindow(), RegionalTick=new TimingWindow();
        public static readonly TimingWindow Frame=new TimingWindow(),Residency=new TimingWindow(),Capture=new TimingWindow(),Director=new TimingWindow();
        public static long Start() { return Stopwatch.GetTimestamp(); }
        public static void Reset() { CoreTick.Clear(); RangeScan.Clear(); RegionalTick.Clear();Frame.Clear();Residency.Clear();Capture.Clear();Director.Clear(); }
    }
}
