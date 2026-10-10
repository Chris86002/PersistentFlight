using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PersistentFlight
{
    public sealed class FuelRate
    {
        public string Name;
        public double UnitsPerSecond;
        public List<uint> Parts = new List<uint>();
        public FuelRate(string name, double rate) { Name = name; UnitsPerSecond = rate; }
    }
    public sealed class BurnJob
    {
        public Guid VesselId;
        public Guid MissionId;
        public Guid DebriefId;
        public double StartUT, LastUT, TargetDV, DeliveredDV, Thrust, MassFlow, DryMass;
        public int BodyIndex;
        public BurnDirection Direction;
        public string Status = "Queued";
        public bool Finished;
        public List<FuelRate> Fuels = new List<FuelRate>();
        public List<uint> EngineParts = new List<uint>();
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public ConfigNode Save()
        {
            ConfigNode n = new ConfigNode("BURN");
            if(DebriefId!=Guid.Empty)n.AddValue("debriefId",DebriefId);
            n.AddValue("vessel", VesselId.ToString());
            if(MissionId!=Guid.Empty)n.AddValue("missionId",MissionId.ToString());
            n.AddValue("startUT", StartUT.ToString("R", C)); n.AddValue("lastUT", LastUT.ToString("R", C));
            n.AddValue("targetDV", TargetDV.ToString("R", C)); n.AddValue("deliveredDV", DeliveredDV.ToString("R", C));
            n.AddValue("thrust", Thrust.ToString("R", C)); n.AddValue("massFlow", MassFlow.ToString("R", C));
            n.AddValue("dryMass", DryMass.ToString("R", C)); n.AddValue("body", BodyIndex);
            n.AddValue("direction", Direction); n.AddValue("status", Status); n.AddValue("finished", Finished);
            foreach (uint id in EngineParts) n.AddValue("enginePart", id);
            foreach (FuelRate f in Fuels)
            {
                ConfigNode x = n.AddNode("FUEL"); x.AddValue("name", f.Name); x.AddValue("rate", f.UnitsPerSecond.ToString("R", C));
                foreach (uint id in f.Parts) x.AddValue("part", id);
            }
            return n;
        }
        public static BurnJob Load(ConfigNode n)
        {
            BurnJob j = new BurnJob();
            // Optional observer identity must never invalidate an otherwise valid burn.
            Guid.TryParse(n.GetValue("debriefId"),out j.DebriefId);
            if(n.HasValue("missionId"))j.MissionId=Guid.Parse(n.GetValue("missionId"));
            j.VesselId = Guid.Parse(n.GetValue("vessel")); j.StartUT = Num(n,"startUT"); j.LastUT = Num(n,"lastUT");
            j.TargetDV = Num(n,"targetDV"); j.DeliveredDV = Num(n,"deliveredDV"); j.Thrust = Num(n,"thrust");
            j.MassFlow = Num(n,"massFlow"); j.DryMass = Num(n,"dryMass"); j.BodyIndex = int.Parse(n.GetValue("body"), C);
            j.Direction = (BurnDirection)Enum.Parse(typeof(BurnDirection), n.GetValue("direction"));
            j.Status = n.GetValue("status"); j.Finished = bool.Parse(n.GetValue("finished"));
            foreach (string id in n.GetValues("enginePart")) j.EngineParts.Add(uint.Parse(id,C));
            foreach (ConfigNode f in n.GetNodes("FUEL"))
            {
                FuelRate rate = new FuelRate(f.GetValue("name"), Num(f,"rate"));
                foreach (string id in f.GetValues("part")) rate.Parts.Add(uint.Parse(id,C));
                j.Fuels.Add(rate);
            }
            if (j.Thrust <= 0 || j.MassFlow <= 0 || j.DryMass <= 0 || j.TargetDV <= 0 || j.Fuels.Count == 0
                || j.DeliveredDV < 0 || j.DeliveredDV > j.TargetDV + 1e-5 || !Enum.IsDefined(typeof(BurnDirection),j.Direction)
                || j.Fuels.Any(f => f.UnitsPerSecond <= 0 || f.Parts.Count == 0)) throw new FormatException("Invalid saved burn");
            return j;
        }
        static double Num(ConfigNode n, string key)
        {
            double v = double.Parse(n.GetValue(key), C); if (!BurnMath.Finite(v)) throw new FormatException(key); return v;
        }
    }
}
