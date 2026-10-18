using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type|-&gt; &lt;references file&gt; [--inert &lt;module,...&gt;]</c>:
/// every user32/kernel32 P/Invoke in the assembly that the stand-in type (in
/// the same assembly; <c>-</c> for none) has a public static method for, same
/// name and signature, becomes a managed call to that method. With
/// <c>--inert</c>, every other P/Invoke into one of the listed modules
/// becomes a managed method that zeroes its out parameters and returns zero
/// (false, null): a call that failed. The assembly is rewritten in place, its
/// symbols with it; the references file lists the assembly's references, one
/// path a line, for resolving its types. A rerun finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args)
    {
        bool inertGiven = args.Length == 5 && args[3] == "--inert";
        if (args.Length != 3 && !inertGiven)
        {
            Console.Error.WriteLine("usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type|-> <references file> [--inert <module,...>]");
            return 2;
        }

        string? standIn = args[1] == "-" ? null : args[1];
        string[] inert = inertGiven
            ? args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        try
        {
            (int redirected, int inerted) = Redirect(args[0], standIn, File.ReadAllLines(args[2]), inert);
            Console.WriteLine(
                $"RynthInteropRedirect: {Path.GetFileName(args[0])}: {redirected} P/Invoke(s) redirected to {standIn ?? "nothing"}, {inerted} made inert");
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            Console.Error.WriteLine($"RynthInteropRedirect: error: {error.Message}");
            return 1;
        }
    }

    private static (int Redirected, int Inerted) Redirect(
        string assemblyPath, string? standInTypeName, IEnumerable<string> references, string[] inertModules)
    {
        bool hasSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in references.Where(r => r.Length > 0).Select(r => Path.GetDirectoryName(r) ?? ".").Distinct())
            resolver.AddSearchDirectory(directory);
        using ModuleDefinition module = ModuleDefinition.ReadModule(
            assemblyPath,
            new ReaderParameters { ReadWrite = true, ReadSymbols = hasSymbols, AssemblyResolver = resolver });
        TypeDefinition? standIn = standInTypeName is null
            ? null
            : module.GetType(standInTypeName) ?? throw new InvalidOperationException($"{standInTypeName} is not in {assemblyPath}");

        int redirected = 0, inerted = 0;
        foreach (TypeDefinition type in module.GetTypes())
        {
            if (type == standIn)
                continue;
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.IsPInvokeImpl || method.PInvokeInfo is not { } pinvoke)
                    continue;
                string moduleName = pinvoke.Module.Name;
                string entryPoint = string.IsNullOrEmpty(pinvoke.EntryPoint) ? method.Name : pinvoke.EntryPoint;
                MethodDefinition? target = standIn is not null && Win32Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase)
                    ? standIn.Methods.FirstOrDefault(
                        candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == entryPoint
                            && SameSignature(candidate, method))
                    : null;
                if (target is not null)
                {
                    CallInstead(method, target);
                    redirected++;
                }
                else if (inertModules.Contains(moduleName, StringComparer.OrdinalIgnoreCase))
                {
                    MakeInert(method);
                    inerted++;
                }
            }
        }

        if (redirected + inerted > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        return (redirected, inerted);
    }

    private static bool SameSignature(MethodDefinition a, MethodDefinition b) =>
        a.ReturnType.FullName == b.ReturnType.FullName
        && a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(b.Parameters.Select(p => p.ParameterType.FullName));

    /// <summary>The extern becomes a managed method that passes its arguments to <paramref name="target"/>.</summary>
    private static void CallInstead(MethodDefinition method, MethodDefinition target)
    {
        ILProcessor il = ManagedBody(method);
        foreach (ParameterDefinition parameter in method.Parameters)
            il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>The extern becomes a managed method that zeroes its out parameters and returns zero.</summary>
    private static void MakeInert(MethodDefinition method)
    {
        ILProcessor il = ManagedBody(method);
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            if (!parameter.IsOut || parameter.ParameterType is not ByReferenceType byReference)
                continue;
            il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Initobj, byReference.ElementType);
        }

        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            var result = new VariableDefinition(method.ReturnType);
            method.Body.Variables.Add(result);
            method.Body.InitLocals = true;
            il.Emit(OpCodes.Ldloca, result);
            il.Emit(OpCodes.Initobj, method.ReturnType);
            il.Emit(OpCodes.Ldloc, result);
        }

        il.Emit(OpCodes.Ret);
    }

    private static ILProcessor ManagedBody(MethodDefinition method)
    {
        method.PInvokeInfo = null;
        method.IsPInvokeImpl = false;
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method);
        return method.Body.GetILProcessor();
    }
}
