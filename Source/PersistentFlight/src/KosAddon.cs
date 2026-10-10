using System;
using System.Linq;
using kOS;
using kOS.AddOns;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Exceptions;

[assembly: KSPAssemblyDependency("kOS", 1, 6)]

namespace PersistentFlight
{
    [kOSAddon("PF")]
    [kOS.Safe.Utilities.KOSNomenclature("PersistentFlightAddon")]
    public class FlightAddon : kOS.Suffixed.Addon
    {
        public FlightAddon(SharedObjects shared) : base(shared)
        {
            AddSuffix("KEEPALIVE",new OneArgsSuffix<BooleanValue,ScalarValue>(KeepAlive));
            AddSuffix("DISARM",new NoArgsVoidSuffix(Disarm));
            AddSuffix("QUEUEBURN",new ThreeArgsSuffix<BooleanValue,ScalarValue,ScalarValue,StringValue>(QueueBurn));
            AddSuffix("CANCELBURN",new NoArgsVoidSuffix(Cancel));
            AddSuffix("STATUS",new Suffix<StringValue>(Status));
            AddSuffix("DELIVEREDDV",new Suffix<ScalarValue>(Delivered));
            AddSuffix("VERSION",new Suffix<StringValue>(() => "0.1.1"));
        }
        public override BooleanValue Available() { return PersistentFlightScenario.Instance!=null; }
        BooleanValue KeepAlive(ScalarValue km)
        {
            double range=km.GetDoubleValue();
            if (!BurnMath.Finite(range) || range<5 || range>60) throw new KOSException("PF:KEEPALIVE radius must be 5 to 60 km");
            ModulePersistentFlight module=shared.KSPPart.Modules.OfType<ModulePersistentFlight>().FirstOrDefault();
            if(module==null) throw new KOSException("PersistentFlight module is missing on this CPU part");
            module.rangeKm=(float)range; module.armed=true; return true;
        }
        void Disarm()
        {
            foreach(var m in shared.KSPPart.Modules.OfType<ModulePersistentFlight>()) m.armed=false;
        }
        BooleanValue QueueBurn(ScalarValue ut,ScalarValue dv,StringValue direction)
        {
            try { Service().Queue(shared.Vessel,ut.GetDoubleValue(),dv.GetDoubleValue(),direction.ToString()); return true; }
            catch(Exception e) { throw new KOSException("PF:QUEUEBURN: " + e.Message); }
        }
        void Cancel() { Service().Cancel(shared.Vessel.id); }
        StringValue Status()
        {
            BurnJob j=Service().Find(shared.Vessel.id);
            return j!=null ? j.Status : shared.Vessel.packed ? "Packed" : "Live physics";
        }
        ScalarValue Delivered() { BurnJob j=Service().Find(shared.Vessel.id); return j==null ? 0 : j.DeliveredDV; }
        static PersistentFlightScenario Service()
        {
            if(PersistentFlightScenario.Instance==null) throw new KOSException("PersistentFlight is unavailable in this scene");
            return PersistentFlightScenario.Instance;
        }
    }
}
