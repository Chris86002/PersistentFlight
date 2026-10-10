using System;
using System.Linq;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        bool showReadiness;Vector2 readinessScroll;Guid readinessCraft;string restoreName="";
        RecoveryPoint[] recoveryPoints=new RecoveryPoint[0];ReadinessRow[] readinessRows=new ReadinessRow[0];float nextReadiness;
        void DrawReadiness(){
            var recovery=FleetRecovery.Instance;
            var fleet=FlightGlobals.Vessels.Where(v=>v!=null&&(v==FlightGlobals.ActiveVessel||InFleet(v))).OrderBy(v=>v.vesselName,StringComparer.OrdinalIgnoreCase).ToArray();
            var selectedCraft=fleet.FirstOrDefault(v=>v.id==readinessCraft)??FlightGlobals.ActiveVessel;
            if(selectedCraft!=null)readinessCraft=selectedCraft.id;
            if(Event.current.type==EventType.Layout&&Time.realtimeSinceStartup>=nextReadiness){
                try{readinessRows=FlightReadiness.Inspect(selectedCraft);recoveryPoints=recovery==null?new RecoveryPoint[0]:recovery.Points();}catch(Exception e){message=e.Message;}
                nextReadiness=Time.realtimeSinceStartup+2;
            }
            GUILayout.Label("Flight readiness — "+(selectedCraft==null?"No craft":selectedCraft.vesselName));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Previous craft"))SelectReadiness(fleet,-1);
            if(GUILayout.Button("Focused craft")){readinessCraft=FlightGlobals.ActiveVessel.id;nextReadiness=0;}
            if(GUILayout.Button("Next craft"))SelectReadiness(fleet,1);
            GUILayout.EndHorizontal();
            GUILayout.Label(readinessRows.Any(r=>r.Attention)?"Review the checks below before starting a maneuver.":"Listed checks passed. Review the scope below.");
            foreach(var row in readinessRows){GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label((row.Attention?"CHECK — ":"")+row.Item);GUILayout.Label(row.Detail);GUILayout.EndVertical();}
            GUILayout.Label("Recovery saves — files checked before restore");
            GUILayout.Label("A restore rewinds the entire game save, including every craft and mission. Custom scripts still need recovery boots/checkpoints.");
            if(recovery==null){GUILayout.Label("Recovery service unavailable");return;}
            bool enabled=GUI.enabled;GUI.enabled=TimeWarp.CurrentRate==1;
            bool auto=GUILayout.Toggle(recovery.Automatic,"Automatic saves when queuing maneuvers, before pilot burns and distant visits");
            if(auto!=recovery.Automatic)Try(()=>recovery.SetAutomatic(auto),"Recovery preference saved for this game.");
            if(GUILayout.Button("Create recovery point now"))Try(()=>{recovery.Capture("Manual recovery");nextReadiness=0;},"Recovery point saved.");
            foreach(var p in recoveryPoints){
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(new DateTime(p.Created,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+"  |  UT "+p.UT.ToString("F1"));GUILayout.Label(p.Reason);
                if(restoreName==p.Name){GUILayout.Label("Restore this point and rewind all flight progress? A recovery of the current state will be saved first.");GUILayout.BeginHorizontal();if(GUILayout.Button("Confirm restore"))Try(()=>{recovery.Restore(p.Name);restoreName="";},"Restoring recovery point.");if(GUILayout.Button("Cancel"))restoreName="";GUILayout.EndHorizontal();}
                else if(GUILayout.Button("Restore this point"))restoreName=p.Name;
                GUILayout.EndVertical();
            }
            GUI.enabled=enabled;GUILayout.Label(recovery.Status);
        }
        void SelectReadiness(Vessel[] fleet,int direction){if(fleet.Length==0)return;int i=Array.FindIndex(fleet,v=>v.id==readinessCraft);readinessCraft=fleet[(Math.Max(0,i)+direction+fleet.Length)%fleet.Length].id;nextReadiness=0;}
    }
}
