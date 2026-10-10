using System;
using System.Collections.Generic;

namespace PersistentFlight
{
    // Immutable contracts; counts are independent planning dimensions, not measured use.
    public sealed class FleetCapacityRequest
    {
        public readonly Guid ReservationId,MissionId,VesselId;
        public readonly string Owner;
        public readonly double StartUT,EndUT;
        public readonly int MonitoredVessels,LiveVessels,LiveParts,BackgroundBurns;
        public FleetCapacityRequest(Guid reservationId,string owner,Guid missionId,Guid vesselId,
            double startUT,double endUT,int monitoredVessels,int liveVessels,int liveParts,int backgroundBurns)
        { ReservationId=reservationId;Owner=owner;MissionId=missionId;VesselId=vesselId;
          StartUT=startUT;EndUT=endUT;MonitoredVessels=monitoredVessels;LiveVessels=liveVessels;LiveParts=liveParts;BackgroundBurns=backgroundBurns; }
        internal void Validate()
        {
            if(ReservationId==Guid.Empty||MissionId==Guid.Empty||String.IsNullOrWhiteSpace(Owner)||Owner.Length>64||
               Owner!=Owner.Trim()||Owner.IndexOfAny(new[]{'\r','\n','{','}','='})>=0||
               double.IsNaN(StartUT)||double.IsInfinity(StartUT)||double.IsNaN(EndUT)||double.IsInfinity(EndUT)||StartUT<0||EndUT<=StartUT||
               MonitoredVessels<0||LiveVessels<0||LiveParts<0||BackgroundBurns<0||
               (MonitoredVessels==0&&LiveVessels==0&&LiveParts==0&&BackgroundBurns==0))
                throw new ArgumentException("Use stable reservation/mission IDs, an owner, a finite [start,end) interval and positive capacity in at least one dimension");
            foreach(char c in Owner)if(Char.IsControl(c))throw new ArgumentException("Owner identifiers cannot contain control characters");
        }
        internal bool Same(FleetCapacityRequest r)
        { return r!=null&&ReservationId==r.ReservationId&&Owner==r.Owner&&MissionId==r.MissionId&&VesselId==r.VesselId&&StartUT==r.StartUT&&EndUT==r.EndUT&&MonitoredVessels==r.MonitoredVessels&&LiveVessels==r.LiveVessels&&LiveParts==r.LiveParts&&BackgroundBurns==r.BackgroundBurns; }
    }
    public sealed class FleetCapacityReservation
    {
        public readonly FleetCapacityRequest Request;
        public readonly int Revision;
        public readonly bool Released;
        internal FleetCapacityReservation(FleetCapacityRequest r,int revision,bool released) { Request=r;Revision=revision;Released=released; }
    }
    public sealed class FleetCapacityUsage
    {
        public readonly long MonitoredVessels,LiveVessels,LiveParts,BackgroundBurns;
        public readonly int Reservations;
        internal FleetCapacityUsage(long monitored,long live,long parts,long burns,int reservations)
        { MonitoredVessels=monitored;LiveVessels=live;LiveParts=parts;BackgroundBurns=burns;Reservations=reservations; }
    }
    sealed class CapacityEntry
    {
        internal FleetCapacityRequest Initial,Current;
        internal int Revision;
        internal bool Released;
        internal FleetCapacityReservation Snapshot() { return new FleetCapacityReservation(Current,Revision,Released); }
    }
    sealed class CapacityLedger
    {
        internal const int RecordLimit=20000;
        readonly Dictionary<Guid,CapacityEntry> entries=new Dictionary<Guid,CapacityEntry>();
        // Queries build this sweep index lazily; flight ticks do no reservation work.
        struct Event { internal double UT; internal long Watched,Live,Parts,Burns; internal int Count; }
        Event[] index=new Event[0];bool dirty=true;
        internal int Count { get { return entries.Count; } }
        internal IEnumerable<CapacityEntry> Entries { get { return entries.Values; } }
        internal void Restore(CapacityEntry entry)
        {
            entry.Initial.Validate();entry.Current.Validate();
            if(entry.Revision<1||!Identity(entry.Initial,entry.Current)||
               (entry.Revision==1&&(entry.Released||!entry.Initial.Same(entry.Current)))||entries.Count>=RecordLimit)
                throw new FormatException("Invalid capacity reservation record");
            entries.Add(entry.Current.ReservationId,entry);dirty=true;
        }
        static bool Identity(FleetCapacityRequest a,FleetCapacityRequest b)
        { return a.ReservationId==b.ReservationId&&a.Owner==b.Owner&&a.MissionId==b.MissionId&&a.VesselId==b.VesselId; }
        CapacityEntry Owned(Guid id,string owner)
        { CapacityEntry e;if(!entries.TryGetValue(id,out e))return null;if(e.Current.Owner!=owner)throw new InvalidOperationException("Only the reservation owner may change it");return e; }
        internal FleetCapacityReservation Reserve(FleetCapacityRequest request)
        {
            if(request==null)throw new ArgumentNullException("request");request.Validate();CapacityEntry entry;
            if(entries.TryGetValue(request.ReservationId,out entry)) {
                if(!entry.Initial.Same(request))throw new InvalidOperationException("Reservation ID already belongs to another initial request");
                return entry.Snapshot();
            }
            if(entries.Count>=RecordLimit)throw new InvalidOperationException("Reservation record storage is full; released records are retained for retry safety");
            entry=new CapacityEntry{Initial=request,Current=request,Revision=1};entries.Add(request.ReservationId,entry);dirty=true;return entry.Snapshot();
        }
        internal FleetCapacityReservation Get(Guid id)
        { CapacityEntry e;return entries.TryGetValue(id,out e)?e.Snapshot():null; }
        internal FleetCapacityReservation Revise(FleetCapacityRequest request,int expectedRevision)
        {
            if(request==null)throw new ArgumentNullException("request");request.Validate();var entry=Owned(request.ReservationId,request.Owner);
            if(entry==null)throw new InvalidOperationException("Reservation does not exist");
            if(!Identity(entry.Initial,request))throw new InvalidOperationException("Reservation mission and vessel identities are fixed; use a new reservation ID");
            if(expectedRevision<1||entry.Released)throw new InvalidOperationException("Reservation is released or its expected revision is invalid");
            if(entry.Revision==(long)expectedRevision+1&&entry.Current.Same(request))return entry.Snapshot();
            if(entry.Revision!=expectedRevision)throw new InvalidOperationException("Reservation revision changed; read it before revising");
            if(entry.Current.Same(request))return entry.Snapshot();
            if(entry.Revision==Int32.MaxValue)throw new InvalidOperationException("Reservation revision storage is full");
            entry.Current=request;entry.Revision++;dirty=true;return entry.Snapshot();
        }
        internal FleetCapacityReservation Release(Guid id,string owner,int expectedRevision)
        {
            var entry=Owned(id,owner);if(entry==null)return null;
            if(expectedRevision<1)throw new ArgumentOutOfRangeException("expectedRevision");
            if(entry.Released&&(entry.Revision==expectedRevision||entry.Revision==(long)expectedRevision+1))return entry.Snapshot();
            if(entry.Revision!=expectedRevision||entry.Released)throw new InvalidOperationException("Reservation revision changed; read it before releasing");
            if(entry.Revision==Int32.MaxValue)throw new InvalidOperationException("Reservation revision storage is full");
            entry.Released=true;entry.Revision++;dirty=true;return entry.Snapshot();
        }
        internal FleetCapacityReservation[] List(string owner,Guid missionId,double startUT,double endUT,bool includeReleased)
        {
            Interval(startUT,endUT);var result=new List<FleetCapacityReservation>();
            foreach(var e in entries.Values)if((owner==null||e.Current.Owner==owner)&&(missionId==Guid.Empty||e.Current.MissionId==missionId)&&
                (includeReleased||!e.Released)&&e.Current.StartUT<endUT&&e.Current.EndUT>startUT)result.Add(e.Snapshot());
            result.Sort((a,b)=>{int c=a.Request.StartUT.CompareTo(b.Request.StartUT);return c!=0?c:a.Request.ReservationId.CompareTo(b.Request.ReservationId);});return result.ToArray();
        }
        static void Interval(double start,double end)
        { Time(start);Time(end);if(end<=start)throw new ArgumentException("Use a nonempty [start,end) interval"); }
        static void Time(double ut)
        { if(ut<0||double.IsNaN(ut)||double.IsInfinity(ut))throw new ArgumentOutOfRangeException("ut"); }
        void Index()
        {
            if(!dirty)return;var events=new List<Event>();
            foreach(var e in entries.Values)if(!e.Released) {
                var r=e.Current;events.Add(new Event{UT=r.StartUT,Watched=r.MonitoredVessels,Live=r.LiveVessels,Parts=r.LiveParts,Burns=r.BackgroundBurns,Count=1});
                events.Add(new Event{UT=r.EndUT,Watched=-((long)r.MonitoredVessels),Live=-((long)r.LiveVessels),Parts=-((long)r.LiveParts),Burns=-((long)r.BackgroundBurns),Count=-1});
            }
            events.Sort((a,b)=>a.UT.CompareTo(b.UT));var cumulative=new List<Event>();var total=new Event();
            for(int i=0;i<events.Count;) {
                double ut=events[i].UT;
                do {var e=events[i++];total.Watched+=e.Watched;total.Live+=e.Live;total.Parts+=e.Parts;total.Burns+=e.Burns;total.Count+=e.Count;}while(i<events.Count&&events[i].UT==ut);
                total.UT=ut;cumulative.Add(total);
            }
            index=cumulative.ToArray();dirty=false;
        }
        int At(double ut)
        { int lo=0,hi=index.Length;while(lo<hi){int mid=lo+(hi-lo)/2;if(index[mid].UT<=ut)lo=mid+1;else hi=mid;}return lo-1; }
        static FleetCapacityUsage Usage(Event e) { return new FleetCapacityUsage(e.Watched,e.Live,e.Parts,e.Burns,e.Count); }
        internal FleetCapacityUsage UsageAt(double ut)
        { Time(ut);Index();int i=At(ut);return Usage(i<0?new Event():index[i]); }
        internal FleetCapacityUsage Peak(double startUT,double endUT)
        {
            Interval(startUT,endUT);Index();int i=At(startUT);var peak=i<0?new Event():index[i];
            for(i++;i<index.Length&&index[i].UT<endUT;i++) {
                var e=index[i];peak.Watched=Math.Max(peak.Watched,e.Watched);peak.Live=Math.Max(peak.Live,e.Live);peak.Parts=Math.Max(peak.Parts,e.Parts);peak.Burns=Math.Max(peak.Burns,e.Burns);peak.Count=Math.Max(peak.Count,e.Count);
            }
            return Usage(peak);
        }
    }
}
