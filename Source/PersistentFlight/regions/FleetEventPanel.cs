using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        bool showEvents;Vector2 eventScroll;string ruleName="New mission step",eventName="ORBIT_ESTABLISHED",eventTask="DEPLOY_PAYLOAD",successEvent="PAYLOAD_DEPLOYED",waitSeconds="3600",runSeconds="300";
        uint eventSource,eventTarget;int eventAction;bool eventFailureHold=true;
        static readonly string[] EventActions={"Send task to kOS","Show notification","Hold mission"};
        static readonly string[] EventChoices={"ORBIT_ESTABLISHED","SEPARATED","LANDED","MANEUVER_COMPLETED","BURN_COMPLETED","ARRIVED","WAYPOINT_REACHED","ROVER_RETURNING","ROVER_HOME","ROVER_STUCK","ROVER_TERRAIN_HOLD","ROVER_POWER_HOLD","BODY_CHANGED","VESSEL_CHANGED"};
        int eventChoice;
        void DrawEvents(){
            GUILayout.Label("Event missions and timeline");
            if(PersistentFlightScenario.Instance==null){GUILayout.Label("Waiting for save state");return;}
            if(FleetEventApi.IsHeld){GUILayout.Label("Event missions held: restore a verified complete save. Existing flights remain available.");return;}
            var cpus=FlightGlobals.Vessels.Where(v=>v!=null&&v.loaded).SelectMany(v=>MissionControl.Modules(v)).Where(m=>m.automatic).ToArray();
            if(cpus.Length>0){
                if(!cpus.Any(m=>m.part.flightID==eventSource))eventSource=cpus[0].part.flightID;
                if(!cpus.Any(m=>m.part.flightID==eventTarget))eventTarget=cpus[0].part.flightID;
                GUILayout.Label("Choose the source event and the CPU that handles the next task.");
                CpuPicker("Source",cpus,ref eventSource);CpuPicker("Target",cpus,ref eventTarget);
                ruleName=Field("Step name",ruleName);GUILayout.BeginHorizontal();
                if(GUILayout.Button("Event preset")){eventChoice=(eventChoice+1)%EventChoices.Length;eventName=EventChoices[eventChoice];}
                eventName=GUILayout.TextField(eventName,64);GUILayout.EndHorizontal();
                if(GUILayout.Button(EventActions[eventAction]))eventAction=(eventAction+1)%EventActions.Length;
                eventTask=Field(eventAction==0?"Task name":"Message",eventTask);successEvent=Field("Success event (optional)",successEvent);
                waitSeconds=Field("Wait timeout (game seconds)",waitSeconds);runSeconds=Field("Action timeout (game seconds)",runSeconds);
                eventFailureHold=GUILayout.Toggle(eventFailureHold,"Hold the target mission if this step fails or times out");
                GUILayout.Label("Send task requires a cooperating kOS handler. SEPARATED is script-published; rover routes publish ARRIVED and their milestones. Rules react to future events only.");
                if(GUILayout.Button("Arm mission step"))Try(()=>{
                    FleetEventApi.Register(new FleetEventRuleRequest(Guid.NewGuid(),ruleName,eventSource,eventName,eventTarget,(FleetAction)eventAction,eventTask,successEvent,Double.Parse(waitSeconds,C),Double.Parse(runSeconds,C),eventFailureHold));
                },"Mission step armed.");
            }else GUILayout.Label("Enable automatic flight on a loaded kOS CPU to create a step.");
            if(!String.IsNullOrEmpty(FleetEventDirector.LastError))GUILayout.Label(FleetEventDirector.LastError);
            foreach(var r in FleetEventApi.List()){
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label(r.Request.Name+" — "+r.State+(r.HoldPending?" / hold pending":""));
                GUILayout.Label(r.Request.EventName+" on CPU "+r.Request.SourceCpu+" -> "+r.Request.Task+" on CPU "+r.Request.TargetCpu);
                GUILayout.Label(r.Detail);GUILayout.BeginHorizontal();
                if(r.State==FleetRuleState.Waiting||r.State==FleetRuleState.Ready||r.State==FleetRuleState.Running){if(GUILayout.Button("Cancel step"))Try(()=>FleetEventApi.Cancel(r.Request.Id),"Mission step cancelled. kOS handlers must honor cancellation.");}
                else if(!r.HoldPending&&GUILayout.Button("Remove finished step"))Try(()=>FleetEventApi.Remove(r.Request.Id),"Finished step removed; use a new ID for new work.");
                GUILayout.EndHorizontal();GUILayout.EndVertical();
            }
            if(GUILayout.Button("Export mission timeline"))Try(ExportTimeline,"Timeline exported; path copied.");
            GUILayout.Label("Recent events (last 40 of up to 2,000 saved entries)");
            foreach(var e in FleetEventApi.Timeline().Reverse().Take(40))GUILayout.Label("UT "+e.UT.ToString("F1",C)+" / CPU "+e.Cpu+" / "+e.EventName+": "+e.Detail);
        }
        static string Field(string label,string value){GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(210));value=GUILayout.TextField(value,160);GUILayout.EndHorizontal();return value;}
        static void CpuPicker(string label,ModuleRegionalAutonomy[] cpus,ref uint id){
            uint current=id;int i=Array.FindIndex(cpus,x=>x.part.flightID==current);var m=cpus[Math.Max(0,i)];
            if(GUILayout.Button(label+": "+m.vessel.vesselName+" / CPU "+m.part.flightID))id=cpus[(Math.Max(0,i)+1)%cpus.Length].part.flightID;
        }
        public static string ExportMissionTimeline(){
            string folder=Path.Combine(KSPUtil.ApplicationRootPath,"PersistentFlight/FlightLogs/Timelines");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"timeline-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".csv");
            var csv=new StringBuilder("sequence,timeline_id,ut,cpu_id,vessel_id,rule_id,event,detail\n");
            foreach(var e in FleetEventApi.Timeline())csv.AppendLine(String.Join(",",new[]{e.Sequence.ToString(C),e.TimelineId.ToString(),e.UT.ToString("R",C),e.Cpu.ToString(C),e.VesselId.ToString(),e.RuleId.ToString(),FleetSample.Quote(e.EventName),FleetSample.Quote(e.Detail)}));
            File.WriteAllText(path,csv.ToString(),new UTF8Encoding(false));return path;
        }
        void ExportTimeline(){GUIUtility.systemCopyBuffer=ExportMissionTimeline();}
    }
}
