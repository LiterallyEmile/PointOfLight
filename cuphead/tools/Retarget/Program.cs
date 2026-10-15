// Points every UnityEngine.*Module reference in a compiled plugin at plain "UnityEngine".
//
// CI can't compile against Cuphead's own DLLs, so it uses Unity reference packages,
// which put each type in its module (UnityEngine.CoreModule, UnityEngine.InputLegacyModule...).
// Which module holds a type differs between Unity versions, but every Unity version ships a
// UnityEngine.dll that either contains or forwards all of them. Referencing that instead
// makes the plugin load whatever Unity version Cuphead is built on.
//
// With --verify <dir>, every Unity type and member the plugin uses is then resolved through
// <dir>/UnityEngine.dll (following its type forwards), and the build fails if any is missing.
//
// usage: Retarget <in.dll> <out.dll> [--verify <unity reference dir>]
using System;
using System.Linq;
using Mono.Cecil;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 2 && !(args.Length == 4 && args[2] == "--verify"))
        {
            Console.Error.WriteLine("usage: Retarget <in.dll> <out.dll> [--verify <unity reference dir>]");
            return 2;
        }

        var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { InMemory = true });
        var module = asm.MainModule;
        Func<AssemblyNameReference, bool> isUnityModule =
            r => r.Name.StartsWith("UnityEngine.", StringComparison.Ordinal) && r.Name.EndsWith("Module", StringComparison.Ordinal);

        var unity = module.AssemblyReferences.FirstOrDefault(r => r.Name == "UnityEngine");
        if (unity == null)
        {
            unity = new AssemblyNameReference("UnityEngine", new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(unity);
        }

        int moved = 0;
        foreach (var t in module.GetTypeReferences())
        {
            if (t.DeclaringType != null) continue;          // nested types follow their outer type
            var scope = t.Scope as AssemblyNameReference;
            if (scope != null && isUnityModule(scope)) { t.Scope = unity; moved++; }
        }

        var stale = module.AssemblyReferences.Where(isUnityModule).ToList();
        foreach (var r in stale) module.AssemblyReferences.Remove(r);

        asm.Write(args[1]);
        Console.WriteLine($"Retargeted {moved} type references from {stale.Count} Unity modules to UnityEngine.");
        foreach (var r in module.AssemblyReferences) Console.WriteLine("  references " + r.FullName);
        return args.Length == 4 ? Verify(args[1], args[3]) : 0;
    }

    static int Verify(string dll, string unityDir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(unityDir);
        var module = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver });
        Func<TypeReference, bool> fromUnity = t =>
        {
            while (t.DeclaringType != null) t = t.DeclaringType;
            var s = t.Scope as AssemblyNameReference;
            return s != null && s.Name == "UnityEngine";
        };

        int checkedCount = 0, missing = 0;
        foreach (var t in module.GetTypeReferences())
        {
            if (!fromUnity(t)) continue;
            checkedCount++;
            TypeDefinition def = null;
            try { def = t.Resolve(); } catch (AssemblyResolutionException) { }
            if (def == null) { missing++; Console.Error.WriteLine("MISSING type   " + t.FullName); }
        }
        foreach (var m in module.GetMemberReferences())
        {
            var owner = m.DeclaringType.GetElementType();
            if (!fromUnity(owner)) continue;
            checkedCount++;
            IMemberDefinition def = null;
            try { def = m.Resolve(); } catch (AssemblyResolutionException) { }
            if (def == null) { missing++; Console.Error.WriteLine("MISSING member " + m.FullName); }
        }
        Console.WriteLine($"Verified {checkedCount} Unity types/members against {unityDir}: {missing} missing.");
        return missing == 0 ? 0 : 1;
    }
}
