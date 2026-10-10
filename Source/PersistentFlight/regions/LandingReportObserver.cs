using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
 public sealed partial class ModuleRegionalAutonomy {
  [KSPField(isPersistant=true)] public bool landingReports=true;
 }
 [KSPAddon(KSPAddon.Startup.Flight,false)]
 public sealed class LandingReportObserver:MonoBehaviour {
  sealed class Approach {public Vessel Vessel;public ModuleRegionalAutonomy Cpu;public FlightDebrief Report;public PersistentFlightScenario Store;public uint[] Parts;public double ContactUT=-1,LastUT;}
  readonly Dictionary<Guid,Approach> active=new Dictionary<Guid,Approach>();
  void Start(){GameEvents.onVesselDestroy.Add(Destroyed);}
  void OnDestroy(){GameEvents.onVesselDestroy.Remove(Destroyed);foreach(var a in active.Values.ToArray())Finish(a,"Interrupted: flight scene left",true);}
  void Destroyed(Vessel v){Approach a;if(v!=null&&active.TryGetValue(v.id,out a)){a.Report.LostParts=a.Report.InitialParts;Finish(a,"Failed: vessel destroyed during approach",true);}}
  static double Resource(Vessel v,string name){double cap;return MissionControl.Resource(v,name,out cap);}
  void Finish(Approach a,string outcome,bool partial){
   a.Store.ObserveDebrief(()=>{a.Report.EndUT=Math.Max(a.Report.StartUT,Planetarium.GetUniversalTime());a.Report.Finished=true;a.Report.Outcome=PersistentFlightScenario.ReportText(outcome);a.Report.Incomplete|=partial;});active.Remove(a.Report.Vessel);
  }
  void Sample(Approach a){
   var v=a.Vessel;var r=a.Report;
   r.RemainingLF=Resource(v,"LiquidFuel");r.RemainingOx=Resource(v,"Oxidizer");r.RemainingEC=Resource(v,"ElectricCharge");
   r.LostParts=a.Parts.Count(id=>!v.parts.Any(p=>p.flightID==id));
   r.Latitude=v.latitude;r.Longitude=v.longitude;r.LandingTarget=PersistentFlightScenario.ReportText(a.Cpu.landingSite);
   try{if(v.mainBody.bodyName!=a.Cpu.landingBody)throw new InvalidOperationException("Selected target is on another body");var site=MissionControl.Site(a.Cpu);r.TargetDistance=FlightDebrief.SurfaceDistance(v.mainBody.Radius,v.latitude,v.longitude,site.x,site.y);r.LandingNote="";}
   catch(Exception e){r.TargetDistance=Double.NaN;r.LandingNote=PersistentFlightScenario.ReportText(e.Message);}
  }
  public void FixedUpdate(){
   if(!FlightGlobals.ready||FlightGlobals.Vessels==null)return;
   foreach(var a in active.Values.ToArray())try{
    var v=a.Vessel;double now=Planetarium.GetUniversalTime();
    if(v==null||!FlightGlobals.Vessels.Contains(v)){Finish(a,"Failed: craft unavailable during approach",true);continue;}
    if(v.state==Vessel.State.DEAD||v.rootPart==null||v.rootPart.State==PartStates.DEAD){a.Report.LostParts=a.Report.InitialParts;Finish(a,"Failed: vessel destroyed during approach",true);continue;}
    if(!v.loaded||v.packed||TimeWarp.CurrentRate!=1){Finish(a,"Interrupted: craft unloaded or time warp",true);continue;}
    if(a.Cpu==null||a.Cpu.part==null||a.Cpu.part.State==PartStates.DEAD){Finish(a,"Failed: landing recorder lost during approach",true);continue;}
    if(!a.Cpu.landingReports){Finish(a,"Interrupted: landing recording disabled",true);continue;}
    if(v.id!=a.Report.Vessel||a.Cpu.vessel!=v){Finish(a,"Interrupted: craft identity or recorder changed",true);continue;}
    if(now-a.LastUT>.2||now<a.LastUT)a.Report.Incomplete=true;
    a.LastUT=now;
    a.Store.ObserveDebrief(()=>{
     Sample(a);
     if(v.LandedOrSplashed){
      if(a.ContactUT<0){a.ContactUT=now;a.Report.Contact=v.situation==Vessel.Situations.SPLASHED?"Water":"Ground";}
      if(now-a.ContactUT>=2)Finish(a,a.Report.LostParts>0?"Landed with part loss":"Touchdown complete",false);
     }else if(a.ContactUT>=0){a.Report.Incomplete=true;Finish(a,"Interrupted: bounced or departed before settling",true);}
     else{a.Report.TouchdownSpeed=v.srf_velocity.magnitude;a.Report.VerticalSpeed=v.verticalSpeed;a.Report.HorizontalSpeed=Math.Sqrt(Math.Max(0,v.srf_velocity.sqrMagnitude-v.verticalSpeed*v.verticalSpeed));}
    });
   }catch(Exception e){Finish(a,"Interrupted: "+e.Message,true);}
   foreach(var v in FlightGlobals.Vessels.Where(v=>v!=null&&v.state!=Vessel.State.DEAD&&v.rootPart!=null&&v.rootPart.State!=PartStates.DEAD&&v.loaded&&!v.packed&&!v.LandedOrSplashed&&!active.ContainsKey(v.id)).ToArray()){
    if(TimeWarp.CurrentRate!=1||v.verticalSpeed>=-.1||v.mainBody.pqsController==null)continue;
    double ground=Math.Max(0,v.mainBody.TerrainAltitude(v.latitude,v.longitude,false));if(v.altitude-ground>10000)continue;
    var m=MissionControl.Modules(v).FirstOrDefault(x=>x.landingReports);var s=PersistentFlightScenario.Instance;if(m==null||m.part.State==PartStates.DEAD||s==null)continue;
    s.ObserveDebrief(()=>{
     double now=Planetarium.GetUniversalTime();var r=new FlightDebrief{Id=Guid.NewGuid(),Branch=s.Debriefs.Branch,Vessel=v.id,Cpu=m.part.flightID,Craft=PersistentFlightScenario.ReportText(v.vesselName),Body=PersistentFlightScenario.ReportText(v.mainBody.bodyName),Mode=DebriefMode.Landing,QueuedUT=now,StartUT=now,Started=true,Outcome="Approach observed",InitialParts=v.parts.Count,LandingTarget=PersistentFlightScenario.ReportText(m.landingSite)};
     s.Debriefs.Add(r);var a=new Approach{Vessel=v,Cpu=m,Report=r,Store=s,Parts=v.parts.Select(p=>p.flightID).ToArray(),LastUT=now};active.Add(v.id,a);Sample(a);r.TouchdownSpeed=v.srf_velocity.magnitude;r.VerticalSpeed=v.verticalSpeed;r.HorizontalSpeed=Math.Sqrt(Math.Max(0,v.srf_velocity.sqrMagnitude-v.verticalSpeed*v.verticalSpeed));
    });
   }
  }
 }
}

