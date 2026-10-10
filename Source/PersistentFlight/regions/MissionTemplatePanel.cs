using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace PersistentFlight.Regions {
 public static class MissionTemplateFlight {
  public static string Folder {get {return Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,"PersistentFlight","MissionTemplates"));}}
  static void Idle(Vessel v){
   if(v==null||v!=FlightGlobals.ActiveVessel||!v.loaded||v.packed||TimeWarp.CurrentRate!=1||v.patchedConicSolver==null)throw new InvalidOperationException("Focus an unpacked craft at normal time");
   if(v.LandedOrSplashed||v.altitude<v.mainBody.atmosphereDepth+1000)throw new InvalidOperationException("Use an orbital craft above the atmosphere");
   if(v.ctrlState.mainThrottle!=0||v.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()).Any(e=>e.finalThrust>.01))throw new InvalidOperationException("Stop thrust before using a template");
   if(MissionControl.Modules(v).Any(m=>m.QueueCount>0||m.ActiveRoverRoute))throw new InvalidOperationException("Cancel the active maneuver or rover plan first");
   var s=PersistentFlight.PersistentFlightScenario.Instance;var job=s==null?null:s.Find(v.id);if(job!=null&&!job.Finished)throw new InvalidOperationException("Cancel the existing background burn first");
  }
  public static MissionTemplate Capture(Vessel v,string name){
   Idle(v);var nodes=v.patchedConicSolver.maneuverNodes.OrderBy(n=>n.UT).ToArray();if(nodes.Length<1||nodes.Length>32)throw new InvalidOperationException("Create 1 to 32 future map maneuvers first");
   var t=new MissionTemplate{Id=Guid.NewGuid(),Name=(name??"").Trim(),Body=v.mainBody.bodyName,SourceSMA=v.orbit.semiMajorAxis,SourceEcc=v.orbit.eccentricity,SourceInclination=v.orbit.inclination};
   foreach(var n in nodes){if(n.UT<=Planetarium.GetUniversalTime()+5||n.patch==null||n.nextPatch==null||n.patch.referenceBody!=v.mainBody||n.nextPatch.referenceBody!=v.mainBody)throw new InvalidOperationException("Use future map maneuvers in the current body's frame; open map view and let the solver finish");t.Steps.Add(new TemplateStep{Delay=n.UT-nodes[0].UT,Radial=n.DeltaV.x,Normal=n.DeltaV.y,Prograde=n.DeltaV.z});}t.Validate();return t;
  }
  public static MissionTemplate CreateGoal(Vessel v,string name,TemplateGoal goal,double altitude){
   Idle(v);var t=new MissionTemplate{Id=Guid.NewGuid(),Name=(name??"").Trim(),Body=v.mainBody.bodyName,SourceSMA=v.orbit.semiMajorAxis,SourceEcc=v.orbit.eccentricity,SourceInclination=v.orbit.inclination,Goal=goal,TargetAltitude=altitude};t.Validate();PreviewGoal(v,t);return t;
  }
  public static GoalOrbitPreview PreviewGoal(Vessel v,MissionTemplate t){
   Idle(v);t.Validate();if(t.Body!=v.mainBody.bodyName)throw new InvalidOperationException("Template belongs to "+t.Body+"; focus a craft at that body");
   var o=v.orbit;bool bodyChange=o.patchEndTransition==Orbit.PatchTransitionType.ENCOUNTER||o.patchEndTransition==Orbit.PatchTransitionType.ESCAPE;return GoalOrbitMath.Calculate(t,new GoalOrbitInput{Mu=v.mainBody.gravParameter,BodyRadius=v.mainBody.Radius,AtmosphereDepth=v.mainBody.atmosphereDepth,SphereOfInfluence=v.mainBody.sphereOfInfluence,SMA=o.semiMajorAxis,Eccentricity=o.eccentricity,Period=o.period,TimeToApoapsis=o.timeToAp,TimeToPeriapsis=o.timeToPe,Now=Planetarium.GetUniversalTime(),BodyChangeUT=bodyChange?o.EndUT:0});
  }
  public static ManeuverNode[] Apply(Vessel v,MissionTemplate t,double delay){
   Idle(v);t.Validate();if(t.Body!=v.mainBody.bodyName)throw new InvalidOperationException("Template belongs to "+t.Body+"; focus a craft at that body");
   if(v.patchedConicSolver.maneuverNodes.Count!=0)throw new InvalidOperationException("Review or remove the existing map plan before applying a template");
   double[] times;TemplateStep[] steps;if(t.Goal==TemplateGoal.Sequence){times=t.Times(Planetarium.GetUniversalTime(),delay);steps=t.Steps.ToArray();}else{var p=PreviewGoal(v,t);times=p.Burns.Select(b=>b.UT).ToArray();steps=p.Burns.Select(b=>new TemplateStep{Prograde=b.Prograde}).ToArray();}
   var made=new List<ManeuverNode>();
   MapView.EnterMapView();try{for(int i=0;i<steps.Length;i++){var s=steps[i];var n=v.patchedConicSolver.AddManeuverNode(times[i]);made.Add(n);n.OnGizmoUpdated(new Vector3d(s.Radial,s.Normal,s.Prograde),n.UT);}v.patchedConicSolver.UpdateFlightPlan();return made.ToArray();}
   catch{foreach(var n in made.ToArray())try{n.RemoveSelf();}catch{}v.patchedConicSolver.UpdateFlightPlan();throw;}
  }
 }
 public sealed partial class RegionalFleetPanel {
  bool showTemplates;Vector2 templateScroll;string templateName="My maneuver sequence",templateDelay="120",goalAltitude="100",templateError="",templateFolder="";int goalChoice;MissionTemplate[] templates=new MissionTemplate[0];string[] templateWarnings=new string[0];Guid selectedTemplate,deleteTemplate;float nextTemplates,nextTemplateForecast;Guid forecastTemplate,forecastVessel;PersistentFlight.FuelForecastResult templateForecast;
  void ReloadTemplates(){templateFolder=MissionTemplateFlight.Folder;templates=MissionTemplateStore.List(templateFolder,out templateWarnings);templateError="";nextTemplates=Time.realtimeSinceStartup+2;}
  void DrawTemplates(){
   if(templateFolder!=MissionTemplateFlight.Folder||Time.realtimeSinceStartup>=nextTemplates)try{ReloadTemplates();}catch(Exception e){templateError=MissionControl.Text(e.Message);nextTemplates=Time.realtimeSinceStartup+2;templates=new MissionTemplate[0];}
   GUILayout.Label("Reusable maneuver and orbital goal templates");GUILayout.Label("Save a map sequence or an orbital goal. Goals calculate fresh burns from the focused craft's orbit. Preview, create a map plan, then review and queue it in Mission actions.",new GUIStyle(GUI.skin.label){wordWrap=true});
   GUILayout.BeginHorizontal();templateName=GUILayout.TextField(templateName,80);if(GUILayout.Button("Save map plan"))Try(()=>{var t=MissionTemplateFlight.Capture(FlightGlobals.ActiveVessel,templateName);MissionTemplateStore.Save(MissionTemplateFlight.Folder,t);selectedTemplate=t.Id;ReloadTemplates();},"Maneuver template saved.");GUILayout.EndHorizontal();
   GUILayout.Label("Save an orbital goal using the name above");goalChoice=GUILayout.SelectionGrid(goalChoice,new[]{"Circularize at Ap","Circularize at Pe","Set periapsis at Ap","Set apoapsis at Pe","Circular orbit at altitude"},2);
   GUILayout.BeginHorizontal();if(goalChoice>=2){GUILayout.Label("Target altitude (km)",GUILayout.Width(140));goalAltitude=GUILayout.TextField(goalAltitude,12);}if(GUILayout.Button("Save orbital goal"))Try(()=>{double altitude=0;if(goalChoice>=2&&(!Double.TryParse(goalAltitude,System.Globalization.NumberStyles.Float,C,out altitude)||Double.IsNaN(altitude)||Double.IsInfinity(altitude)))throw new ArgumentException("Enter a finite target altitude in kilometers");var t=MissionTemplateFlight.CreateGoal(FlightGlobals.ActiveVessel,templateName,(TemplateGoal)(goalChoice+1),altitude*1000);MissionTemplateStore.Save(MissionTemplateFlight.Folder,t);selectedTemplate=t.Id;ReloadTemplates();},"Orbital goal saved. Select it to preview this craft's calculated burn.");GUILayout.EndHorizontal();
   if(GUILayout.Button("Copy library folder"))GUIUtility.systemCopyBuffer=MissionTemplateFlight.Folder;
   if(templateError!="")GUILayout.Label(templateError);foreach(var warning in templateWarnings)GUILayout.Label("Preserved unreadable file: "+warning);
   foreach(var t in templates){if(GUILayout.Button((selectedTemplate==t.Id?"> ":"")+t.Name+" / "+t.Body+" / "+(t.Goal==TemplateGoal.Sequence?t.Steps.Count+" maneuvers":GoalOrbitMath.Label(t.Goal))+" / "+t.Id.ToString("N").Substring(0,6))){selectedTemplate=t.Id;deleteTemplate=Guid.Empty;}}
   var chosen=templates.FirstOrDefault(t=>t.Id==selectedTemplate);if(chosen==null){GUILayout.Label("Save a future map plan to create a template. Up to 100 files per campaign.");return;}
   GUILayout.Label("Source orbit: SMA "+chosen.SourceSMA.ToString("F0",C)+" m / eccentricity "+chosen.SourceEcc.ToString("F3",C)+" / inclination "+chosen.SourceInclination.ToString("F2",C)+"°");
   if(chosen.Goal!=TemplateGoal.Sequence){DrawGoalTemplate(chosen);DrawTemplateRemoval(chosen);return;}
   GUILayout.BeginHorizontal();GUILayout.Label("First burn in seconds",GUILayout.Width(140));templateDelay=GUILayout.TextField(templateDelay,12);GUILayout.EndHorizontal();
   var v=FlightGlobals.ActiveVessel;if(v!=null&&v.mainBody!=null){GUILayout.Label("Destination: "+v.vesselName+" / "+v.mainBody.bodyName);if(v.mainBody.bodyName!=chosen.Body)GUILayout.Label("Body mismatch: this template cannot be applied here.");else GUILayout.Label("Current orbit: SMA "+v.orbit.semiMajorAxis.ToString("F0",C)+" m / eccentricity "+v.orbit.eccentricity.ToString("F3",C)+" / inclination "+v.orbit.inclination.ToString("F2",C)+"°");var m=MissionControl.Modules(v).FirstOrDefault();if(m!=null){if(templateForecast==null||forecastTemplate!=chosen.Id||forecastVessel!=v.id||Time.realtimeSinceStartup>=nextTemplateForecast){templateForecast=FleetFuelForecast.Evaluate(m,chosen.Steps.Sum(s=>s.DV));forecastTemplate=chosen.Id;forecastVessel=v.id;nextTemplateForecast=Time.realtimeSinceStartup+1;}GUILayout.Label("Current craft fuel estimate: "+templateForecast.State+" / "+templateForecast.Reason);}}
   foreach(var s in chosen.Steps)GUILayout.Label("+"+s.Delay.ToString("F1",C)+" s: radial "+s.Radial.ToString("F2",C)+" / normal "+s.Normal.ToString("F2",C)+" / prograde "+s.Prograde.ToString("F2",C)+" m/s");
   GUILayout.Label("The same timing and delta-v can produce a different orbit or encounter. Review the map plan and reserve forecast before queuing.",new GUIStyle(GUI.skin.label){wordWrap=true});
   if(GUILayout.Button("Apply to focused craft — create map plan"))Try(()=>{double delay;if(!Double.TryParse(templateDelay,System.Globalization.NumberStyles.Float,C,out delay))throw new ArgumentException("Enter a valid first-burn delay");var fresh=MissionTemplateStore.Load(MissionTemplateFlight.Folder,chosen.Id);if(fresh.Checksum!=chosen.Checksum)throw new InvalidOperationException("Template changed; reload the library before applying");MissionTemplateFlight.Apply(FlightGlobals.ActiveVessel,fresh,delay);},"Template applied. Review the new map plan, then queue it in Mission actions.");
   DrawTemplateRemoval(chosen);
  }
  void DrawGoalTemplate(MissionTemplate chosen){
   GUILayout.Label(GoalOrbitMath.Label(chosen.Goal)+(chosen.TargetAltitude>0?" / target "+(chosen.TargetAltitude/1000).ToString("F2",C)+" km":""));
   GoalOrbitPreview p=null;var v=FlightGlobals.ActiveVessel;try{p=MissionTemplateFlight.PreviewGoal(v,chosen);}catch(Exception e){GUILayout.Label("Preview unavailable: "+MissionControl.Text(e.Message));}
   if(p==null)return;
   GUILayout.Label("Destination: "+v.vesselName+" / "+v.mainBody.bodyName);
   GUILayout.Label("Calculated plan: "+p.Burns.Length+" burn(s) / total "+p.DV.ToString("F2",C)+" m/s");
   for(int i=0;i<p.Burns.Length;i++){var b=p.Burns[i];GUILayout.Label("Burn "+(i+1)+": in "+(b.UT-Planetarium.GetUniversalTime()).ToString("F0",C)+" s / "+Math.Abs(b.Prograde).ToString("F2",C)+" m/s "+(b.Prograde>=0?"prograde":"retrograde"));if(i<p.Burns.Length-1)GUILayout.Label("Transfer orbit: Pe "+(b.PeriapsisAltitude/1000).ToString("F2",C)+" km / Ap "+(b.ApoapsisAltitude/1000).ToString("F2",C)+" km");}
   GUILayout.Label("Expected orbit: periapsis "+(p.PeriapsisAltitude/1000).ToString("F2",C)+" km / apoapsis "+(p.ApoapsisAltitude/1000).ToString("F2",C)+" km");
   var m=MissionControl.Modules(v).FirstOrDefault();if(m!=null){if(templateForecast==null||forecastTemplate!=chosen.Id||forecastVessel!=v.id||Time.realtimeSinceStartup>=nextTemplateForecast){templateForecast=FleetFuelForecast.Evaluate(m,p.DV);forecastTemplate=chosen.Id;forecastVessel=v.id;nextTemplateForecast=Time.realtimeSinceStartup+1;}GUILayout.Label("Current craft fuel estimate: "+templateForecast.State+" / "+templateForecast.Reason);}
   GUILayout.Label("Plans allow at least 60 seconds before and between burns. Preview assumes instantaneous burns; review the native map orbit and fuel reserve before queuing.",new GUIStyle(GUI.skin.label){wordWrap=true});
   if(GUILayout.Button("Calculate fresh goal — create map plan"))Try(()=>{var fresh=MissionTemplateStore.Load(MissionTemplateFlight.Folder,chosen.Id);if(fresh.Checksum!=chosen.Checksum)throw new InvalidOperationException("Template changed; reload the library before applying");MissionTemplateFlight.Apply(FlightGlobals.ActiveVessel,fresh,0);},"Calculated map plan created. Review it, then queue it in Mission actions.");
  }
  void DrawTemplateRemoval(MissionTemplate chosen){if(deleteTemplate==chosen.Id){GUILayout.BeginHorizontal();if(GUILayout.Button("Delete this saved template"))Try(()=>{MissionTemplateStore.Delete(MissionTemplateFlight.Folder,chosen.Id,chosen.Checksum);selectedTemplate=deleteTemplate=Guid.Empty;ReloadTemplates();},"Template deleted.");if(GUILayout.Button("Keep template"))deleteTemplate=Guid.Empty;GUILayout.EndHorizontal();}else if(GUILayout.Button("Remove template…"))deleteTemplate=chosen.Id;}
 }
}
