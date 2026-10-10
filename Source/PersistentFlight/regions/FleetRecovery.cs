using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class FleetRecovery:MonoBehaviour {
        public static FleetRecovery Instance;
        public string Status="Recovery ready";
        public bool Automatic {get;private set;}
        string settings;RecoveryStore store;float lastAutomatic=-1000;
        public void Awake(){
            Instance=this;Automatic=true;
            try{var folder=Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder));store=new RecoveryStore(folder);settings=Path.Combine(folder,"PersistentFlight-Recovery.cfg");if(File.Exists(settings)){var n=ConfigNode.Load(settings);bool enabled;if(n==null||!Boolean.TryParse(n.GetValue("automatic"),out enabled))throw new InvalidDataException("Recovery setting is damaged");Automatic=enabled;}}
            catch(Exception e){Automatic=false;Status=e.Message;}
        }
        public void OnDestroy(){if(Instance==this)Instance=null;}
        public void SetAutomatic(bool enabled){var n=new ConfigNode("PF_RECOVERY");n.AddValue("automatic",enabled);if(!n.Save(settings))throw new IOException("Could not save recovery setting");Automatic=enabled;Status=enabled?"Automatic recovery enabled":"Automatic recovery disabled";}
        public RecoveryPoint[] Points(){return store==null?new RecoveryPoint[0]:store.List();}
        static void Ready(){if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||FlightGlobals.ActiveVessel==null||TimeWarp.CurrentRate!=1)throw new InvalidOperationException("Recovery needs a ready flight at normal time");}
        public RecoveryPoint Capture(string reason){
            Ready();if(store==null)throw new InvalidOperationException("Recovery folder unavailable");
            var point=store.Create(name=>{GamePersistence.SaveGame(name,HighLogic.SaveFolder,SaveMode.OVERWRITE);var node=ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,name+".sfs"));if(node==null||!node.HasNode("GAME")||!node.GetNode("GAME").HasNode("FLIGHTSTATE"))throw new IOException("Recovery save could not be verified");},Planetarium.GetUniversalTime(),FlightGlobals.ActiveVessel.id,reason);
            Status="Saved recovery: "+point.Reason;return point;
        }
        public static void Before(string reason){
            var r=Instance;if(r==null||!r.Automatic)return;
            if(Time.realtimeSinceStartup-r.lastAutomatic<10){r.Status="Recent recovery reused (within 10 seconds)";return;}
            try{r.Capture(reason);r.lastAutomatic=Time.realtimeSinceStartup;}
            catch(Exception e){r.Status="Recovery failed: "+e.Message;Debug.LogWarning("[PF-RECOVERY] "+r.Status);ScreenMessages.PostScreenMessage(r.Status,6,ScreenMessageStyle.UPPER_CENTER);}
        }
        public void Restore(string name){
            Ready();if(store==null)throw new InvalidOperationException("Recovery folder unavailable");
            var point=store.Verify(name);
            var game=GamePersistence.LoadGame(point.Name,HighLogic.SaveFolder,true,false);
            if(game==null||game.flightState==null)throw new InvalidDataException("Recovery game could not be loaded");
            int index=game.flightState.protoVessels.FindIndex(v=>v.vesselID==point.Focus);
            if(index<0)throw new InvalidDataException("Recovery focused craft is missing");
            // Keep a way back, even when automatic snapshots are disabled.
            Capture("Before restoring: "+point.Reason);
            if(FleetSpectator.Instance!=null)FleetSpectator.Instance.StopTour();
            FlightDriver.StartAndFocusVessel(game,index);
        }
    }
}
