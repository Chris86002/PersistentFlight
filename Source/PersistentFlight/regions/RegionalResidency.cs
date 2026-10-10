using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PersistentFlight.Regions
{
    // Restore opt-in loading bounds from saved CPU modules, before their kOS boots.
    // Bounds remain per-vessel; unrelated vessels keep stock loading rules.
    public sealed class RegionalResidency
    {
        sealed class Lease { public Vessel Vessel; public VesselRanges Before; public int Frame; }
        readonly Dictionary<Guid,Lease> leases=new Dictionary<Guid,Lease>();
        readonly HashSet<Guid> seen=new HashSet<Guid>();
        readonly List<Guid> removed=new List<Guid>();
        public static bool Armed(Vessel v)
        {
            if(v.loaded) return v.parts.Any(p=>p.Modules.OfType<ModuleRegionalAutonomy>().Any(m=>m.automatic));
            return v.protoVessel!=null && v.protoVessel.protoPartSnapshots.Any(p=>p.modules.Any(m=>
                m.moduleName=="ModuleRegionalAutonomy" && String.Equals(m.moduleValues.GetValue("automatic"),"True",StringComparison.OrdinalIgnoreCase)));
        }
        public void Update()
        {
            long stamp=FleetPerformance.Start();try{Refresh();}finally{FleetPerformance.Residency.Record(stamp);}
        }
        void Refresh()
        {
            if(FlightGlobals.fetch==null || !FlightGlobals.ready) return;
            seen.Clear();removed.Clear();
            foreach(var v in FlightGlobals.Vessels)
            {
                if(v==null||!Armed(v))continue;
                seen.Add(v.id);
                Lease lease;
                if(leases.TryGetValue(v.id,out lease)&&lease.Vessel!=v) {
                    if(lease.Vessel!=null)lease.Vessel.vesselRanges=lease.Before;
                    leases.Remove(v.id);lease=null;
                }
                // Let the core capture its original range baseline first.
                if(v.vesselRanges.orbit.load<62000) continue;
                if(!leases.TryGetValue(v.id,out lease)) {
                    lease=new Lease{Vessel=v,Before=new VesselRanges(v.vesselRanges),Frame=Time.frameCount};
                    leases.Add(v.id,lease);
                }
                bool unpack=Time.frameCount>lease.Frame||(v.loaded&&!v.packed);
                var r=v.vesselRanges;Extend(r.prelaunch,unpack);Extend(r.landed,unpack);Extend(r.splashed,unpack);
                Extend(r.flying,unpack);Extend(r.subOrbital,unpack);Extend(r.orbit,unpack);Extend(r.escaping,unpack);
            }
            foreach(var pair in leases)if(!seen.Contains(pair.Key))removed.Add(pair.Key);
            foreach(var id in removed){var lease=leases[id];if(lease.Vessel!=null)lease.Vessel.vesselRanges=lease.Before;leases.Remove(id);}
        }
        static void Extend(VesselRanges.Situation s,bool unpack){s.load=Math.Max(s.load,2000000);s.unload=Math.Max(s.unload,2001000);if(unpack){s.unpack=Math.Max(s.unpack,1998000);s.pack=Math.Max(s.pack,1999000);}}
        public void Dispose() { leases.Clear();seen.Clear();removed.Clear(); }
    }
}
