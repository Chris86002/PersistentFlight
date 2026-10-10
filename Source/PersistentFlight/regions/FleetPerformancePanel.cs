using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PersistentFlight.Regions
{
    public sealed partial class RegionalFleetPanel
    {
        float nextTimingRefresh; string timingText="Waiting for timing samples";
        static string TimingLine(string name,TimingWindow window)
        {
            var s=window.Snapshot(); return name+": mean "+s.MeanMs.ToString("F2")+" ms, p95 "+s.P95Ms.ToString("F2")+" ms, max "+s.MaximumMs.ToString("F2")+" ms ("+s.Samples+" ticks)";
        }
        string PerformanceText()
        {
            var service=PersistentFlightScenario.Instance; var runner=RegionFlightRunner.Instance;
            var text=new StringBuilder();
            text.AppendLine("Pending distant burns: "+(service==null?0:service.ScheduledCount)+"; due/running: "+(service==null?0:service.ActiveCount)+"; live regions: "+(runner==null?0:runner.Regions.Count));
            text.AppendLine(TimingLine("Core incl. range scans",FleetPerformance.CoreTick));
            text.AppendLine(TimingLine("Regional reconciliation + physics",FleetPerformance.RegionalTick));
            text.AppendLine(TimingLine("Range scan (included in core)",FleetPerformance.RangeScan));
            text.AppendLine(TimingLine("Observed whole-game frame",FleetPerformance.Frame));
            text.AppendLine(TimingLine("Fleet residency (per frame)",FleetPerformance.Residency));
            text.AppendLine(TimingLine("Telemetry capture (per capture)",FleetPerformance.Capture));
            text.AppendLine(TimingLine("Safety/warp director (per frame)",FleetPerformance.Director));
            text.AppendLine("Longest dispatch delay: "+(service==null?0:service.MaximumDispatchDelay).ToString("F3")+" game seconds");
            text.AppendLine("Managed memory: "+(GC.GetTotalMemory(false)/1048576.0).ToString("F1")+" MB; GC collections: "+GC.CollectionCount(0)+" / "+GC.CollectionCount(1)+" / "+GC.CollectionCount(2));
            text.Append("Frame timing includes the whole game; PF scopes overlap and must not be added. Memory and GC are process-wide.");return text.ToString();
        }
        void DrawPerformance()
        {
            if(Time.realtimeSinceStartup>=nextTimingRefresh){timingText=PerformanceText();nextTimingRefresh=Time.realtimeSinceStartup+1;}
            GUILayout.Label(timingText);
            if(GUILayout.Button("Export performance report"))Try(()=>{
                string folder=Path.Combine(KSPUtil.ApplicationRootPath,"PluginData/PersistentFlight/Performance");Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,"fleet-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt");
                File.WriteAllText(path,"PersistentFlight fleet timing\nUTC: "+DateTime.UtcNow.ToString("o")+"\nUT: "+Planetarium.GetUniversalTime().ToString("R",C)+"\nSave: "+HighLogic.SaveFolder+"\n"+PerformanceText());
                GUIUtility.systemCopyBuffer=path;
            },"Performance report saved; path copied.");
        }
    }
}
