using System;
using System.Linq;
using System.Globalization;
using System.Reflection;
namespace PersistentFlight {
    public partial class PersistentFlightScenario {
        public FlightDebriefLedger Debriefs=new FlightDebriefLedger();
        public string DebriefError="";
        ConfigNode rawDebriefs;
        static readonly CultureInfo DC=CultureInfo.InvariantCulture;
        static readonly FieldInfo[] DebriefFields=typeof(FlightDebrief).GetFields(BindingFlags.Instance|BindingFlags.Public);
        public void ObserveDebrief(Action action){
            if(rawDebriefs!=null)return;
            try{action();}catch(Exception e){DebriefError="Debrief recording: "+e.Message;UnityEngine.Debug.LogError("[PF-DEBRIEF] "+DebriefError);}
        }
        void LoadDebriefs(ConfigNode node){
            Debriefs=new FlightDebriefLedger();rawDebriefs=null;DebriefError="";
            var roots=node.GetNodes("FLIGHT_DEBRIEFS");
            if(roots.Length==0&&!node.HasValue("debriefVersion"))return;
            try{
                if(roots.Length!=1||(node.GetValue("debriefVersion")!="1"&&node.GetValue("debriefVersion")!="2")||node.GetValue("debriefHeld")=="True")throw new FormatException("Unsupported or held saved debriefs");
                var rows=roots[0].GetNodes("BURN_REPORT");if(rows.Length>FlightDebriefLedger.Limit||Int32.Parse(roots[0].GetValue("count"),DC)!=rows.Length)throw new FormatException("Debrief count mismatch");
                var restored=new FlightDebriefLedger();
                foreach(var row in rows){
                    var r=new FlightDebrief();
                    foreach(var f in DebriefFields){
                        if(node.GetValue("debriefVersion")=="1"&&FlightDebrief.LandingField(f.Name))continue;
                        if(row.GetValues(f.Name).Length!=1)throw new FormatException("Missing or duplicate "+f.Name);
                        string v=row.GetValue(f.Name);object value;
                        if(f.FieldType==typeof(Guid))value=Guid.Parse(v);
                        else if(f.FieldType.IsEnum)value=Enum.Parse(f.FieldType,v);
                        else value=Convert.ChangeType(v,f.FieldType,DC);
                        f.SetValue(r,value);
                    }
                    r.Validate();if(r.Mode==DebriefMode.Landing&&!r.Finished){r.Finished=true;r.Incomplete=true;r.EndUT=Math.Max(r.StartUT,Planetarium.GetUniversalTime());r.Outcome="Interrupted: save reloaded during approach";}if(r.Started&&!r.Finished&&r.Mode==DebriefMode.LiveManeuver)r.Incomplete=true;
                    restored.Add(r);
                }
                Debriefs=restored;
            }catch(Exception e){rawDebriefs=node.CreateCopy();DebriefError="Saved debriefs could not be restored; recording held. Flight controls remain available. "+e.Message;UnityEngine.Debug.LogError("[PF-DEBRIEF] "+DebriefError);}
        }
        void SaveDebriefs(ConfigNode node){
            node.AddValue("debriefVersion",2);node.AddValue("debriefHeld",rawDebriefs!=null);
            if(rawDebriefs!=null){foreach(var root in rawDebriefs.GetNodes("FLIGHT_DEBRIEFS"))node.AddNode(root.CreateCopy());return;}
            var n=node.AddNode("FLIGHT_DEBRIEFS");n.AddValue("count",Debriefs.Records.Count);
            foreach(var r in Debriefs.Records){var row=n.AddNode("BURN_REPORT");foreach(var f in DebriefFields){object v=f.GetValue(r);row.AddValue(f.Name,v is double?((double)v).ToString("R",DC):Convert.ToString(v,DC));}}
        }
        FlightDebrief BackgroundReport(BurnJob j){
            var r=Debriefs.Find(j.DebriefId);if(r!=null)return r;
            var v=FlightGlobals.Vessels==null?null:FlightGlobals.Vessels.FirstOrDefault(x=>x!=null&&x.id==j.VesselId);
            r=new FlightDebrief{Id=Guid.NewGuid(),Branch=Debriefs.Branch,Vessel=j.VesselId,Craft=v==null?j.VesselId.ToString():ReportText(v.vesselName),Body=v==null?j.BodyIndex.ToString():ReportText(v.mainBody.bodyName),Mode=DebriefMode.BackgroundBurn,PlannedDV=j.TargetDV,QueuedUT=Math.Min(Planetarium.GetUniversalTime(),j.StartUT),NodeUT=j.StartUT,ImpulseDV=j.DeliveredDV,Started=j.DeliveredDV>0,StartUT=j.DeliveredDV>0?j.StartUT:0,Incomplete=j.DeliveredDV>0,ResourcesKnown=j.DeliveredDV==0};
            Debriefs.Add(r);j.DebriefId=r.Id;return r;
        }
        public static string ReportText(string value){return (value??"").Replace('\r',' ').Replace('\n',' ').Substring(0,Math.Min(240,(value??"").Length));}
        void RecordBackgroundStep(BurnJob j,double duration,double lf,double ox,double ec){ObserveDebrief(()=>{
            var r=BackgroundReport(j);if(!r.Started){r.Started=true;r.StartUT=j.LastUT-duration;}
            r.ImpulseDV=j.DeliveredDV;r.PoweredSeconds+=duration;r.LiquidFuel+=lf;r.Oxidizer+=ox;r.ElectricCharge+=ec;r.Outcome="Burning";
        });}
        void FinishBackgroundReport(BurnJob j,string status){ObserveDebrief(()=>{var r=BackgroundReport(j);if(r.Finished)return;r.ImpulseDV=j.DeliveredDV;r.ResidualDV=Math.Max(0,j.TargetDV-j.DeliveredDV);r.EndUT=Planetarium.GetUniversalTime();r.Outcome=ReportText(status);r.Finished=true;});}
    }
}
