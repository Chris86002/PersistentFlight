using System;
using System.IO;
using System.Reflection;
using kOS.Safe.Compilation;
using kOS.Safe.Compilation.KS;
using kOS.Safe.Encapsulation;
class ScriptChecks {
    static string library;
    static void Main(string[] args){try{library=args[0];AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>e.Name.StartsWith("kOS.Safe,")?Assembly.LoadFrom(library):null;Run(args);}catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.Exit(1);}}
    static void Run(string[] args){
        foreach(string path in new[]{args[1],args[2]}){
            var tree=new Parser(new Scanner()).Parse(File.ReadAllText(path));if(tree.Errors.Count>0)throw new Exception("kOS parse errors: "+path+" / "+tree.Errors[0]);
            var compiler=typeof(Parser).Assembly.GetType("kOS.Safe.Compilation.KS.Compiler");compiler.GetMethod("Compile").Invoke(Activator.CreateInstance(compiler),new object[]{0,tree,new Context(),new CompilerOptions()});Console.WriteLine("PASS kOS compiler "+Path.GetFileName(path));
        }
        if(!new StringValue("PFE1|x").HasSuffix("STARTSWITH"))throw new Exception("Checkpoint prefix suffix unavailable");
        Console.WriteLine("PASS kOS checkpoint prefix suffix available");
    }
}
