using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type|-&gt; &lt;references file&gt; [--inert &lt;module,...&gt;] [--plain-threadstatic-swaps] [--redirect-type &lt;from&gt;=&lt;to&gt;]...</c>:
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
/// path a line, for resolving its types. With <c>--redirect-type</c>, every
/// use of type <c>from</c> (a referenced type, by full name) becomes <c>to</c>
/// (a type in the assembly): its members by name, signature and static-ness,
/// and every field, local, parameter, return and type operand of that type. A
/// member <c>to</c> lacks fails the run. A rerun finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args)
    {
        const string Usage = "usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type|-> <references file> [--inert <module,...>] [--plain-threadstatic-swaps] [--redirect-type <from>=<to>]...";
        string[] inert = [];
        bool inertGiven = false, plainSwaps = false;
        var typeMaps = new List<(string From, string To)>();
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--inert" && !inertGiven && i + 1 < args.Length)
            {
                inertGiven = true;
                inert = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (args[i] == "--plain-threadstatic-swaps" && !plainSwaps)
            {
                plainSwaps = true;
            }
            else if (args[i] == "--redirect-type" && i + 1 < args.Length
                     && args[i + 1].Split('=') is [{ Length: > 0 } from, { Length: > 0 } to])
            {
                typeMaps.Add((from, to));
                i++;
            }
            else
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        string? standIn = args[1] == "-" ? null : args[1];
        try
        {
            (int redirected, int inerted, int swaps, int types) = Redirect(args[0], standIn, File.ReadAllLines(args[2]), inert, plainSwaps, typeMaps);
            Console.WriteLine(
                $"RynthInteropRedirect: {Path.GetFileName(args[0])}: {redirected} P/Invoke(s) redirected to {standIn ?? "nothing"}, {inerted} made inert{(plainSwaps ? $", {swaps} thread-static swap(s) made plain" : "")}{(typeMaps.Count > 0 ? $", {types} type(s) redirected" : "")}");
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            Console.Error.WriteLine($"RynthInteropRedirect: error: {error.Message}");
            return 1;
        }
    }

    private static (int Redirected, int Inerted, int Swaps, int Types) Redirect(
        string assemblyPath, string? standInTypeName, IEnumerable<string> references, string[] inertModules, bool plainSwaps,
        IReadOnlyList<(string From, string To)> typeMaps)
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
        int types = RedirectTypes(module, typeMaps);
        if (redirected + inerted + swaps + types > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        return (redirected, inerted, swaps, types);
    }

    /// <summary>
    /// Rebinds every use of each <c>from</c> type to its <c>to</c> type in the module.
    /// Returns how many <c>from</c> types were found in use.
    /// </summary>
    private static int RedirectTypes(ModuleDefinition module, IReadOnlyList<(string From, string To)> maps)
    {
        if (maps.Count == 0)
            return 0;
        var targets = maps.ToDictionary(
            m => m.From,
            m => module.GetType(m.To) ?? throw new InvalidOperationException($"{m.To} is not in {module.Name}"));
        var used = new HashSet<string>();
        var standIns = targets.Values.ToHashSet();

        TypeReference Map(TypeReference type)
        {
            switch (type)
            {
                case ByReferenceType byRef:
                    return new ByReferenceType(Map(byRef.ElementType));
                case ArrayType array:
                    return new ArrayType(Map(array.ElementType), array.Rank);
                case GenericInstanceType generic:
                    var copy = new GenericInstanceType(generic.ElementType);
                    foreach (TypeReference argument in generic.GenericArguments)
                        copy.GenericArguments.Add(Map(argument));
                    return copy;
            }

            if (!targets.TryGetValue(type.FullName, out TypeDefinition? target))
                return type;
            used.Add(type.FullName);
            return target;
        }

        foreach (TypeDefinition type in module.GetTypes().Where(t => !standIns.Contains(t)).ToList())
        {
            foreach (FieldDefinition field in type.Fields)
                field.FieldType = Map(field.FieldType);
            foreach (PropertyDefinition property in type.Properties)
                property.PropertyType = Map(property.PropertyType);
            foreach (MethodDefinition method in type.Methods)
            {
                method.ReturnType = Map(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters)
                    parameter.ParameterType = Map(parameter.ParameterType);
                if (!method.HasBody)
                    continue;
                foreach (VariableDefinition variable in method.Body.Variables)
                    variable.VariableType = Map(variable.VariableType);
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference callee when targets.TryGetValue(callee.DeclaringType.FullName, out TypeDefinition? to):
                            used.Add(callee.DeclaringType.FullName);
                            instruction.Operand = FindMember(to, callee, Map);
                            break;
                        case FieldReference field when targets.TryGetValue(field.DeclaringType.FullName, out TypeDefinition? to):
                            used.Add(field.DeclaringType.FullName);
                            bool isStatic = instruction.OpCode.Code is Code.Ldsfld or Code.Stsfld or Code.Ldsflda;
                            instruction.Operand = to.Fields.FirstOrDefault(f => f.Name == field.Name && f.IsStatic == isStatic)
                                ?? throw new InvalidOperationException($"{to.FullName} has no {(isStatic ? "static" : "instance")} field {field.Name}");
                            break;
                        case TypeReference operand:
                            instruction.Operand = Map(operand);
                            break;
                    }
                }
            }
        }

        return used.Count;
    }

    private static MethodDefinition FindMember(TypeDefinition to, MethodReference callee, Func<TypeReference, TypeReference> map)
    {
        string[] parameters = callee.Parameters.Select(p => map(p.ParameterType).FullName).ToArray();
        return to.Methods.FirstOrDefault(candidate =>
                   candidate.IsPublic && candidate.Name == callee.Name && candidate.HasThis == callee.HasThis
                   && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
               ?? throw new InvalidOperationException(
                   $"{to.FullName} has no {(callee.HasThis ? "instance" : "static")} {callee.Name}({string.Join(", ", parameters)})");
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
