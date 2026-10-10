using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace PersistentFlight
{
    // Public contracts contain no Unity/KSP objects. Invoke the gateway on KSP's main thread.
    public sealed class FleetBurnRequest
    {
        public readonly Guid MissionId,VesselId;
        public readonly string Owner,Direction;
        public readonly double StartUT,DeltaV;
        public FleetBurnRequest(Guid missionId,string owner,Guid vesselId,double startUT,double deltaV,string direction)
        { MissionId=missionId; Owner=owner; VesselId=vesselId; StartUT=startUT; DeltaV=deltaV; Direction=direction; }
        internal string Signature()
        {
            if(MissionId==Guid.Empty||VesselId==Guid.Empty||String.IsNullOrWhiteSpace(Owner)||Owner.Length>64||
                !BurnMath.Finite(StartUT)||!BurnMath.Finite(DeltaV)||DeltaV<=0||DeltaV>10000||String.IsNullOrEmpty(Direction))
                throw new ArgumentException("Use stable mission/vessel identities, an owner and a valid burn request");
            BurnDirection d;
            if(!Enum.TryParse(Direction.ToUpperInvariant(),out d)||!Enum.IsDefined(typeof(BurnDirection),d))throw new ArgumentException("Unsupported burn direction");
            return Owner+"|"+VesselId+"|"+StartUT.ToString("R",CultureInfo.InvariantCulture)+"|"+DeltaV.ToString("R",CultureInfo.InvariantCulture)+"|"+d;
        }
    }
    public sealed class FleetMissionResult
    {
        public readonly Guid MissionId,VesselId;
        public readonly string Owner,Status;
        public readonly bool Finished;
        public readonly double DeliveredDV;
        internal FleetMissionResult(MissionReceipt r) { MissionId=r.Id; VesselId=r.Vessel; Owner=r.Owner; Status=r.Status; Finished=r.Finished; DeliveredDV=r.Delivered; }
    }
    sealed class MissionReceipt
    {
        public Guid Id,Vessel; public string Owner,Signature,Status; public bool Finished; public double Delivered;
    }
    public static class FleetMissionApi
    {
        public const int Version=1;
        public const string Capabilities="CapturedStockChemicalBurn;PersistentReceipts;CancelByOwner;TimingMetrics";
        static PersistentFlightScenario Service() {
            var service=PersistentFlightScenario.Instance;
            if(service==null)throw new InvalidOperationException("PersistentFlight is not ready in this save");
            service.CheckMissionThread(); return service;
        }
        public static FleetMissionResult Submit(FleetBurnRequest request) { return Service().SubmitMission(request); }
        public static FleetMissionResult Get(Guid missionId) { return Service().GetMission(missionId); }
        public static FleetMissionResult Cancel(Guid missionId,string owner) { return Service().CancelMission(missionId,owner); }
    }
    public partial class PersistentFlightScenario
    {
        const int ReceiptLimit=20000;
        readonly Dictionary<Guid,MissionReceipt> receipts=new Dictionary<Guid,MissionReceipt>();
        int missionThread;
        bool receiptLoadFailed;
        internal void CheckMissionThread() {
            if(missionThread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Dispatch PersistentFlight mission calls on KSP's main thread");
            if(receiptLoadFailed)throw new InvalidOperationException("Mission receipts could not be restored; submissions are held to prevent replay");
        }
        internal FleetMissionResult SubmitMission(FleetBurnRequest r)
        {
            if(r==null)throw new ArgumentNullException("request"); string signature=r.Signature();
            MissionReceipt prior;
            if(receipts.TryGetValue(r.MissionId,out prior)) {
                if(prior.Signature!=signature)throw new InvalidOperationException("Mission ID already belongs to a different request");
                return GetMission(r.MissionId);
            }
            if(receipts.Count>=ReceiptLimit)throw new InvalidOperationException("Mission receipt capacity reached; no receipts were discarded");
            if(FlightGlobals.Vessels==null)throw new InvalidOperationException("Flight vessels are not ready");
            Vessel vessel=null; foreach(var v in FlightGlobals.Vessels)if(v!=null&&v.id==r.VesselId) { vessel=v; break; }
            // Queue validates capture, topology, fuel, mode and exclusive live/background ownership.
            BurnJob job=Queue(vessel,r.StartUT,r.DeltaV,r.Direction);
            job.MissionId=r.MissionId;
            var receipt=new MissionReceipt {Id=r.MissionId,Vessel=r.VesselId,Owner=r.Owner,Signature=signature,Status=job.Status};
            receipts.Add(receipt.Id,receipt); return new FleetMissionResult(receipt);
        }
        internal FleetMissionResult GetMission(Guid id)
        {
            MissionReceipt receipt; if(!receipts.TryGetValue(id,out receipt))return null;
            BurnJob job=Find(receipt.Vessel);
            if(job!=null&&job.MissionId==id) { receipt.Status=job.Status; receipt.Finished=job.Finished; receipt.Delivered=job.DeliveredDV; }
            return new FleetMissionResult(receipt);
        }
        internal FleetMissionResult CancelMission(Guid id,string owner)
        {
            MissionReceipt receipt; if(!receipts.TryGetValue(id,out receipt))return null;
            if(receipt.Owner!=owner)throw new InvalidOperationException("Only the mission owner may cancel this request");
            BurnJob job=Find(receipt.Vessel);
            if(job!=null&&job.MissionId==id)Cancel(receipt.Vessel);
            return GetMission(id);
        }
        void LoadReceipts(ConfigNode node)
        {
            receipts.Clear(); receiptLoadFailed=false;
            bool held; if(bool.TryParse(node.GetValue("missionReceiptsHeld"),out held)&&held)receiptLoadFailed=true;
            try {
                foreach(ConfigNode n in node.GetNodes("MISSION_RECEIPT")) {
                    var r=new MissionReceipt {Id=Guid.Parse(n.GetValue("id")),Vessel=Guid.Parse(n.GetValue("vessel")),Owner=n.GetValue("owner"),Signature=n.GetValue("signature"),Status=n.GetValue("status"),Finished=bool.Parse(n.GetValue("finished")),Delivered=double.Parse(n.GetValue("delivered"),CultureInfo.InvariantCulture)};
                    if(r.Id==Guid.Empty||r.Vessel==Guid.Empty||String.IsNullOrEmpty(r.Owner)||String.IsNullOrEmpty(r.Signature)||!BurnMath.Finite(r.Delivered)||r.Delivered<0||receipts.Count>=ReceiptLimit)throw new FormatException("Invalid mission receipt");
                    receipts.Add(r.Id,r);
                }
                foreach(BurnJob job in Jobs)if(job.MissionId!=Guid.Empty) {
                    MissionReceipt r;
                    if(!receipts.TryGetValue(job.MissionId,out r)||r.Vessel!=job.VesselId)throw new FormatException("Burn is missing its matching mission receipt");
                }
                foreach(var r in receipts.Values)if(!r.Finished&&!Jobs.Exists(j=>j.MissionId==r.Id))throw new FormatException("Pending receipt is missing its burn");
            } catch(Exception e) { receiptLoadFailed=true; UnityEngine.Debug.LogError("[PersistentFlight] Mission gateway held: "+e.Message); }
        }
        void SaveReceipts(ConfigNode node)
        {
            foreach(var r in receipts.Values) {
                GetMission(r.Id);
                var n=node.AddNode("MISSION_RECEIPT"); n.AddValue("id",r.Id); n.AddValue("vessel",r.Vessel); n.AddValue("owner",r.Owner); n.AddValue("signature",r.Signature);
                n.AddValue("status",r.Status); n.AddValue("finished",r.Finished); n.AddValue("delivered",r.Delivered.ToString("R",CultureInfo.InvariantCulture));
            }
            node.AddValue("missionReceiptsHeld",receiptLoadFailed);
        }
    }
}
