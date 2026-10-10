using System;
using System.IO;
using System.Linq;
using PersistentFlight;
public static class RecoveryTests {
 static int count;static void Check(bool b,string s){count++;if(!b)throw new Exception(s);}
 static void Refuses(Action a,string s){bool denied=false;try{a();}catch{denied=true;}Check(denied,s);}
 public static void Main(string[] args){
  string root=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var store=new RecoveryStore(root);Guid focus=Guid.NewGuid();
  File.WriteAllText(Path.Combine(root,"persistent.sfs"),"player save");
  for(int i=0;i<20;i++){var p=store.Create(n=>{File.WriteAllText(Path.Combine(root,n+".sfs"),"game "+i);File.WriteAllText(Path.Combine(root,n+".loadmeta"),"preview");},100+i,focus,"burn\n"+i);Check(store.List().Length==Math.Min(i+1,8),"rotation");Check(store.Verify(p.Name).Focus==focus,"metadata identity");Check(!p.Reason.Contains("\n"),"label cleanup");}
  Check(Directory.GetFiles(root,"PF-Recovery-*.loadmeta").Length==8,"preview caches rotate with saves");var points=store.List();Check(points.Length==8,"bounded saves");Check(File.ReadAllText(Path.Combine(root,"persistent.sfs"))=="player save","player save preserved");
  Refuses(()=>store.Verify("../persistent"),"path escape refused");
  Refuses(()=>store.Create(n=>{throw new IOException("injected");},100,focus,"failed"),"write failure reported");Check(store.List().Length==8&&points.All(p=>File.Exists(Path.Combine(root,p.Name+".sfs"))),"failed write retains history");
  Refuses(()=>store.Create(n=>{},100,focus,"missing"),"missing save refused");Refuses(()=>store.Create(n=>File.WriteAllText(Path.Combine(root,n+".sfs"),""),100,focus,"empty"),"empty save refused");
  Refuses(()=>store.Create(n=>{},Double.NaN,focus,"invalid"),"invalid UT refused");
  var damaged=points.Last();File.AppendAllText(Path.Combine(root,damaged.Name+".sfs"),"tamper");Refuses(()=>store.Verify(damaged.Name),"damaged save refused");
  var latest=store.Create(n=>File.WriteAllText(Path.Combine(root,n+".sfs"),"latest"),500,focus,"new");Check(File.Exists(Path.Combine(root,damaged.Name+".sfs")),"damaged evidence preserved");Check(store.Verify(latest.Name).UT==500,"new save readable after damage");
  string bogus="PF-Recovery-"+Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(root,bogus+".pfr"),"corrupt metadata");Check(!store.List().Any(p=>p.Name==bogus),"corrupt metadata excluded");
  var meta=File.ReadAllLines(Path.Combine(root,latest.Name+".pfr"));meta[1]="../../persistent";File.WriteAllLines(Path.Combine(root,latest.Name+".pfr"),meta);Refuses(()=>store.Verify(latest.Name),"forged identity refused");Check(File.ReadAllText(Path.Combine(root,"persistent.sfs"))=="player save","no unrelated deletion");
  Console.WriteLine("PASS "+count+" recovery storage checks");
 }
}