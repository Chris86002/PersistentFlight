using System;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
namespace PersistentFlight {
    public interface IRoverRouteOwner { bool ActiveRoverRoute {get;} }
    public sealed class RoverPoint {
        public readonly double Latitude,Longitude;
        public RoverPoint(double latitude,double longitude){if(!FleetEventLedger.Finite(latitude)||!FleetEventLedger.Finite(longitude)||latitude<-90||latitude>90||longitude<-180||longitude>180)throw new ArgumentException("Use valid waypoint coordinates");Latitude=latitude;Longitude=longitude;}
        const double Rad=Math.PI/180;
        public double Distance(RoverPoint other,double radius){double lat=(other.Latitude-Latitude)*Rad,lon=(other.Longitude-Longitude)*Rad;double h=Math.Sin(lat/2)*Math.Sin(lat/2)+Math.Cos(Latitude*Rad)*Math.Cos(other.Latitude*Rad)*Math.Sin(lon/2)*Math.Sin(lon/2);return 2*radius*Math.Asin(Math.Sqrt(Math.Min(1,Math.Max(0,h))));}
        public double Bearing(RoverPoint other){double d=(other.Longitude-Longitude)*Rad;return Math.Atan2(Math.Sin(d)*Math.Cos(other.Latitude*Rad),Math.Cos(Latitude*Rad)*Math.Sin(other.Latitude*Rad)-Math.Sin(Latitude*Rad)*Math.Cos(other.Latitude*Rad)*Math.Cos(d))/Rad;}
        public RoverPoint Offset(double bearing,double metres,double radius){double a=metres/radius,b=bearing*Rad,p=Latitude*Rad;double q=Math.Asin(Math.Max(-1,Math.Min(1,Math.Sin(p)*Math.Cos(a)+Math.Cos(p)*Math.Sin(a)*Math.Cos(b))));double lon=Longitude*Rad+Math.Atan2(Math.Sin(b)*Math.Sin(a)*Math.Cos(p),Math.Cos(a)-Math.Sin(p)*Math.Sin(q));return new RoverPoint(q/Rad,((lon/Rad+540)%360)-180);}
        internal string Encode(){return Latitude.ToString("R",CultureInfo.InvariantCulture)+","+Longitude.ToString("R",CultureInfo.InvariantCulture);}
        internal static RoverPoint Decode(string text){var n=text.Split(',');if(n.Length!=2)throw new FormatException("Invalid saved coordinate");return new RoverPoint(Double.Parse(n[0],CultureInfo.InvariantCulture),Double.Parse(n[1],CultureInfo.InvariantCulture));}
    }
    public sealed class RoverRouteRequest {
        public readonly Guid Id;public readonly string Body;
        public readonly RoverPoint Home;internal readonly RoverPoint[] Nodes;
        public RoverPoint[] Waypoints {get{return Nodes.ToArray();}}
        public readonly double Speed,ArrivalRadius,MaximumSlope,ReserveFraction,StuckSeconds;
        public readonly bool AutoReturn;
        public RoverRouteRequest(Guid id,string body,RoverPoint home,IEnumerable<RoverPoint> waypoints,double speed,double radius,double slope,double reserve,double stuck,bool autoReturn){
            if(id==Guid.Empty||home==null||waypoints==null)throw new ArgumentException("Route needs an identity, home and waypoints");FleetEventLedger.Text(body,64);
            Nodes=waypoints.ToArray();if(Nodes.Length<1||Nodes.Length>64||Nodes.Any(p=>p==null))throw new ArgumentException("Use 1 to 64 waypoints");
            if(!FleetEventLedger.Finite(speed)||speed<.25||speed>5||!FleetEventLedger.Finite(radius)||radius<2||radius>20||!FleetEventLedger.Finite(slope)||slope<2||slope>30||!FleetEventLedger.Finite(reserve)||reserve<.05||reserve>.8||!FleetEventLedger.Finite(stuck)||stuck<15||stuck>600)throw new ArgumentException("Speed .25–5 m/s, radius 2–20 m, slope 2–30 degrees, reserve 5–80%, stuck timeout 15–600 s");
            Id=id;Body=body;Home=home;Speed=speed;ArrivalRadius=radius;MaximumSlope=slope;ReserveFraction=reserve;StuckSeconds=stuck;AutoReturn=autoReturn;
        }
    }
    public enum RoverMode { Outbound,Returning,Complete,Home,Cancelled }
    public enum RoverHold { None,Terrain,Stuck,Power,Body,Clock,Data,TraceFull }
    public sealed class RoverObservation {
        public string Body;public RoverPoint Position;public double UT,Radius,Speed,Power,Capacity,Slope;
        public bool Paused,Supported=true,TerrainValid=true,Water;
    }
    public sealed class RoverDecision {
        public readonly string Status;public readonly double Speed,Distance,Slope,ReturnDistance;
        public readonly RoverPoint Target;public readonly string[] Events;
        internal RoverDecision(string status,double speed,double distance,double slope,double returnDistance,RoverPoint target,IEnumerable<string> events){Status=status;Speed=speed;Distance=distance;Slope=slope;ReturnDistance=returnDistance;Target=target;Events=events.ToArray();}
    }
    public sealed class RoverRoute {
        public const int TraceLimit=1024;
        public readonly RoverRouteRequest Request;
        public RoverMode Mode {get;private set;}
        public RoverHold Hold {get;private set;}
        public int Index {get;private set;}
        public int ReturnIndex {get;private set;}
        public double DistanceDriven {get;private set;}
        public double EnergyUsed {get;private set;}
        public double EnergyPerMetre {get{return DistanceDriven>1?EnergyUsed/DistanceDriven:0;}}
        readonly List<RoverPoint> trace=new List<RoverPoint>();
        bool observed,watch;RoverPoint previous;double lastUT,lastPower,bestDistance,watchStart;
        public RoverPoint[] Trace {get{return trace.ToArray();}}
        public bool Active {get{return Mode==RoverMode.Outbound||Mode==RoverMode.Returning;}}
        public RoverPoint Target {get{return Mode==RoverMode.Returning?trace[Math.Max(0,ReturnIndex)]:Mode==RoverMode.Outbound?Request.Nodes[Index]:Request.Home;}}
        public RoverRoute(RoverRouteRequest request){if(request==null)throw new ArgumentNullException("request");Request=request;trace.Add(request.Home);previous=request.Home;}
        public void ResetObservation(){observed=false;watch=false;}
        public void Resume(){if(!Active)throw new InvalidOperationException("This route has finished; save a new route");Hold=RoverHold.None;ResetObservation();}
        public void Cancel(){Mode=RoverMode.Cancelled;Hold=RoverHold.None;watch=false;}
        public void ReturnHome(){if(Mode==RoverMode.Cancelled||Mode==RoverMode.Home)throw new InvalidOperationException("Route is already closed");Mode=RoverMode.Returning;ReturnIndex=trace.Count-1;watch=false;}
        public double ReturnDistance(RoverPoint current,double radius){double d=current.Distance(trace[Mode==RoverMode.Returning?Math.Max(0,ReturnIndex):trace.Count-1],radius);int top=Mode==RoverMode.Returning?ReturnIndex:trace.Count-1;for(int i=top;i>0;i--)d+=trace[i].Distance(trace[i-1],radius);return d;}
        public RoverDecision Tick(RoverObservation o){
            if(o==null||o.Position==null||!FleetEventLedger.Finite(o.UT)||o.UT<0||!FleetEventLedger.Finite(o.Radius)||o.Radius<=0||!FleetEventLedger.Finite(o.Speed)||o.Speed<0||!FleetEventLedger.Finite(o.Power)||o.Power<0||!FleetEventLedger.Finite(o.Capacity)||o.Capacity<0)throw new ArgumentException("Invalid rover observations");
            var events=new List<string>();double distance=o.Position.Distance(Target,o.Radius);
            if(!Active)return Decision(Mode.ToString().ToUpperInvariant(),0,o,distance,events);
            if(o.Body!=Request.Body)Hold=RoverHold.Body;
            if(observed&&o.UT<lastUT-.01)Hold=RoverHold.Clock;
            if(observed){double moved=o.Position.Distance(previous,o.Radius);if(moved<100&&o.UT-lastUT<=2&&moved>.001){DistanceDriven+=moved;EnergyUsed+=Math.Max(0,lastPower-o.Power);}}
            previous=o.Position;lastUT=o.UT;lastPower=o.Power;observed=true;
            if(o.Paused||!o.Supported){watch=false;return Decision(o.Paused?"PAUSED":"WAITING_FOR_WHEELS",0,o,distance,events);}
            if(Hold!=RoverHold.None){watch=false;return Decision(Hold.ToString().ToUpperInvariant()+"_HOLD",0,o,distance,events);}
            if(o.Capacity<=0||o.Power<=o.Capacity*.01){Hold=RoverHold.Power;events.Add("ROVER_POWER_HOLD");return Decision("POWER_HOLD",0,o,distance,events);}
            if(Mode==RoverMode.Outbound&&(o.Power/o.Capacity<=Request.ReserveFraction||(DistanceDriven>10&&o.Power<=ReturnDistance(o.Position,o.Radius)*EnergyPerMetre*1.5+o.Capacity*.05))){
                if(Request.AutoReturn){ReturnHome();events.Add("ROVER_RETURNING");distance=o.Position.Distance(Target,o.Radius);return Decision("RETURNING",0,o,distance,events);}else{Hold=RoverHold.Power;events.Add("ROVER_POWER_HOLD");return Decision("POWER_HOLD",0,o,distance,events);}
            }
            if(!o.TerrainValid||!FleetEventLedger.Finite(o.Slope)||o.Slope<0||o.Slope>Request.MaximumSlope||o.Water){Hold=RoverHold.Terrain;events.Add("ROVER_TERRAIN_HOLD");return Decision("TERRAIN_HOLD",0,o,distance,events);}
            if(distance<=Request.ArrivalRadius&&o.Speed<.1){
                watch=false;
                if(Mode==RoverMode.Returning){if(ReturnIndex==0){Mode=RoverMode.Home;events.Add("ROVER_HOME");return Decision("HOME",0,o,distance,events);}ReturnIndex--;}
                else{
                    if(!AddTrace(o.Position,o.Radius)){events.Add("ROVER_TRACE_HOLD");return Decision("TRACEFULL_HOLD",0,o,distance,events);}
                    Index++;events.Add("WAYPOINT_REACHED");
                    if(Index==Request.Nodes.Length){Mode=RoverMode.Complete;events.Add("ARRIVED");return Decision("COMPLETE",0,o,distance,events);}
                }
                distance=o.Position.Distance(Target,o.Radius);
                // Brake for one tick so the next terrain observation uses the new heading.
                return Decision("WAYPOINT_REACHED",0,o,distance,events);
            }
            if(Mode==RoverMode.Outbound&&o.Position.Distance(trace.Last(),o.Radius)>=20&&!AddTrace(o.Position,o.Radius)){events.Add("ROVER_TRACE_HOLD");return Decision("TRACEFULL_HOLD",0,o,distance,events);}
            // A finite crawl crosses the arrival boundary despite wheel friction;
            // an asymptotic stop can strand a rover centimetres outside it.
            double speed=distance<=Request.ArrivalRadius?0:Math.Min(Request.Speed,Math.Max(.8,Math.Sqrt((distance-Request.ArrivalRadius)*.6)));
            speed*=Math.Max(.25,1-.75*Math.Pow(o.Slope/Request.MaximumSlope,2));
            if(distance>Request.ArrivalRadius+.01&&speed>.1){
                if(!watch){watch=true;watchStart=o.UT;bestDistance=distance;}
                else if(distance<bestDistance-.5){watchStart=o.UT;bestDistance=distance;}
                else if(o.UT-watchStart>=Request.StuckSeconds){Hold=RoverHold.Stuck;events.Add("ROVER_STUCK");return Decision("STUCK_HOLD",0,o,distance,events);}
            }else watch=false;
            return Decision(Mode==RoverMode.Returning?"RETURNING":"DRIVING",speed,o,distance,events);
        }
        bool AddTrace(RoverPoint point,double radius){if(point.Distance(trace.Last(),radius)<1)return true;if(trace.Count>=TraceLimit){Hold=RoverHold.TraceFull;return false;}trace.Add(point);return true;}
        RoverDecision Decision(string status,double speed,RoverObservation o,double distance,List<string> events){return new RoverDecision(status,speed,distance,o.Slope,ReturnDistance(o.Position,o.Radius),Target,events);}
        static readonly CultureInfo C=CultureInfo.InvariantCulture;
        static string F(double v){return v.ToString("R",C);}
        static double N(string v){double n=Double.Parse(v,C);if(!FleetEventLedger.Finite(n)||n<0)throw new FormatException("Invalid route checkpoint number");return n;}
        public string Save(){
            var q=Request;var h=new[]{"PFROUTE1",q.Id.ToString(),Convert.ToBase64String(Encoding.UTF8.GetBytes(q.Body)),q.Home.Encode(),F(q.Speed),F(q.ArrivalRadius),F(q.MaximumSlope),F(q.ReserveFraction),F(q.StuckSeconds),q.AutoReturn.ToString(),q.Nodes.Length.ToString(C),Mode.ToString(),Index.ToString(C),ReturnIndex.ToString(C),Hold.ToString(),F(DistanceDriven),F(EnergyUsed),observed.ToString(),previous.Encode(),F(lastUT),F(lastPower),watch.ToString(),F(bestDistance),F(watchStart),trace.Count.ToString(C)};
            string raw=String.Join("|",h)+"\n"+String.Join("\n",q.Nodes.Concat(trace).Select(p=>p.Encode()).ToArray());return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }
        public static RoverRoute Load(string encoded){
            if(String.IsNullOrEmpty(encoded)||encoded.Length>196608)throw new FormatException("Missing or oversized rover checkpoint");
            var rows=Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('\n');var n=rows[0].Split('|');
            if(n.Length!=25||n[0]!="PFROUTE1")throw new FormatException("Unsupported rover checkpoint");
            int nodes=Int32.Parse(n[10],C),crumbs=Int32.Parse(n[24],C);if(nodes<1||nodes>64||crumbs<1||crumbs>TraceLimit||rows.Length!=1+nodes+crumbs)throw new FormatException("Rover checkpoint count mismatch");
            var q=new RoverRouteRequest(Guid.Parse(n[1]),Encoding.UTF8.GetString(Convert.FromBase64String(n[2])),RoverPoint.Decode(n[3]),rows.Skip(1).Take(nodes).Select(RoverPoint.Decode),N(n[4]),N(n[5]),N(n[6]),N(n[7]),N(n[8]),Boolean.Parse(n[9]));
            var r=new RoverRoute(q){Mode=(RoverMode)Enum.Parse(typeof(RoverMode),n[11]),Index=Int32.Parse(n[12],C),ReturnIndex=Int32.Parse(n[13],C),Hold=(RoverHold)Enum.Parse(typeof(RoverHold),n[14]),DistanceDriven=N(n[15]),EnergyUsed=N(n[16]),observed=Boolean.Parse(n[17]),previous=RoverPoint.Decode(n[18]),lastUT=N(n[19]),lastPower=N(n[20]),watch=Boolean.Parse(n[21]),bestDistance=N(n[22]),watchStart=N(n[23])};
            if(!Enum.IsDefined(typeof(RoverMode),r.Mode)||!Enum.IsDefined(typeof(RoverHold),r.Hold)||r.Index<0||r.Index>nodes||r.Mode==RoverMode.Outbound&&r.Index>=nodes||r.Mode==RoverMode.Complete&&r.Index!=nodes||r.ReturnIndex<0||r.ReturnIndex>=crumbs||r.watchStart>r.lastUT)throw new FormatException("Invalid route checkpoint state");
            r.trace.Clear();r.trace.AddRange(rows.Skip(1+nodes).Select(RoverPoint.Decode));if(r.trace[0].Latitude!=q.Home.Latitude||r.trace[0].Longitude!=q.Home.Longitude)throw new FormatException("Route trace does not start at home");return r;
        }
    }
}
