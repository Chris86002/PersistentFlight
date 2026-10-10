using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
namespace PersistentFlight {
    public sealed class RecoveryPoint {
        public string Name,Reason,Hash;
        public long Created;
        public double UT;
        public Guid Focus;
    }
    // Only files bearing our strict name and matching metadata are managed.
    // Publish a new save before retiring older ones; a failed write keeps history.
    public sealed class RecoveryStore {
        public const int Limit=8;
        readonly string directory;
        static readonly CultureInfo C=CultureInfo.InvariantCulture;
        public RecoveryStore(string folder){directory=Path.GetFullPath(folder);}
        static bool Owned(string name){Guid id;return name!=null&&name.Length==44&&name.StartsWith("PF-Recovery-",StringComparison.Ordinal)&&Guid.TryParseExact(name.Substring(12),"N",out id);}
        string PathFor(string name,string suffix){if(!Owned(name))throw new InvalidDataException("Invalid recovery identity");return Path.Combine(directory,name+suffix);}
        static string Hash(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
        RecoveryPoint Read(string path){
            if(new FileInfo(path).Length>4096)throw new InvalidDataException("Recovery metadata too large");
            var a=File.ReadAllLines(path);Guid focus;long ticks;double ut;
            if(a.Length!=8||a[0]!="PersistentFlight recovery 1"||!Owned(a[1])||Path.GetFileName(path)!=a[1]+".pfr"||!Int64.TryParse(a[2],out ticks)||ticks<DateTime.MinValue.Ticks||ticks>DateTime.MaxValue.Ticks||!Double.TryParse(a[3],NumberStyles.Float,C,out ut)||!BurnMath.Finite(ut)||ut<0||!Guid.TryParseExact(a[4],"D",out focus)||a[7]!="Committed"||a[6].Length!=64||a[6].Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Invalid recovery metadata");
            string reason=Encoding.UTF8.GetString(Convert.FromBase64String(a[5]));if(reason.Length>160)throw new InvalidDataException("Recovery label too long");
            return new RecoveryPoint{Name=a[1],Created=ticks,UT=ut,Focus=focus,Reason=reason,Hash=a[6]};
        }
        public RecoveryPoint[] List(){
            if(!Directory.Exists(directory))return new RecoveryPoint[0];
            return Directory.GetFiles(directory,"PF-Recovery-*.pfr").Select(p=>{try{return Read(p);}catch{return null;}}).Where(p=>p!=null&&File.Exists(PathFor(p.Name,".sfs"))).OrderByDescending(p=>p.Created).ThenBy(p=>p.Name,StringComparer.Ordinal).ToArray();
        }
        public RecoveryPoint Verify(string name){
            var p=Read(PathFor(name,".pfr"));if(!String.Equals(Hash(PathFor(name,".sfs")),p.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Recovery save changed or is damaged; choose another point");return p;
        }
        public RecoveryPoint Create(Action<string> save,double ut,Guid focus,string reason){
            if(save==null||!BurnMath.Finite(ut)||ut<0)throw new ArgumentException("Invalid recovery request");
            Directory.CreateDirectory(directory);
            string name="PF-Recovery-"+Guid.NewGuid().ToString("N"),meta=PathFor(name,".pfr"),temp=meta+".pending";
            var point=new RecoveryPoint{Name=name,Created=DateTime.UtcNow.Ticks,UT=ut,Focus=focus,Reason=(reason??"Recovery").Replace('\r',' ').Replace('\n',' ')};
            if(point.Reason.Length>160)point.Reason=point.Reason.Substring(0,160);
            try{
                save(name);var file=PathFor(name,".sfs");if(!File.Exists(file)||new FileInfo(file).Length==0)throw new IOException("Recovery save was not written");point.Hash=Hash(file);
                File.WriteAllLines(temp,new[]{"PersistentFlight recovery 1",name,point.Created.ToString(C),ut.ToString("R",C),focus.ToString("D"),Convert.ToBase64String(Encoding.UTF8.GetBytes(point.Reason)),point.Hash,"Committed"});
                File.Move(temp,meta);
            }catch{if(File.Exists(temp))File.Delete(temp);throw;}
            // A damaged old point is preserved for manual inspection, never used
            // as permission to delete a file whose contents no longer match.
            foreach(var old in List().Skip(Limit)){try{Verify(old.Name);var preview=PathFor(old.Name,".loadmeta");if(File.Exists(preview))File.Delete(preview);File.Delete(PathFor(old.Name,".sfs"));File.Delete(PathFor(old.Name,".pfr"));}catch{}}
            return point;
        }
    }
}
