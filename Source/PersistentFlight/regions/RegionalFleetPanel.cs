using System;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEngine;
namespace PersistentFlight.Regions
{
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed partial class RegionalFleetPanel:MonoBehaviour
    {
        bool open,chartSpeed;Vector2 scroll;string message="",lat="",lon="";Guid selected;
        Vessel[] cachedFleet=new Vessel[0]; float nextFleetRefresh; int fleetPage; bool showPerformance;
        Rect window=new Rect(16,112,570,660);
        const string SaveName="PersistentFlight-Fleet",InputLock="PF-MissionControl";
        static readonly CultureInfo C=CultureInfo.InvariantCulture;
        public void OnGUI(){
            // Scene transitions can show the native UI directly, without a
            // matching onShowUI event. Use its actual visibility when available.
            bool hidden=KSP.UI.UIMasterController.Instance==null ? uiHidden : !KSP.UI.UIMasterController.Instance.IsUIShowing;
            if(FleetSpectator.Instance!=null)FleetSpectator.Instance.Visible=open&&showSpectator&&!showReadiness&&!hidden;
            if(hidden||RegionFlightRunner.Instance==null||!FlightGlobals.ready){InputLockManager.RemoveControlLock(InputLock);return;}
            if(!open){InputLockManager.RemoveControlLock(InputLock);return;}
            if(Event.current.type==EventType.Layout&&Time.realtimeSinceStartup>=nextFleetRefresh) { cachedFleet=FlightGlobals.Vessels.Where(v=>v!=null&&InFleet(v)).ToArray(); nextFleetRefresh=Time.realtimeSinceStartup+.5f; }
            var fleet=cachedFleet;
            window.width=Math.Max(200,Math.Min(570,Screen.width-32));window.height=Math.Max(180,Math.Min(660,Screen.height-130));
            ClampWindow();
            window=GUILayout.Window(558143,window,id=>Draw(fleet),"PersistentFlight mission control");
            ClampWindow();
            if(Event.current.type==EventType.MouseUp)SavePosition();
            if(open&&window.Contains(Event.current.mousePosition))InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS,InputLock);
            else InputLockManager.RemoveControlLock(InputLock);
        }
        void Draw(Vessel[] fleet){
            if(GUILayout.Button(showReadiness?"Back to fleet":"Flight readiness / Recovery")){showReadiness=!showReadiness;nextReadiness=0;restoreName="";}
            if(showReadiness){readinessScroll=GUILayout.BeginScrollView(readinessScroll,GUILayout.Height(Math.Max(100,window.height-100)));DrawReadiness();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(GUILayout.Button(showSpectator?"Back to fleet":"Fleet spectator")){showSpectator=!showSpectator;spectatorScroll=Vector2.zero;}
            if(showSpectator){spectatorScroll=GUILayout.BeginScrollView(spectatorScroll,GUILayout.Height(Math.Max(100,window.height-100)));DrawSpectator();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(GUILayout.Button(showDebriefs?"Back to fleet":"Flight debrief")){showDebriefs=!showDebriefs;debriefScroll=Vector2.zero;}
            if(showDebriefs){debriefScroll=GUILayout.BeginScrollView(debriefScroll,GUILayout.Height(Math.Max(120,window.height-100)));DrawDebriefs();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(GUILayout.Button(showTemplates?"Back to fleet":"Mission templates")){showTemplates=!showTemplates;templateScroll=Vector2.zero;nextTemplates=0;}
            if(showTemplates){templateScroll=GUILayout.BeginScrollView(templateScroll,GUILayout.Height(Math.Max(120,window.height-160)));DrawTemplates();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(GUILayout.Button(showRoutes?"Back to fleet":"Rover routes")){showRoutes=!showRoutes;showEvents=false;routeScroll=Vector2.zero;}
            if(showRoutes){routeScroll=GUILayout.BeginScrollView(routeScroll,GUILayout.Height(Math.Max(120,window.height-120)));DrawRoverRoutes();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(GUILayout.Button(showEvents?"Back to fleet":"Event missions / Timeline")){showEvents=!showEvents;eventScroll=Vector2.zero;}
            if(showEvents){eventScroll=GUILayout.BeginScrollView(eventScroll,GUILayout.Height(Math.Max(120,window.height-120)));DrawEvents();GUILayout.EndScrollView();GUILayout.Label(message);if(GUILayout.Button("Close"))SetOpen(false);GUI.DragWindow(new Rect(0,0,window.width-70,22));return;}
            if(fleet.Length==0){
                GUILayout.Label("No craft have automatic flight enabled. Enable it on a craft's kOS CPU to add it here.");
                if(GUILayout.Button("Close"))SetOpen(false);
                GUI.DragWindow(new Rect(0,0,window.width-70,22));return;
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Record fleet"))foreach(var v in fleet)foreach(var m in MissionControl.Modules(v))m.recordFlight=true;
            if(GUILayout.Button("Stop recording"))foreach(var v in fleet)foreach(var m in MissionControl.Modules(v))m.recordFlight=false;
            if(GUILayout.Button("Close",GUILayout.Width(65)))SetOpen(false);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            int pages=Math.Max(1,(fleet.Length+11)/12); fleetPage=Math.Max(0,Math.Min(fleetPage,pages-1));
            if(GUILayout.Button("Previous",GUILayout.Width(75))){fleetPage=Math.Max(0,fleetPage-1);scroll=Vector2.zero;}
            GUILayout.Label("Fleet "+fleet.Length+" — page "+(fleetPage+1)+" / "+pages);
            if(GUILayout.Button("Next",GUILayout.Width(60))){fleetPage=Math.Min(pages-1,fleetPage+1);scroll=Vector2.zero;}
            if(GUILayout.Button(showPerformance?"Hide timing":"Performance",GUILayout.Width(100)))showPerformance=!showPerformance;
            GUILayout.EndHorizontal();
            scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(Math.Max(120,window.height-260)));
            if(showPerformance)DrawPerformance();
            foreach(var v in fleet.Skip(fleetPage*12).Take(12)){
                if(v==null)continue;
                var modules=MissionControl.Modules(v);FleetSample s=null;
                if(FleetRecorder.Instance!=null)FleetRecorder.Instance.Current.TryGetValue(v.id,out s);
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.BeginHorizontal();GUILayout.Label(v.vesselName);
                bool previous=GUI.enabled;GUI.enabled=v!=FlightGlobals.ActiveVessel&&TimeWarp.CurrentRate==1;
                if(GUILayout.Button(v==FlightGlobals.ActiveVessel?"Focused":v.loaded&&!v.packed?"Switch here":"Visit craft",GUILayout.Width(95)))Try(()=>FleetDirector.Visit(v),"Focus switched; distant travel restores the saved fleet.");
                GUI.enabled=previous;GUILayout.EndHorizontal();
                if(s!=null){GUILayout.Label(s.Body+"  |  "+s.Physics+"  |  "+s.Phase);
                    GUILayout.Label("Altitude "+Distance(s.Altitude)+"    Speed "+s.Speed.ToString("F0")+" m/s");
                    GUILayout.Label("Orbit "+Distance(s.Periapsis)+" × "+Distance(s.Apoapsis)+"    Fuel "+s.Fuel.ToString("F0")+" / "+s.FuelCapacity.ToString("F0")+"    Power "+s.Power.ToString("F0")+" / "+s.PowerCapacity.ToString("F0"));
                    GUILayout.Label(s.NodeState+(modules.Any(m=>m.nodeRequested)?" — "+s.RemainingDV.ToString("F1")+" m/s remaining":""));}
                bool recording=modules.Any(m=>m.recordFlight);bool requested=GUILayout.Toggle(recording,"Record this craft");if(recording!=requested)foreach(var m in modules)m.recordFlight=requested;
                if(GUILayout.Button(selected==v.id?"Hide mission actions":"Mission actions")){selected=selected==v.id?Guid.Empty:v.id;var site=modules.FirstOrDefault();if(site!=null){lat=site.landingLatitude.ToString("R",C);lon=site.landingLongitude.ToString("R",C);}}
                if(selected==v.id&&modules.Length>0){Actions(v,modules);ExtendedActions(v,modules);}GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            if(FleetDirector.Instance!=null)GUILayout.Label(FleetDirector.Instance.WarpStatus);
            GUILayout.BeginHorizontal();if(GUILayout.Button(chartSpeed?"Show altitude chart":"Show speed chart"))chartSpeed=!chartSpeed;
            if(GUILayout.Button("Copy log folder")&&FleetRecorder.Instance!=null&&!String.IsNullOrEmpty(FleetRecorder.Instance.LogDirectory)){GUIUtility.systemCopyBuffer=FleetRecorder.Instance.LogDirectory;message="Log-folder path copied.";}
            GUILayout.EndHorizontal();Chart();
            GUILayout.BeginHorizontal();bool enabled=GUI.enabled;GUI.enabled=TimeWarp.CurrentRate==1;
            if(GUILayout.Button("Save fleet"))Save();GUI.enabled=GUI.enabled&&File.Exists(Path.Combine(KSPUtil.ApplicationRootPath,"saves/"+HighLogic.SaveFolder+"/"+SaveName+".sfs"));
            if(GUILayout.Button("Load fleet save"))Load();GUI.enabled=enabled;GUILayout.EndHorizontal();GUILayout.Label(message);
            if(FleetRecorder.Instance!=null&&!String.IsNullOrEmpty(FleetRecorder.Instance.LastError)){GUILayout.Label(FleetRecorder.Instance.LastError);if(GUILayout.Button("Retry recording"))FleetRecorder.Instance.Retry();}
            GUI.DragWindow(new Rect(0,0,window.width-70,22));
        }
        void Actions(Vessel v,ModuleRegionalAutonomy[] modules){
            var pilot=MissionControl.Pilot(v);bool enabled=GUI.enabled;
            GUILayout.BeginHorizontal();GUI.enabled=v==FlightGlobals.ActiveVessel&&TimeWarp.CurrentRate==1&&pilot!=null&&!modules.Any(m=>m.nodeRequested);
            if(GUILayout.Button("Queue next maneuver"))Try(()=>MissionControl.QueueNext(v,pilot),"Maneuver captured. You can switch away.");
            GUI.enabled=modules.Any(m=>m.nodeRequested);if(GUILayout.Button("Cancel maneuver"))foreach(var m in modules)m.CancelNode("Cancelled by player");GUI.enabled=enabled;GUILayout.EndHorizontal();
            if(pilot==null)GUILayout.Label("Maneuvers need an idle CPU running pf_mission_pilot.ks.");
            GUILayout.Label("Landing site: "+modules[0].landingSite+" ("+modules[0].landingBody+")");
            GUILayout.BeginHorizontal();GUI.enabled=v.mainBody.bodyName=="Kerbin";
            if(GUILayout.Button("KSC grass"))foreach(var m in modules)MissionControl.SetSite(m,"KSC grass",v.mainBody,-.2187,-74.5732);
            if(GUILayout.Button("West of KSC"))foreach(var m in modules)MissionControl.SetSite(m,"West of KSC",v.mainBody,-.1,-75.3);
            if(GUILayout.Button("KSC launchpad"))foreach(var m in modules){MissionControl.SetSite(m,"KSC launchpad",v.mainBody,-.0972,-74.5576);m.landingTakeover=true;}
            GUI.enabled=enabled;if(GUILayout.Button("Here"))foreach(var m in modules)MissionControl.SetSite(m,"Selected coordinates",v.mainBody,v.latitude,v.longitude);
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();GUILayout.Label("Lat",GUILayout.Width(25));lat=GUILayout.TextField(lat,24);GUILayout.Label("Lon",GUILayout.Width(25));lon=GUILayout.TextField(lon,24);
            if(GUILayout.Button("Apply",GUILayout.Width(60)))Try(()=>{double a,b;if(!Double.TryParse(lat,NumberStyles.Float,C,out a)||!Double.TryParse(lon,NumberStyles.Float,C,out b))throw new ArgumentException("Enter latitude and longitude as decimal degrees");foreach(var m in modules)MissionControl.SetSite(m,"Custom site",v.mainBody,a,b);},"Landing coordinates applied.");
            GUILayout.EndHorizontal();if(GUILayout.Button("Use selected surface target"))Try(()=>{var target=FlightGlobals.fetch.VesselTarget==null?null:FlightGlobals.fetch.VesselTarget.GetVessel();foreach(var m in modules)MissionControl.SetTargetSite(m,target);},"Landing target selected.");
            GUILayout.Label("Kalcon reads this site at separation. Choose it before launch.");
        }
        void Chart(){
            var recorder=FleetRecorder.Instance;var rect=GUILayoutUtility.GetRect(window.width-30,105);GUI.Box(rect,"");
            if(recorder==null||recorder.History.Count==0){GUI.Label(rect,"Record a craft to compare flight traces here.");return;}
            var traces=recorder.History.OrderBy(p=>p.Key).Take(8).ToArray();var all=traces.SelectMany(p=>p.Value).ToArray();
            double minUT=all.Min(s=>s.UT),maxUT=all.Max(s=>s.UT),minY=all.Min(s=>chartSpeed?s.Speed:s.Altitude),maxY=all.Max(s=>chartSpeed?s.Speed:s.Altitude);
            GUI.Label(new Rect(rect.x+5,rect.y+2,rect.width-10,18),(chartSpeed?"Orbit speed":"Altitude")+"  "+minY.ToString("F0")+"–"+maxY.ToString("F0")+(chartSpeed?" m/s":" m"));
            var colors=new[]{Color.cyan,Color.yellow,Color.green,Color.magenta,Color.white,Color.red,new Color(1,.6f,0),new Color(.5f,.7f,1)};
            var plot=new Rect(rect.x+8,rect.y+25,rect.width-16,rect.height-(traces.Length>4?65:45));
            for(int i=0;i<traces.Length;i++){var history=traces[i].Value;for(int k=1;k<history.Count;k++){var a=Point(history[k-1],plot,minUT,maxUT,minY,maxY);var b=Point(history[k],plot,minUT,maxUT,minY,maxY);Line(a,b,colors[i]);}
                var old=GUI.color;GUI.color=colors[i];GUI.Label(new Rect(rect.x+8+(i%4)*rect.width/4,rect.y+rect.height-20-(i/4)*16,rect.width/4-5,18),history.Last().Name);GUI.color=old;}
        }
        Vector2 Point(FleetSample s,Rect r,double minX,double maxX,double minY,double maxY){return new Vector2(r.x+(float)((s.UT-minX)/Math.Max(1,maxX-minX))*r.width,r.yMax-(float)(((chartSpeed?s.Speed:s.Altitude)-minY)/Math.Max(1,maxY-minY))*r.height);}
        static void Line(Vector2 a,Vector2 b,Color color){if(Event.current.type!=EventType.Repaint)return;var matrix=GUI.matrix;var previous=GUI.color;GUI.color=color;GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);GUI.DrawTexture(new Rect(a.x,a.y,(b-a).magnitude,1),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=previous;}
        static string Distance(double metres){return Math.Abs(metres)>=1000?(metres/1000).ToString("F1")+" km":metres.ToString("F0")+" m";}
        void Try(Action action,string success){try{action();message=success;}catch(Exception e){message=e.Message;}}
        void Save(){Try(()=>GamePersistence.SaveGame(SaveName,HighLogic.SaveFolder,SaveMode.OVERWRITE),"Fleet saved. Queued mission pilots have a recovery boot; custom scripts need checkpoints.");}
        void Load(){Try(()=>{var game=GamePersistence.LoadGame(SaveName,HighLogic.SaveFolder,true,false);if(game==null)throw new InvalidOperationException("Fleet save unavailable");game.startScene=GameScenes.FLIGHT;HighLogic.CurrentGame=game;game.Start();},"Fleet loaded.");}
    }
}
