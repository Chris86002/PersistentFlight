using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
namespace PersistentFlight.Regions {
 public enum TemplateGoal { Sequence=0, CircularizeApoapsis=1, CircularizePeriapsis=2, OppositeFromApoapsis=3, OppositeFromPeriapsis=4, CircularAltitude=5 }
 public sealed class TemplateStep {
  public double Delay,Radial,Normal,Prograde;
  public double DV {get{return Math.Sqrt(Radial*Radial+Normal*Normal+Prograde*Prograde);}}
 }
 public sealed class MissionTemplate {
  public Guid Id;public string Name="",Body="",Checksum="";
  public double SourceSMA,SourceEcc,SourceInclination;
  public TemplateGoal Goal;public double TargetAltitude;
  public readonly List<TemplateStep> Steps=new List<TemplateStep>();
  static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
  public void Validate(){
   if(Id==Guid.Empty||String.IsNullOrWhiteSpace(Name)||Name.Length>80||String.IsNullOrWhiteSpace(Body)||Body.Length>80||new[]{Name,Body}.Any(s=>s.Any(Char.IsControl)))throw new FormatException("Invalid template identity or name");
   if(!Finite(SourceSMA)||Math.Abs(SourceSMA)<1||!Finite(SourceEcc)||SourceEcc<0||!Finite(SourceInclination)||SourceInclination<0||SourceInclination>180)throw new FormatException("Invalid source orbit");
   if(!Enum.IsDefined(typeof(TemplateGoal),Goal)||!Finite(TargetAltitude))throw new FormatException("Invalid template goal");
   if(Goal!=TemplateGoal.Sequence){if(Steps.Count!=0||TargetAltitude<0||TargetAltitude>1e9||((Goal==TemplateGoal.CircularizeApoapsis||Goal==TemplateGoal.CircularizePeriapsis)&&TargetAltitude!=0)||((Goal==TemplateGoal.OppositeFromApoapsis||Goal==TemplateGoal.OppositeFromPeriapsis||Goal==TemplateGoal.CircularAltitude)&&TargetAltitude<=0))throw new FormatException("Invalid goal altitude or stored maneuvers");return;}
   if(TargetAltitude!=0)throw new FormatException("Sequence cannot contain a goal altitude");
   if(Steps.Count==0||Steps.Count>32)throw new FormatException("Use 1 to 32 maneuvers");
   double previous=-1;foreach(var s in Steps){if(s==null||!Finite(s.Delay)||!Finite(s.Radial)||!Finite(s.Normal)||!Finite(s.Prograde)||s.Delay<0||s.Delay>31536000||s.Delay<=previous||s.DV<.5||s.DV>10000)throw new FormatException("Invalid maneuver timing or delta-v");previous=s.Delay;}
   if(Steps[0].Delay!=0)throw new FormatException("First template maneuver must use zero relative delay");
  }
  public double[] Times(double now,double firstDelay){Validate();if(Goal!=TemplateGoal.Sequence)throw new InvalidOperationException("Goal timing must be calculated from the destination orbit");if(!Finite(now)||now<0||!Finite(firstDelay)||firstDelay<60||firstDelay>86400)throw new ArgumentException("First burn delay must be 60 to 86,400 seconds");var times=Steps.Select(s=>now+firstDelay+s.Delay).ToArray();double previous=now;foreach(var ut in times){if(!Finite(ut)||ut<=previous)throw new ArgumentException("Template times cannot be represented safely");previous=ut;}return times;}
 }
 public static class MissionTemplateStore {
  public const int Limit=100,MaxBytes=131072;
  static readonly CultureInfo C=CultureInfo.InvariantCulture;
  static string N(double x){return x.ToString("R",C);}
  static string Text(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));}
  static string Hash(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","");}
  public static string Encode(MissionTemplate t){t.Validate();string header=t.Id.ToString("N")+"\n"+Text(t.Name)+"\n"+Text(t.Body)+"\n"+N(t.SourceSMA)+"|"+N(t.SourceEcc)+"|"+N(t.SourceInclination)+"\n";string body=t.Goal==TemplateGoal.Sequence?"PFMT1\n"+header+t.Steps.Count.ToString(C)+"\n"+String.Join("\n",t.Steps.Select(s=>N(s.Delay)+"|"+N(s.Radial)+"|"+N(s.Normal)+"|"+N(s.Prograde)).ToArray())+"\n":"PFMT2\n"+header+((int)t.Goal).ToString(C)+"|"+N(t.TargetAltitude)+"\n";return body+Hash(body)+"\n";}
  public static MissionTemplate Decode(string text){
   if(text==null||Encoding.UTF8.GetByteCount(text)>MaxBytes)throw new FormatException("Template exceeds its size limit");
   var rows=text.Split('\n');int count;
   count=0;bool goal=rows.Length==8&&rows[0]=="PFMT2";
   if((!goal&&(rows.Length<9||rows[0]!="PFMT1"||!Int32.TryParse(rows[5],NumberStyles.None,C,out count)||count<1||count>32||rows.Length!=count+8))||rows[rows.Length-1]!="")throw new FormatException("Unsupported or damaged template structure");
   string body=String.Join("\n",rows.Take(rows.Length-2).ToArray())+"\n";if(rows[rows.Length-2]!=Hash(body))throw new FormatException("Template checksum mismatch");
   var t=new MissionTemplate{Id=Guid.ParseExact(rows[1],"N"),Name=Encoding.UTF8.GetString(Convert.FromBase64String(rows[2])),Body=Encoding.UTF8.GetString(Convert.FromBase64String(rows[3])),Checksum=rows[rows.Length-2]};
   var orbit=rows[4].Split('|').Select(x=>Double.Parse(x,C)).ToArray();if(orbit.Length!=3)throw new FormatException("Invalid source orbit fields");t.SourceSMA=orbit[0];t.SourceEcc=orbit[1];t.SourceInclination=orbit[2];
   if(goal){var fields=rows[5].Split('|');int kind;if(fields.Length!=2||!Int32.TryParse(fields[0],NumberStyles.None,C,out kind)||kind==0)throw new FormatException("Invalid goal fields");t.Goal=(TemplateGoal)kind;t.TargetAltitude=Double.Parse(fields[1],C);t.Validate();return t;}
   for(int i=0;i<count;i++){var n=rows[6+i].Split('|').Select(x=>Double.Parse(x,C)).ToArray();if(n.Length!=4)throw new FormatException("Invalid maneuver fields");t.Steps.Add(new TemplateStep{Delay=n[0],Radial=n[1],Normal=n[2],Prograde=n[3]});}t.Validate();return t;
  }
  static string PathFor(string folder,Guid id){if(id==Guid.Empty)throw new ArgumentException("Select a template");return Path.Combine(Path.GetFullPath(folder),id.ToString("N")+".pft");}
  public static MissionTemplate Load(string folder,Guid id){string p=PathFor(folder,id);var info=new FileInfo(p);if(info.Length>MaxBytes)throw new FormatException("Template exceeds its size limit");var t=Decode(File.ReadAllText(p,Encoding.UTF8));if(t.Id!=id)throw new FormatException("Template filename identity mismatch");return t;}
  public static MissionTemplate[] List(string folder,out string[] errors){
   folder=Path.GetFullPath(folder);var bad=new List<string>();var result=new List<MissionTemplate>();if(Directory.Exists(folder)){var files=Directory.GetFiles(folder,"*.pft");if(files.Length>Limit)throw new InvalidOperationException("Library exceeds 100 files; preserve and organize it before adding templates");foreach(var p in files.OrderBy(x=>x)){try{Guid id;if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(p),"N",out id))throw new FormatException("Invalid template filename");result.Add(Load(folder,id));}catch(Exception e){bad.Add(Path.GetFileName(p)+": "+e.Message);}}}errors=bad.ToArray();return result.OrderBy(t=>t.Name,StringComparer.OrdinalIgnoreCase).ToArray();
  }
  public static void Save(string folder,MissionTemplate t){folder=Path.GetFullPath(folder);string text=Encode(t);Directory.CreateDirectory(folder);if(Directory.GetFiles(folder,"*.pft").Length>=Limit)throw new InvalidOperationException("Template library is full (100 files)");string p=PathFor(folder,t.Id),temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(text);File.Move(temp,p);}finally{if(File.Exists(temp))File.Delete(temp);}}
  public static void Delete(string folder,Guid id,string checksum){var t=Load(folder,id);if(t.Checksum!=checksum)throw new InvalidOperationException("Template changed since it was selected; reload the library");File.Delete(PathFor(folder,id));}
 }
}
