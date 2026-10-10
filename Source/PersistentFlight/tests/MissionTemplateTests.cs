using System;using System.IO;using System.Linq;using System.Globalization;using PersistentFlight.Regions;
class MissionTemplateTests {
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
 static void Reject(Action a,string s){bool rejected=false;try{a();}catch{rejected=true;}Check(rejected,s);}
 static MissionTemplate T(){var t=new MissionTemplate{Id=Guid.NewGuid(),Name="Transfer, α",Body="Kerbin",SourceSMA=700000,SourceEcc=.01,SourceInclination=2};t.Steps.Add(new TemplateStep{Delay=0,Radial=-1,Normal=2,Prograde=10});t.Steps.Add(new TemplateStep{Delay=60,Prograde=5});return t;}
 static int Main(){string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PF-Template-Tests-"+Guid.NewGuid().ToString("N"));try{
  var t=T();var text=MissionTemplateStore.Encode(t);var r=MissionTemplateStore.Decode(text);Check(r.Name==t.Name&&r.Steps[0].Radial==-1&&r.Steps[1].Delay==60,"Roundtrip or vector sign changed");
  CultureInfo.CurrentCulture=new CultureInfo("fr-FR");Check(MissionTemplateStore.Encode(r)==text,"Locale changed template");var times=t.Times(10000,120);Check(times[0]==10120&&times[1]==10180,"Times not rebased");
  Reject(()=>t.Times(10000,59),"Too-short delay accepted");Reject(()=>t.Times(Double.MaxValue,60),"Unrepresentable times accepted");Reject(()=>t.Times(0,Double.NaN),"NaN delay accepted");
  foreach(double n in new[]{Double.NaN,Double.PositiveInfinity,-1,31536001}){var x=T();x.Steps[1].Delay=n;Reject(()=>x.Validate(),"Invalid time accepted");}
  var order=T();order.Steps[1].Delay=0;Reject(()=>order.Validate(),"Duplicate time accepted");var first=T();first.Steps[0].Delay=1;Reject(()=>first.Validate(),"Nonzero relative start accepted");
  foreach(double n in new[]{0,.1,10001,Double.NaN}){var x=T();x.Steps[0].Radial=x.Steps[0].Normal=0;x.Steps[0].Prograde=n;Reject(()=>x.Validate(),"Invalid DV accepted");}
  Reject(()=>MissionTemplateStore.Decode(text.Replace("PFMT1","PFMT2")),"Future version accepted");Reject(()=>MissionTemplateStore.Decode(text.Replace("700000","700001")),"Corruption accepted");Reject(()=>MissionTemplateStore.Decode(text+"extra"),"Trailing payload accepted");Reject(()=>MissionTemplateStore.Decode(new string('x',MissionTemplateStore.MaxBytes+1)),"Oversized payload accepted");
  MissionTemplateStore.Save(folder,t);var original=File.ReadAllBytes(Path.Combine(folder,t.Id.ToString("N")+".pft"));Reject(()=>MissionTemplateStore.Save(folder,t),"Duplicate identity overwritten");Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,t.Id.ToString("N")+".pft"))),"Failed save changed original");
  File.WriteAllText(Path.Combine(folder,"broken.pft"),"corrupt evidence");string[] errors;var rows=MissionTemplateStore.List(folder,out errors);Check(rows.Length==1&&errors.Length==1&&File.ReadAllText(Path.Combine(folder,"broken.pft"))=="corrupt evidence","Corruption hid good files or deleted evidence");
  Reject(()=>MissionTemplateStore.Delete(folder,t.Id,"stale"),"Stale delete accepted");Check(File.Exists(Path.Combine(folder,t.Id.ToString("N")+".pft")),"Stale delete removed file");MissionTemplateStore.Delete(folder,t.Id,r.Checksum);Check(!File.Exists(Path.Combine(folder,t.Id.ToString("N")+".pft")),"Verified delete failed");
  var mismatch=T();MissionTemplateStore.Save(folder,mismatch);File.Move(Path.Combine(folder,mismatch.Id.ToString("N")+".pft"),Path.Combine(folder,Guid.NewGuid().ToString("N")+".pft"));rows=MissionTemplateStore.List(folder,out errors);Check(rows.Length==0&&errors.Length==2,"Filename/content identity mismatch accepted");
  for(int i=0;i<98;i++)File.WriteAllText(Path.Combine(folder,Guid.NewGuid().ToString("N")+".pft"),"preserved");Reject(()=>MissionTemplateStore.Save(folder,T()),"Library capacity ignored");Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Temp files left after rejected save");
  Console.WriteLine("PASS "+checks+" mission template checks");return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{Console.WriteLine("Evidence: "+folder);}}
}

