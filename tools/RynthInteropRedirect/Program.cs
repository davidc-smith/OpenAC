using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type|-&gt; &lt;references file&gt; [--inert &lt;module,...&gt;] [--plain-threadstatic-swaps]</c>:
/// every user32/kernel32 P/Invoke in the assembly that the stand-in type (in
/// the same assembly; <c>-</c> for none) has a public static method for, same
/// name and signature, becomes a managed call to that method. With
/// <c>--inert</c>, every other P/Invoke into one of the listed modules
/// becomes a managed method that zeroes its out parameters and returns zero
/// (false, null): a call that failed. The assembly is rewritten in place, its
/// symbols with it. With <c>--plain-threadstatic-swaps</c>, every
/// <c>Interlocked.Exchange(ref IntPtr, IntPtr)</c> on a <c>[ThreadStatic]</c>
/// field (a field only its own thread touches, so the swap needs no atomic;
/// on .NET 10 osx-arm64 the runtime can leave such a field 4-byte aligned,
/// where Interlocked faults) becomes a plain read and write in a generated
/// helper. The references file lists the assembly's references, one
/// path a line, for resolving its types. A rerun finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args)
    {
        const string PlainFlag = "--plain-threadstatic-swaps";
        string[] rest = args.Length > 3 ? args[3..] : [];
        bool plainSwaps = rest.Contains(PlainFlag);
        string[] others = rest.Where(a => a != PlainFlag).ToArray();
        bool inertGiven = others.Length == 2 && others[0] == "--inert";
        if (args.Length < 3 || rest.Count(a => a == PlainFlag) > 1 || (others.Length != 0 && !inertGiven))
        {
            Console.Error.WriteLine("usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type|-> <references file> [--inert <module,...>] [--plain-threadstatic-swaps]");
            return 2;
        }

        string? standIn = args[1] == "-" ? null : args[1];
        string[] inert = inertGiven
            ? others[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        try
        {
            (int redirected, int inerted, int swaps) = Redirect(args[0], standIn, File.ReadAllLines(args[2]), inert, plainSwaps);
            Console.WriteLine(
                $"RynthInteropRedirect: {Path.GetFileName(args[0])}: {redirected} P/Invoke(s) redirected to {standIn ?? "nothing"}, {inerted} made inert{(plainSwaps ? $", {swaps} thread-static swap(s) made plain" : "")}");
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            Console.Error.WriteLine($"RynthInteropRedirect: error: {error.Message}");
            return 1;
        }
    }

    private static (int Redirected, int Inerted, int Swaps) Redirect(
        string assemblyPath, string? standInTypeName, IEnumerable<string> references, string[] inertModules, bool plainSwaps)
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

        int swaps = plainSwaps ? MakeThreadStaticSwapsPlain(module) : 0;
        if (redirected + inerted + swaps > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        return (redirected, inerted, swaps);
    }

    private const string HelperType = "<RynthInteropRedirect>";

    /// <summary>
    /// <c>ldsflda F; (push value); call Interlocked.Exchange(native int&amp;, native int)</c>
    /// with F a [ThreadStatic] field and a one-instruction value push becomes
    /// a call to a plain swap. Anything else is left alone.
    /// </summary>
    private static int MakeThreadStaticSwapsPlain(ModuleDefinition module)
    {
        MethodDefinition? helper = null;
        int swaps = 0;
        foreach (TypeDefinition type in module.GetTypes().ToList())
        {
            foreach (MethodDefinition method in type.Methods.ToList())
            {
                if (!method.HasBody)
                    continue;
                foreach (Instruction call in method.Body.Instructions.ToList())
                {
                    if (call.OpCode != OpCodes.Call || call.Operand is not MethodReference callee
                        || callee.DeclaringType.FullName != "System.Threading.Interlocked" || callee.Name != "Exchange"
                        || callee.Parameters.Count != 2
                        || callee.Parameters[0].ParameterType is not ByReferenceType { ElementType.FullName: "System.IntPtr" }
                        || callee.Parameters[1].ParameterType.FullName != "System.IntPtr")
                        continue;
                    Instruction? value = call.Previous, address = value?.Previous;
                    if (address is null || address.OpCode != OpCodes.Ldsflda || address.Operand is not FieldReference field
                        || !IsSinglePush(value!)
                        || field.Resolve() is not { } resolved
                        || !resolved.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ThreadStaticAttribute"))
                        continue;
                    helper ??= module.GetType(HelperType)?.Methods.FirstOrDefault(m => m.Name == "ExchangeThreadStatic") ?? AddHelper(module);
                    call.Operand = helper;
                    swaps++;
                }
            }
        }

        return swaps;
    }

    private static bool IsSinglePush(Instruction instruction) =>
        instruction.OpCode.Code is Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S
            or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S;

    private static MethodDefinition AddHelper(ModuleDefinition module)
    {
        TypeReference intPtr = module.TypeSystem.IntPtr;
        var holder = new TypeDefinition(
            string.Empty, HelperType,
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);
        var swap = new MethodDefinition(
            "ExchangeThreadStatic", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, intPtr);
        swap.Parameters.Add(new ParameterDefinition("location", ParameterAttributes.None, new ByReferenceType(intPtr)));
        swap.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, intPtr));
        var old = new VariableDefinition(intPtr);
        swap.Body.Variables.Add(old);
        swap.Body.InitLocals = true;
        ILProcessor il = swap.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldind_I);
        il.Emit(OpCodes.Stloc, old);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stind_I);
        il.Emit(OpCodes.Ldloc, old);
        il.Emit(OpCodes.Ret);
        holder.Methods.Add(swap);
        module.Types.Add(holder);
        return swap;
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
