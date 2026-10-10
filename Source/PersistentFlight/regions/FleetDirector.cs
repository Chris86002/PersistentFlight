using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace PersistentFlight.Regions {
    public sealed partial class ModuleRegionalAutonomy {
        [KSPField(isPersistant=true)] public bool monitorMission,abortOnCritical,guardWarp=true,warpReady,landingTakeover;
        [KSPField(isPersistant=true)] public double fuelWarning=.1,powerWarning=.1,warpUntil,landingTakeoverHeight=6000;
        [KSPField(isPersistant=true)] public string acknowledgedAlert="";
        public string alert="";
        public float scriptHeartbeat;
        public void ScriptPulse(){scriptHeartbeat=Time.realtimeSinceStartup;}
        public void HoldMission(string reason){
            bool wasHeld=missionHold;
            FlightDebriefRecorder.End(this,"Held: "+reason);
            missionHold=true;warpReady=false;roverHold=true;nodeStatus=reason;
            if(vessel!=null&&vessel.loaded){
                vessel.ctrlState.mainThrottle=0;
                foreach(var e in vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))e.Shutdown();
            }
            if(!wasHeld)FleetEventDirector.Emit(this,"MISSION_HELD",reason);
        }
    }
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FleetDirector:MonoBehaviour {
        public static FleetDirector Instance;public string WarpStatus="Time-warp guard ready",LastError="";
        float nextScan,started;Guid takeover=Guid.Empty;
        public void Awake(){Instance=this;started=Time.realtimeSinceStartup;GameEvents.onTimeWarpRateChanged.Add(ProtectWarp);}
        public void OnDestroy(){GameEvents.onTimeWarpRateChanged.Remove(ProtectWarp);if(Instance==this)Instance=null;}
        public void Update(){
            long stamp=FleetPerformance.Start();try{Tick();}finally{FleetPerformance.Director.Record(stamp);}
        }
        void Tick(){
            if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready)return;
            if(TimeWarp.CurrentRate!=1)started=Time.realtimeSinceStartup;
            ProtectWarp();
            if(Time.realtimeSinceStartup<nextScan)return;nextScan=Time.realtimeSinceStartup+.5f;
            if(SoiFlightPlan.Handoff())return;
            foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded&&RegionalResidency.Armed(v)).ToArray())try{
                foreach(var m in MissionControl.Modules(v))Monitor(m);
                LandingHandoff(v);
            }catch(Exception e){LastError=e.Message;Debug.LogError("[PF-DIRECTOR] "+e);}
        }
        public static void PropellantReserve(Vessel v,out double fraction,out double capacity){
            fraction=1;capacity=0;
            var names=v.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).SelectMany(e=>e.propellants).Where(p=>p.name!="ElectricCharge").Select(p=>p.name).Distinct();
            foreach(var name in names){double c;double amount=MissionControl.Resource(v,name,out c);if(c>0){fraction=Math.Min(fraction,amount/c);capacity=1;}}
        }
        void Monitor(ModuleRegionalAutonomy m){
            FleetFuelForecast.Monitor(m);
            double f,fc,pc;PropellantReserve(m.vessel,out f,out fc);double p=MissionControl.Resource(m.vessel,"ElectricCharge",out pc);
            bool stalled=m.monitorMission&&!m.vessel.packed&&TimeWarp.CurrentRate==1&&Time.realtimeSinceStartup-started>15&&Time.realtimeSinceStartup-m.scriptHeartbeat>15;
            string alert=FleetSafety.Evaluate(f,fc,p,pc,stalled,m.fuelWarning,m.powerWarning);
            if(m.alert!=alert){m.alert=alert;if(alert=="")m.acknowledgedAlert="";if(alert!=""&&m.acknowledgedAlert!=alert){
                ScreenMessages.PostScreenMessage(m.vessel.vesselName+": "+alert,7,ScreenMessageStyle.UPPER_CENTER);
                Debug.LogWarning("[PF-ALERT] "+m.vessel.id+" "+alert);
            }}
            if(m.abortOnCritical&&FleetSafety.Critical(f,fc,p,pc,stalled)&&!m.missionHold)m.HoldMission("Safety hold: "+alert);
            if(!m.missionHold && !m.nodeRequested && m.PilotOnline)MissionQueue.Advance(m);
        }
        public void ProtectWarp(){
            if(TimeWarp.CurrentRate==1){WarpStatus="Warp guard: standing by";return;}
            double now=Planetarium.GetUniversalTime();string reason=null;
            var service=PersistentFlight.PersistentFlightScenario.Instance;
            if(service!=null&&service.Jobs.Any(j=>!j.Finished)&&TimeWarp.CurrentRate>1000)
                reason="Distant burns require 1000x warp or lower";
            foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&RegionalResidency.Armed(v))){
                if(v.loaded){
                    foreach(var m in MissionControl.Modules(v).Where(m=>m.guardWarp)){
                        if(!m.warpReady||m.missionHold||!PersistentFlight.BurnMath.Finite(m.warpUntil)||m.warpUntil<=now+Math.Max(5,TimeWarp.CurrentRate*.1))reason=v.vesselName+": active mission needs normal time";
                    }
                }else if(v.protoVessel!=null){
                    // A packed CPU cannot renew a coast lease or validate its
                    // steering. Never treat saved warpReady as a live lease.
                    if(v.protoVessel.protoPartSnapshots.SelectMany(p=>p.modules).Any(m=>m.moduleName=="ModuleRegionalAutonomy"&&m.moduleValues.GetValue("guardWarp")!="False"))reason=v.vesselName+": live script unavailable";
                }
                if(reason!=null)break;
            }
            if(reason!=null){TimeWarp.SetRate(0,true);started=Time.realtimeSinceStartup;WarpStatus="Warp stopped — "+reason;}
            else WarpStatus="Coasting at "+TimeWarp.CurrentRate+"x; returning to normal before the next burn";
        }
        void LandingHandoff(Vessel v){
            if(v==FlightGlobals.ActiveVessel||v.packed||TimeWarp.CurrentRate!=1||v.mainBody!=FlightGlobals.ActiveVessel.mainBody)return;
            var m=MissionControl.Modules(v).FirstOrDefault(x=>x.landingTakeover);
            if(m==null||v.LandedOrSplashed||v.srf_velocity.magnitude<.1||v.verticalSpeed>=0)return;
            if(v.mainBody.pqsController==null)return;
            double ground=Math.Max(0,v.mainBody.TerrainAltitude(v.latitude,v.longitude,false));
            if(v.altitude-ground>m.landingTakeoverHeight||takeover==v.id)return;
            if(RegionFlightRunner.Instance!=null&&RegionFlightRunner.Instance.Focus(v)){
                takeover=v.id;m.missionPhase="Landing with native surface physics";
                ScreenMessages.PostScreenMessage("Landing focus: "+v.vesselName+". Other regional craft continue.",7,ScreenMessageStyle.UPPER_CENTER);
            }
        }
        public static void Visit(Vessel v){
            if(v==null||v==FlightGlobals.ActiveVessel||TimeWarp.CurrentRate!=1||!FlightGlobals.ready)throw new InvalidOperationException("Visit a different craft at normal time");
            if(v.loaded&&!v.packed&&RegionFlightRunner.Instance.Focus(v))return;
            FleetRecovery.Before("Before distant visit: "+v.vesselName);
            GamePersistence.SaveGame("PersistentFlight-Travel",HighLogic.SaveFolder,SaveMode.OVERWRITE);
            var game=GamePersistence.LoadGame("PersistentFlight-Travel",HighLogic.SaveFolder,true,false);
            if(game==null)throw new InvalidOperationException("Travel snapshot could not be loaded");
            int index=game.flightState.protoVessels.FindIndex(p=>p.vesselID==v.id);
            if(index<0)throw new InvalidOperationException("Craft was not found in the travel snapshot");
            FlightDriver.StartAndFocusVessel(game,index);
        }
    }
}
