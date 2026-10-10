using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace PersistentFlight
{
    public static class FleetCapacityApi
    {
        public const int Version=1;
        public const int MaximumRecords=CapacityLedger.RecordLimit;
        public const string Capabilities="ReservationLedgerOnly;PersistentOwnerRecords;RevisionChecks;IntervalUsage;NoAdmissionControl";
        static PersistentFlightScenario Service()
        {
            var s=PersistentFlightScenario.Instance;
            if(s==null)throw new InvalidOperationException("PersistentFlight is not ready in this save");
            s.CheckCapacityThread();return s;
        }
        public static bool IsHeld { get { return Service().CapacityHeld; } }
        public static int RecordCount { get { return Service().Capacity().Count; } }
        public static FleetCapacityReservation Reserve(FleetCapacityRequest r) { return Service().Capacity().Reserve(r); }
        public static FleetCapacityReservation Get(Guid id) { return Service().Capacity().Get(id); }
        public static FleetCapacityReservation Revise(FleetCapacityRequest r,int expectedRevision) { return Service().Capacity().Revise(r,expectedRevision); }
        public static FleetCapacityReservation Release(Guid id,string owner,int expectedRevision) { return Service().Capacity().Release(id,owner,expectedRevision); }
        public static FleetCapacityReservation[] List(string owner,Guid missionId,double startUT,double endUT,bool includeReleased)
        { return Service().Capacity().List(owner,missionId,startUT,endUT,includeReleased); }
        public static FleetCapacityUsage UsageAt(double ut) { return Service().Capacity().UsageAt(ut); }
        // Independent maxima per dimension, not necessarily one simultaneous fleet configuration.
        public static FleetCapacityUsage Peak(double startUT,double endUT) { return Service().Capacity().Peak(startUT,endUT); }
    }
    public partial class PersistentFlightScenario
    {
        CapacityLedger capacityLedger=new CapacityLedger();
        bool capacityHeld;
        readonly List<ConfigNode> capacitySavedNodes=new List<ConfigNode>();
        internal bool CapacityHeld { get { return capacityHeld; } }
        internal void CheckCapacityThread()
        { if(missionThread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Call capacity reservations on KSP's main thread"); }
        internal CapacityLedger Capacity()
        { if(capacityHeld)throw new InvalidOperationException("Saved capacity reservations could not be restored; restore a verified complete save");return capacityLedger; }
        static readonly CultureInfo CapacityCulture=CultureInfo.InvariantCulture;
        static FleetCapacityRequest ReadCapacityRequest(ConfigNode n)
        {
            if(n==null)throw new FormatException("Missing capacity request");
            var r=new FleetCapacityRequest(Guid.Parse(n.GetValue("id")),n.GetValue("owner"),Guid.Parse(n.GetValue("mission")),Guid.Parse(n.GetValue("vessel")),
                double.Parse(n.GetValue("start"),CapacityCulture),double.Parse(n.GetValue("end"),CapacityCulture),
                int.Parse(n.GetValue("monitored"),CapacityCulture),int.Parse(n.GetValue("live"),CapacityCulture),int.Parse(n.GetValue("parts"),CapacityCulture),int.Parse(n.GetValue("burns"),CapacityCulture));
            r.Validate();return r;
        }
        static void WriteCapacityRequest(ConfigNode n,FleetCapacityRequest r)
        {
            n.AddValue("id",r.ReservationId);n.AddValue("owner",r.Owner);n.AddValue("mission",r.MissionId);n.AddValue("vessel",r.VesselId);
            n.AddValue("start",r.StartUT.ToString("R",CapacityCulture));n.AddValue("end",r.EndUT.ToString("R",CapacityCulture));
            n.AddValue("monitored",r.MonitoredVessels);n.AddValue("live",r.LiveVessels);n.AddValue("parts",r.LiveParts);n.AddValue("burns",r.BackgroundBurns);
        }
        void LoadCapacity(ConfigNode node)
        {
            capacityLedger=new CapacityLedger();capacityHeld=false;capacitySavedNodes.Clear();
            foreach(var n in node.GetNodes("CAPACITY_RESERVATIONS"))capacitySavedNodes.Add(n.CreateCopy());
            try {
                string held=node.GetValue("capacityReservationsHeld");
                if(node.GetValues("capacityReservationsHeld").Length>1)throw new FormatException("Duplicate capacity hold flags");
                if(held!=null&&bool.Parse(held))throw new FormatException("Capacity reservation hold was saved");
                if(capacitySavedNodes.Count==0){if(held!=null)throw new FormatException("Missing saved capacity reservation ledger");return;} // Old saves migrate to an empty ledger.
                if(capacitySavedNodes.Count!=1)throw new FormatException("Duplicate capacity reservation ledgers");
                var ledger=capacitySavedNodes[0];
                if(ledger.GetValue("version")!="1")throw new FormatException("Unsupported capacity reservation format");
                var records=ledger.GetNodes("RESERVATION");
                if(int.Parse(ledger.GetValue("count"),CapacityCulture)!=records.Length)throw new FormatException("Capacity reservation count changed");
                var restored=new CapacityLedger();
                foreach(var n in records) {
                    if(n.GetNodes("INITIAL").Length!=1||n.GetNodes("CURRENT").Length!=1)throw new FormatException("Invalid capacity request snapshots");
                    restored.Restore(new CapacityEntry{Initial=ReadCapacityRequest(n.GetNode("INITIAL")),Current=ReadCapacityRequest(n.GetNode("CURRENT")),Revision=int.Parse(n.GetValue("revision"),CapacityCulture),Released=bool.Parse(n.GetValue("released"))});
                }
                capacityLedger=restored;
            }catch(Exception e){capacityHeld=true;UnityEngine.Debug.LogError("[PersistentFlight] Capacity reservation ledger held: "+e.Message);}
        }
        void SaveCapacity(ConfigNode node)
        {
            node.AddValue("capacityReservationsHeld",capacityHeld);
            if(capacityHeld){foreach(var saved in capacitySavedNodes)node.AddNode(saved.CreateCopy());return;}
            var ledger=node.AddNode("CAPACITY_RESERVATIONS");ledger.AddValue("version",1);ledger.AddValue("count",capacityLedger.Count);
            foreach(var e in capacityLedger.Entries) {
                var n=ledger.AddNode("RESERVATION");n.AddValue("revision",e.Revision);n.AddValue("released",e.Released);
                WriteCapacityRequest(n.AddNode("INITIAL"),e.Initial);WriteCapacityRequest(n.AddNode("CURRENT"),e.Current);
            }
        }
    }
}
