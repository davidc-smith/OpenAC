using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type&gt; &lt;references file&gt;</c>:
/// every user32/kernel32 P/Invoke in the assembly that the stand-in type (in
/// the same assembly) has a public static method for, same name and
/// signature, becomes a managed call to that method. The assembly is
/// rewritten in place, its symbols with it; the references file lists the
/// assembly's references, one path a line, for resolving its types. A rerun
/// finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type> <references file>");
            return 2;
        }

        try
        {
            int count = Redirect(args[0], args[1], File.ReadAllLines(args[2]));
            Console.WriteLine($"RynthInteropRedirect: {Path.GetFileName(args[0])}: {count} P/Invoke(s) redirected to {args[1]}");
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            Console.Error.WriteLine($"RynthInteropRedirect: error: {error.Message}");
            return 1;
        }
    }

    private static int Redirect(string assemblyPath, string standInTypeName, IEnumerable<string> references)
    {
        bool hasSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in references.Where(r => r.Length > 0).Select(r => Path.GetDirectoryName(r) ?? ".").Distinct())
            resolver.AddSearchDirectory(directory);
        using ModuleDefinition module = ModuleDefinition.ReadModule(
            assemblyPath,
            new ReaderParameters { ReadWrite = true, ReadSymbols = hasSymbols, AssemblyResolver = resolver });
        TypeDefinition standIn = module.GetType(standInTypeName)
            ?? throw new InvalidOperationException($"{standInTypeName} is not in {assemblyPath}");

        int count = 0;
        foreach (TypeDefinition type in module.GetTypes())
        {
            if (type == standIn)
                continue;
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.IsPInvokeImpl || method.PInvokeInfo is not { } pinvoke
                    || !Win32Modules.Contains(pinvoke.Module.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                string entryPoint = string.IsNullOrEmpty(pinvoke.EntryPoint) ? method.Name : pinvoke.EntryPoint;
                MethodDefinition? target = standIn.Methods.FirstOrDefault(
                    candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == entryPoint
                        && SameSignature(candidate, method));
                if (target is null)
                    continue;
                CallInstead(method, target);
                count++;
            }
        }

        if (count > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        return count;
    }

    private static bool SameSignature(MethodDefinition a, MethodDefinition b) =>
        a.ReturnType.FullName == b.ReturnType.FullName
        && a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(b.Parameters.Select(p => p.ParameterType.FullName));

    /// <summary>The extern becomes a managed method that passes its arguments to <paramref name="target"/>.</summary>
    private static void CallInstead(MethodDefinition method, MethodDefinition target)
    {
        method.PInvokeInfo = null;
        method.IsPInvokeImpl = false;
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method);
        ILProcessor il = method.Body.GetILProcessor();
        foreach (ParameterDefinition parameter in method.Parameters)
            il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }
}
