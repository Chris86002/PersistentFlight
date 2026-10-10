using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[assembly: KSPAssembly("PersistentFlight", 0, 1)]

namespace PersistentFlight
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public partial class PersistentFlightScenario : ScenarioModule
    {
        public static PersistentFlightScenario Instance;
        public readonly List<BurnJob> Jobs = new List<BurnJob>();
        readonly DueQueue<BurnJob> due = new DueQueue<BurnJob>();
        readonly List<BurnJob> active = new List<BurnJob>();
        readonly Dictionary<Guid,BurnJob> byVessel = new Dictionary<Guid,BurnJob>();
        readonly Dictionary<Guid,Vessel> vessels = new Dictionary<Guid,Vessel>();
        int indexedCount = -1;
        public int ScheduledCount { get { EnsureIndex(); return due.Count; } }
        public int ActiveCount { get { EnsureIndex(); return active.Count; } }
        public double NextScheduledUT { get { EnsureIndex(); return due.NextUT; } }
        public double MaximumDispatchDelay { get; private set; }
        void EnsureIndex() { if(indexedCount!=Jobs.Count) RebuildIndex(); }
        void RebuildIndex()
        {
            due.Clear(); active.Clear(); byVessel.Clear();
            foreach(BurnJob j in Jobs) {
                byVessel[j.VesselId]=j;
                if(!j.Finished)due.Schedule(j.VesselId,j.StartUT,j);
            }
            indexedCount=Jobs.Count;
        }
        readonly Dictionary<Guid, VesselRanges> originalRanges = new Dictionary<Guid, VesselRanges>();
        readonly Dictionary<Guid, float> previousRange = new Dictionary<Guid, float>();
        readonly Dictionary<uint, VesselRanges> cpuOriginalRanges = new Dictionary<uint, VesselRanges>();
        float nextScan;
        double previousUT=double.NaN;
        public override void OnAwake() { base.OnAwake(); Instance = this; missionThread=System.Threading.Thread.CurrentThread.ManagedThreadId; }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); Instance = this; Jobs.Clear();
            foreach (ConfigNode n in node.GetNodes("BURN"))
            {
                try { Jobs.Add(BurnJob.Load(n)); }
                catch (Exception e) { Debug.LogError("[PersistentFlight] Rejected invalid saved burn: " + e.Message); }
            }
            RebuildIndex(); previousUT = double.NaN; MaximumDispatchDelay=0; FleetPerformance.Reset();
            LoadReceipts(node);
            LoadCapacity(node);
            LoadEvents(node);
            LoadDebriefs(node);
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node); foreach (BurnJob j in Jobs) node.AddNode(j.Save());
            SaveReceipts(node);
            SaveCapacity(node);
            SaveEvents(node);
            SaveDebriefs(node);
        }
        public void OnDestroy()
        {
            if (FlightGlobals.fetch != null && FlightGlobals.Vessels != null)
                foreach (Vessel v in FlightGlobals.Vessels)
                    if (v != null && originalRanges.ContainsKey(v.id)) v.vesselRanges = originalRanges[v.id];
            if (Instance == this) Instance = null;
        }
        public BurnJob Find(Guid id) { EnsureIndex(); BurnJob j; return byVessel.TryGetValue(id,out j)?j:null; }
        public void Cancel(Guid id)
        {
            BurnJob j = Find(id); if (j != null && !j.Finished) { Finish(j, "Cancelled"); due.Remove(id); active.Remove(j); }
        }
        public void FixedUpdate()
        {
            long stamp=FleetPerformance.Start();
            try { Tick(); } finally { FleetPerformance.CoreTick.Record(stamp); }
        }
        void Tick()
        {
            if (HighLogic.CurrentGame == null || Planetarium.fetch == null || FlightGlobals.fetch == null || FlightGlobals.Vessels == null || FlightGlobals.Bodies == null) return;
            double now = Planetarium.GetUniversalTime();
            EnsureIndex();
            if (!double.IsNaN(previousUT) && now < previousUT - 0.01)
            {
                foreach (BurnJob j in Jobs.Where(x => !x.Finished)) Finish(j, "Stopped: time moved backwards");
                due.Clear(); active.Clear();
            }
            previousUT = now;
            if (HighLogic.LoadedSceneIsFlight && Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 0.2f;
                long scanStamp=FleetPerformance.Start();
                try {
                foreach (Vessel v in FlightGlobals.Vessels.ToArray())
                {
                    if (v == null) continue;
                    try { UpdateRanges(v); } catch (Exception e) { Debug.LogError("[PersistentFlight] Range update: " + e); }
                }
                } finally { FleetPerformance.RangeScan.Record(scanStamp); }
            }
            BurnJob ready;
            while(due.TryTake(now,out ready)) {
                if(ready.Finished)continue;
                if(ready.DeliveredDV==0&&ready.LastUT==ready.StartUT)MaximumDispatchDelay=Math.Max(MaximumDispatchDelay,Math.Max(0,now-ready.StartUT));
                active.Add(ready);
            }
            if(active.Count==0)return;
            vessels.Clear();
            foreach(Vessel v in FlightGlobals.Vessels)if(v!=null)vessels[v.id]=v;
            // All due burns receive a tick; a budget must never silently drop flight time.
            for(int i=active.Count-1;i>=0;i--)
            {
                BurnJob j=active[i];
                try { Advance(j, now); }
                catch (Exception e) { Finish(j, "Stopped: " + e.Message); Debug.LogError("[PersistentFlight] Burn stopped: " + e); }
                if(j.Finished)active.RemoveAt(i);
            }
        }
        static IEnumerable<VesselRanges.Situation> Situations(VesselRanges r)
        {
            yield return r.prelaunch; yield return r.landed; yield return r.splashed; yield return r.flying;
            yield return r.subOrbital; yield return r.orbit; yield return r.escaping;
        }
        void UpdateRanges(Vessel v)
        {
            float km = 0;
            if (v.loaded)
            {
                foreach (Part p in v.parts)
                    foreach (ModulePersistentFlight m in p.Modules.OfType<ModulePersistentFlight>())
                        if (m.armed && BurnMath.Finite(m.rangeKm)) km = Math.Max(km, Mathf.Clamp(m.rangeKm,5,60));
            }
            else if (v.protoVessel != null)
            {
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                    foreach (ProtoPartModuleSnapshot m in p.modules.Where(x => x.moduleName == "ModulePersistentFlight"))
                    {
                        bool armed; float range;
                        if (bool.TryParse(m.moduleValues.GetValue("armed"),out armed) && armed &&
                            float.TryParse(m.moduleValues.GetValue("rangeKm"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out range)
                            && BurnMath.Finite(range)) km = Math.Max(km,Mathf.Clamp(range,5,60));
                    }
            }
            if (km == 0)
            {
                if (originalRanges.ContainsKey(v.id)) { v.vesselRanges = originalRanges[v.id]; originalRanges.Remove(v.id); previousRange.Remove(v.id); }
                return;
            }
            if (!originalRanges.ContainsKey(v.id))
            {
                // Part identities survive separation even though the vessel GUID changes.
                // Use the parent's baseline when an armed CPU moves into a new vessel.
                uint[] cpuIds = v.loaded
                    ? v.parts.Where(p=>p.Modules.OfType<ModulePersistentFlight>().Any()).Select(p=>p.flightID).ToArray()
                    : v.protoVessel.protoPartSnapshots.Where(p=>p.modules.Any(m=>m.moduleName=="ModulePersistentFlight")).Select(p=>p.flightID).ToArray();
                VesselRanges baseline = cpuIds.Where(id=>cpuOriginalRanges.ContainsKey(id)).Select(id=>cpuOriginalRanges[id]).FirstOrDefault() ?? v.vesselRanges;
                originalRanges[v.id] = baseline; v.vesselRanges = new VesselRanges(baseline);
                foreach(uint id in cpuIds) cpuOriginalRanges[id]=baseline;
                previousRange[v.id] = -1;
            }
            // Change load boundaries before physics boundaries; stock requires a tick between them.
            if (previousRange[v.id] != km)
            {
                v.vesselRanges = new VesselRanges(originalRanges[v.id]);
                foreach (VesselRanges.Situation s in Situations(v.vesselRanges))
                {
                    s.unload = Math.Max(s.unload,km*1000+3000); s.load = Math.Max(s.load,km*1000+2000);
                }
                previousRange[v.id] = km; return;
            }
            foreach (VesselRanges.Situation s in Situations(v.vesselRanges))
            {
                s.pack = Math.Max(s.pack,km*1000+1000); s.unpack = Math.Max(s.unpack,km*1000);
            }
        }
        public BurnJob Queue(Vessel v, double startUT, double deltaV, string direction)
        {
            if (v == null || !v.loaded || v.packed) throw new InvalidOperationException("Queue a burn while your craft is loaded and unpacked");
            if(v.parts.SelectMany(p=>p.Modules.OfType<IRoverRouteOwner>()).Any(m=>m.ActiveRoverRoute))throw new InvalidOperationException("Cancel the rover route before capturing a background burn on this craft");
            if(v.parts.SelectMany(p=>p.Modules.OfType<IManeuverPlanOwner>()).Any(m=>m.ActiveManeuverPlan))throw new InvalidOperationException("Cancel the saved maneuver plan before capturing a background burn on this craft");
            if (Find(v.id) != null && !Find(v.id).Finished) throw new InvalidOperationException("This craft already has a burn job");
            if (!BurnMath.Finite(startUT) || !BurnMath.Finite(deltaV) || deltaV <= 0 || deltaV > 10000 || startUT < Planetarium.GetUniversalTime())
                throw new ArgumentOutOfRangeException("Use a future universal time and delta-v from 0 to 10000 m/s");
            BurnDirection dir;
            if (!Enum.TryParse(direction.ToUpperInvariant(),out dir) || !Enum.IsDefined(typeof(BurnDirection),dir))
                throw new ArgumentException("Direction: PROGRADE, RETROGRADE, NORMAL, ANTINORMAL, RADIALOUT, RADIALIN");
            if (v.orbit.PeA < v.mainBody.atmosphereDepth + 1000 || v.Landed || v.Splashed)
                throw new InvalidOperationException("Background burns require an orbit entirely above atmosphere and terrain");
            if (v.ctrlState.mainThrottle > 0.001) throw new InvalidOperationException("Set throttle to zero before queuing a burn");
            List<ModuleEngines> engines = v.parts.SelectMany(p => p.Modules.OfType<ModuleEngines>()).Where(e => e.EngineIgnited && e.isEnabled && !e.engineShutdown).ToList();
            if (engines.Count == 0) throw new InvalidOperationException("Activate the engines for the burn first, with throttle at zero");
            v.UpdateResourceSetsIfDirty();
            BurnJob j = new BurnJob { VesselId=v.id, StartUT=startUT, LastUT=startUT, TargetDV=deltaV, Direction=dir, BodyIndex=v.mainBody.flightGlobalsIndex };
            foreach (ModuleEngines e in engines)
            {
                // The initial backend supports ordinary stock chemical engines; complex modules need adapters.
                if ((e.GetType() != typeof(ModuleEngines) && e.GetType() != typeof(ModuleEnginesFX)) || e.throttleLocked || e.atmChangeFlow || e.useThrustCurve || e.useVelCurve || e.useVelCurveIsp || e.useThrottleIspCurve || e.useEngineResponseTime || e.nonThrustMotor || e.independentThrottle)
                    throw new InvalidOperationException("This engine needs a dedicated background adapter");
                if (e.part.Modules.Cast<PartModule>().Any(m => m.moduleName == "MultiModeEngine")) throw new InvalidOperationException("Multimode engines are not supported by this backend");
                double isp=e.atmosphereCurve.Evaluate(0), thrust=e.maxThrust*e.thrustPercentage/100.0;
                if (!(isp>0) || !(thrust>0)) throw new InvalidOperationException("Engine has no usable vacuum thrust");
                double massFlow=thrust/(isp*BurnMath.G0), mixture=0;
                foreach (Propellant p in e.propellants)
                {
                    if (p.resourceDef.density <= 0) throw new InvalidOperationException("Massless engine propellants need a power-system adapter");
                    mixture += p.ratio*p.resourceDef.density;
                }
                if (!(mixture>0)) throw new InvalidOperationException("Invalid propellant mixture");
                foreach (Propellant p in e.propellants)
                {
                    FuelRate f = new FuelRate(p.name,massFlow*p.ratio/mixture);
                    ResourceFlowMode mode = p.GetFlowMode();
                    IEnumerable<Part> pool;
                    if (mode == ResourceFlowMode.NO_FLOW) pool = new [] { e.part };
                    else if (mode == ResourceFlowMode.ALL_VESSEL || mode == ResourceFlowMode.ALL_VESSEL_BALANCE) pool = v.parts;
                    else if (mode == ResourceFlowMode.STACK_PRIORITY_SEARCH || mode == ResourceFlowMode.STAGE_PRIORITY_FLOW || mode == ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE)
                        pool = e.part.crossfeedPartSet.GetParts();
                    else throw new InvalidOperationException("Unsupported fuel flow mode: " + mode);
                    f.Parts = pool.Where(part => part.Resources.Contains(p.name) && part.Resources[p.name].flowState).Select(part => part.flightID).ToList();
                    if (f.Parts.Count == 0) throw new InvalidOperationException("No connected fuel for " + p.name);
                    j.Fuels.Add(f);
                }
                j.Thrust+=thrust; j.MassFlow+=massFlow; j.EngineParts.Add(e.part.flightID);
            }
            double resourceMass = v.parts.Sum(p => p.Resources.Cast<PartResource>().Sum(r => r.amount*r.info.density));
            j.DryMass = v.GetTotalMass()-resourceMass;
            if (j.DryMass <= 0) throw new InvalidOperationException("Invalid dry mass");
            FuelRate avionics = new FuelRate("ElectricCharge",0.02);
            avionics.Parts = v.parts.Where(p => p.Resources.Contains("ElectricCharge") && p.Resources["ElectricCharge"].flowState).Select(p => p.flightID).ToList();
            if (avionics.Parts.Count == 0) throw new InvalidOperationException("Background flight requires an electric power reserve");
            j.Fuels.Add(avionics);
            // Completed records are replaced to keep save size bounded per vessel.
            BurnJob previous=Find(v.id); if(previous!=null&&previous.MissionId!=Guid.Empty)GetMission(previous.MissionId);
            ObserveDebrief(()=>BackgroundReport(j));
            Jobs.RemoveAll(x => x.VesselId==v.id && x.Finished); Jobs.Add(j);
            byVessel[v.id]=j; due.Schedule(v.id,j.StartUT,j); indexedCount=Jobs.Count;
            Debug.Log("[PersistentFlight] Queued " + deltaV + " m/s " + direction + " for " + v.vesselName);
            return j;
        }
        static DVector D(Vector3d v) { return new DVector(v.x,v.y,v.z); }
        static Vector3d V(DVector v) { return new Vector3d(v.X,v.Y,v.Z); }
        void Advance(BurnJob j, double now)
        {
            if (now < j.StartUT) return;
            Vessel v; vessels.TryGetValue(j.VesselId,out v);
            if (v == null) { Finish(j,"Stopped: craft missing or changed by docking"); return; }
            // A queued job never competes with a live kOS program, even at high warp.
            if (v.loaded) { j.Status="Waiting for craft to unload"; j.LastUT=now; return; }
            if (!v.packed || v.protoVessel==null || v.mainBody.flightGlobalsIndex!=j.BodyIndex)
            { Finish(j,"Stopped: simulation mode or sphere of influence changed"); return; }
            if (v.Landed || v.Splashed || v.orbit.PeA < v.mainBody.atmosphereDepth+1000)
            { Finish(j,"Stopped: orbit reaches atmosphere or surface"); return; }
            ProtoVessel proto=v.protoVessel;
            if (j.EngineParts.Any(id => !proto.protoPartSnapshots.Any(p=>p.flightID==id)))
            { Finish(j,"Stopped: engine topology changed"); return; }
            double elapsed=now-j.LastUT;
            // KSP's accumulated clock can cross a scheduled UT by a few picoseconds.
            // That is a pending tick, not evidence of resource exhaustion.
            if (elapsed<=1e-8) return;
            if (j.TargetDV-j.DeliveredDV<=1e-6) { Finish(j,"Complete"); return; }
            if (elapsed>120) { Finish(j,"Stopped: time step exceeded 120 seconds; use lower warp"); return; }
            OrbitalState state = new OrbitalState(D(v.orbit.getRelativePositionAtUT(j.LastUT)),D(v.orbit.getOrbitalVelocityAtUT(j.LastUT)));
            List<ProtoPartResourceSnapshot> all=proto.protoPartSnapshots.SelectMany(p=>p.resources).ToList();
            double mass=j.DryMass+all.Sum(r=>r.amount*PartResourceLibrary.Instance.GetDefinition(r.resourceName).density);
            // One withdrawal plan handles overlapping engine pools without double spending.
            Dictionary<ProtoPartResourceSnapshot,double> rates = new Dictionary<ProtoPartResourceSnapshot,double>();
            foreach (FuelRate f in j.Fuels)
            {
                var slots=proto.protoPartSnapshots.Where(p=>f.Parts.Contains(p.flightID)).SelectMany(p=>p.resources).Where(r=>r.resourceName==f.Name && r.flowState && r.amount>0).ToList();
                double total=slots.Sum(r=>r.amount);
                if (total<=0) { Finish(j,"Stopped: no available " + f.Name + " in captured fuel pool"); return; }
                foreach (var slot in slots)
                {
                    double rate=f.UnitsPerSecond*slot.amount/total;
                    if (!rates.ContainsKey(slot)) rates[slot]=0;
                    rates[slot]+=rate;
                }
            }
            double allowed=rates.Min(pair=>pair.Key.amount/pair.Value);
            double remaining=BurnMath.TimeForDeltaV(j.Thrust,j.MassFlow,mass,j.TargetDV-j.DeliveredDV);
            double duration=Math.Min(elapsed,Math.Min(allowed,remaining));
            if (duration<1e-8) { Finish(j,"Stopped: propellant depleted"); return; }
            double cursor=j.LastUT, end=cursor+duration;
            while (cursor<end-1e-9)
            {
                double dt=Math.Min(0.5,end-cursor);
                OrbitalState candidate=BurnMath.Step(state,v.mainBody.gravParameter,j.Thrust,mass,j.MassFlow,dt,j.Direction);
                if (candidate.Position.Length<v.mainBody.Radius+v.mainBody.atmosphereDepth+1000 || candidate.Position.Length>v.mainBody.sphereOfInfluence*0.99)
                { Finish(j,"Stopped: atmosphere or sphere of influence boundary"); return; }
                state=candidate; mass-=j.MassFlow*dt; cursor+=dt;
            }
            Orbit trial=new Orbit(v.orbit);
            trial.UpdateFromStateVectors(V(state.Position),V(state.Velocity),v.mainBody,end);
            if (trial.PeA<v.mainBody.atmosphereDepth+1000) { Finish(j,"Stopped: burn would enter atmosphere or surface"); return; }
            // Validate every result before committing resources and orbital state together.
            double massBefore=mass+j.MassFlow*duration;
            foreach(var pair in rates) pair.Key.amount=Math.Max(0,pair.Key.amount-pair.Value*duration);
            v.orbit.UpdateFromStateVectors(V(state.Position),V(state.Velocity),v.mainBody,end);
            v.orbit.UpdateFromUT(now);
            v.orbitDriver.pos=v.orbit.pos; v.orbitDriver.vel=v.orbit.vel;
            v.CoMD=v.mainBody.position+v.orbit.pos.xzy;
            proto.orbitSnapShot=new OrbitSnapshot(v.orbit);
            j.DeliveredDV+=BurnMath.DeltaV(j.Thrust,j.MassFlow,massBefore,duration); j.LastUT=end;
            RecordBackgroundStep(j,duration,rates.Where(p=>p.Key.resourceName=="LiquidFuel").Sum(p=>p.Value*duration),rates.Where(p=>p.Key.resourceName=="Oxidizer").Sum(p=>p.Value*duration),rates.Where(p=>p.Key.resourceName=="ElectricCharge").Sum(p=>p.Value*duration));
            j.Status="Background burn: " + j.DeliveredDV.ToString("F1") + "/" + j.TargetDV.ToString("F1") + " m/s";
            if (j.TargetDV-j.DeliveredDV<1e-6) Finish(j,"Complete");
            else if (allowed<=duration+1e-8) Finish(j,"Stopped: propellant depleted");
            if (TimeWarp.CurrentRate>100 && HighLogic.LoadedSceneIsFlight) TimeWarp.SetRate(0,true);
        }
        void Finish(BurnJob j, string status)
        {
            if(j.Finished)return;
            j.Status=status; j.Finished=true; Debug.Log("[PersistentFlight] " + status + " " + j.VesselId + " delivered=" + j.DeliveredDV);
            FinishBackgroundReport(j,status);
            RecordBurnFinish(j,status);
        }
    }
}
