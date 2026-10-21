using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type[,type...]|-&gt; &lt;references file&gt; [--inert &lt;module,...&gt;] [--plain-threadstatic-swaps] [--redirect-type &lt;from&gt;=&lt;to&gt;]... [--redirect-member &lt;type&gt;::&lt;member&gt;=&lt;stand-in type&gt;]...</c>:
/// every user32/kernel32 P/Invoke in the assembly that one of the stand-in types (in
/// the same assembly; several separated by commas; <c>-</c> for none) has a public
/// static method for, same name and signature, becomes a managed call to that method. With
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
/// and every field, local, parameter, return, type operand, base type,
/// interface, generic constraint, catch type, event, override and custom
/// attribute that holds that type (nothing else is rebuilt). A
/// member <c>to</c> lacks (by name, return and parameter types) fails the run, as
/// does a <c>from</c> given twice (a usage error). With <c>--redirect-member</c>, every
/// call to that static member of a referenced type becomes a call to the public static
/// method of the same name, return and parameter types on the stand-in type (in the
/// assembly), whose own methods are left alone so its fallback can call the real member;
/// one without such a method fails the run. A rerun finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>The command line, with its output and error streams given (tests capture them).</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        const string Usage = "usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type[,type...]|-> <references file> [--inert <module,...>] [--plain-threadstatic-swaps] [--redirect-type <from>=<to>]... [--redirect-member <type>::<member>=<stand-in type>]...";
        string[] inert = [];
        bool inertGiven = false, plainSwaps = false;
        var typeMaps = new List<(string From, string To)>();
        var memberMaps = new List<(string FromType, string Member, string To)>();
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
                     && args[i + 1].Split('=') is [{ Length: > 0 } from, { Length: > 0 } to]
                     && !typeMaps.Any(m => m.From == from))
            {
                typeMaps.Add((from, to));
                i++;
            }
            else if (args[i] == "--redirect-member" && i + 1 < args.Length
                     && args[i + 1].Split('=') is [{ Length: > 0 } member, { Length: > 0 } toType]
                     && member.Split("::") is [{ Length: > 0 } fromType, { Length: > 0 } memberName]
                     && !memberMaps.Any(m => m.FromType == fromType && m.Member == memberName))
            {
                memberMaps.Add((fromType, memberName, toType));
                i++;
            }
            else
            {
                error.WriteLine(Usage);
                return 2;
            }
        }

        if (args.Length < 3)
        {
            error.WriteLine(Usage);
            return 2;
        }

        string? standIn = args[1] == "-" ? null : args[1];
        try
        {
            (int redirected, int inerted, int swaps, int types, int members) = Redirect(args[0], standIn, File.ReadAllLines(args[2]), inert, plainSwaps, typeMaps, memberMaps);
            output.WriteLine(
                $"RynthInteropRedirect: {Path.GetFileName(args[0])}: {redirected} P/Invoke(s) redirected to {standIn ?? "nothing"}, {inerted} made inert{(plainSwaps ? $", {swaps} thread-static swap(s) made plain" : "")}{(typeMaps.Count > 0 ? $", {types} type(s) redirected" : "")}{(memberMaps.Count > 0 ? $", {members} member call(s) redirected" : "")}");
            return 0;
        }
        catch (Exception failure) when (failure is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            error.WriteLine($"RynthInteropRedirect: error: {failure.Message}");
            return 1;
        }
    }

    private static (int Redirected, int Inerted, int Swaps, int Types, int Members) Redirect(
        string assemblyPath, string? standInTypeName, IEnumerable<string> references, string[] inertModules, bool plainSwaps,
        IReadOnlyList<(string From, string To)> typeMaps, IReadOnlyList<(string FromType, string Member, string To)> memberMaps)
    {
        bool hasSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in references.Where(r => r.Length > 0).Select(r => Path.GetDirectoryName(r) ?? ".").Distinct())
            resolver.AddSearchDirectory(directory);
        using ModuleDefinition module = ModuleDefinition.ReadModule(
            assemblyPath,
            new ReaderParameters { ReadWrite = true, ReadSymbols = hasSymbols, AssemblyResolver = resolver });
        TypeDefinition[] standIns = standInTypeName is null
            ? []
            : standInTypeName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => module.GetType(name) ?? throw new InvalidOperationException($"{name} is not in {assemblyPath}"))
                .ToArray();

        int redirected = 0, inerted = 0;
        foreach (TypeDefinition type in module.GetTypes())
        {
            if (standIns.Contains(type))
                continue;
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.IsPInvokeImpl || method.PInvokeInfo is not { } pinvoke)
                    continue;
                string moduleName = pinvoke.Module.Name;
                string entryPoint = string.IsNullOrEmpty(pinvoke.EntryPoint) ? method.Name : pinvoke.EntryPoint;
                MethodDefinition? target = Win32Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase)
                    ? standIns.SelectMany(s => s.Methods).FirstOrDefault(
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
        int members = RedirectMembers(module, memberMaps);
        if (redirected + inerted + swaps + types + members > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        if (typeMaps.Count > 0)
            VerifyNoneLeft(assemblyPath, resolver, typeMaps);
        return (redirected, inerted, swaps, types, members);
    }

    /// <summary>
    /// Every call to a mapped static member (<c>Type::member</c> of a referenced type)
    /// becomes a call to the public static method of the same name, return and
    /// parameter types on the stand-in type (in the module). The stand-in's own
    /// methods are skipped, so its fallback can still call the real member.
    /// Returns how many calls were rewritten.
    /// </summary>
    private static int RedirectMembers(ModuleDefinition module, IReadOnlyList<(string FromType, string Member, string To)> maps)
    {
        int rewritten = 0;
        foreach ((string fromType, string member, string to) in maps)
        {
            TypeDefinition standIn = module.GetType(to) ?? throw new InvalidOperationException($"{to} is not in {module.Name}");
            var targets = new Dictionary<string, MethodReference>();   // by the called signature, so overloads each find their own
            foreach (TypeDefinition type in module.GetTypes())
            {
                if (type == standIn)
                    continue;
                foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is not MethodReference called || called.HasThis
                            || called.Name != member || called.DeclaringType.FullName != fromType)
                            continue;
                        if (!targets.TryGetValue(called.FullName, out MethodReference? target))
                            targets[called.FullName] = target = module.ImportReference(FindStatic(standIn, called));
                        instruction.Operand = target;
                        rewritten++;
                    }
                }
            }
        }

        return rewritten;
    }

    private static MethodDefinition FindStatic(TypeDefinition standIn, MethodReference called) =>
        standIn.Methods.FirstOrDefault(candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == called.Name
            && candidate.ReturnType.FullName == called.ReturnType.FullName
            && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(called.Parameters.Select(p => p.ParameterType.FullName)))
        ?? throw new InvalidOperationException(
            $"{standIn.FullName} has no public static {called.ReturnType.FullName} {called.Name}({string.Join(", ", called.Parameters.Select(p => p.ParameterType.FullName))})");

    /// <summary>
    /// Rebinds every use of each <c>from</c> type to its <c>to</c> type in the module:
    /// signatures, locals, operands, base types, interfaces, generic constraints, catch
    /// types, events, overrides and custom attributes. Only what holds a <c>from</c> type
    /// is rebuilt. Returns how many <c>from</c> types were found in use.
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
        // A custom attribute's blob names a type (typeof, boxed argument) by its serialized name.
        byte[][] blobNames = maps.Select(m => System.Text.Encoding.UTF8.GetBytes(m.From.Replace('/', '+'))).ToArray();

        TypeReference Map(TypeReference type)
        {
            switch (type)
            {
                case ByReferenceType byRef:
                    TypeReference byRefElement = Map(byRef.ElementType);
                    return ReferenceEquals(byRefElement, byRef.ElementType) ? type : new ByReferenceType(byRefElement);
                case PointerType pointer:
                    TypeReference pointee = Map(pointer.ElementType);
                    return ReferenceEquals(pointee, pointer.ElementType) ? type : new PointerType(pointee);
                case PinnedType pinned:
                    TypeReference pinnedElement = Map(pinned.ElementType);
                    return ReferenceEquals(pinnedElement, pinned.ElementType) ? type : new PinnedType(pinnedElement);
                case SentinelType sentinel:
                    TypeReference sentinelElement = Map(sentinel.ElementType);
                    return ReferenceEquals(sentinelElement, sentinel.ElementType) ? type : new SentinelType(sentinelElement);
                case RequiredModifierType required:
                    TypeReference requiredModifier = Map(required.ModifierType), requiredElement = Map(required.ElementType);
                    return ReferenceEquals(requiredModifier, required.ModifierType) && ReferenceEquals(requiredElement, required.ElementType)
                        ? type
                        : new RequiredModifierType(requiredModifier, requiredElement);
                case OptionalModifierType optional:
                    TypeReference optionalModifier = Map(optional.ModifierType), optionalElement = Map(optional.ElementType);
                    return ReferenceEquals(optionalModifier, optional.ModifierType) && ReferenceEquals(optionalElement, optional.ElementType)
                        ? type
                        : new OptionalModifierType(optionalModifier, optionalElement);
                case ArrayType array:
                    TypeReference arrayElement = Map(array.ElementType);
                    if (ReferenceEquals(arrayElement, array.ElementType))
                        return type;
                    var rebuilt = new ArrayType(arrayElement, array.Rank);
                    for (int d = 0; d < array.Rank; d++)
                        rebuilt.Dimensions[d] = new ArrayDimension(array.Dimensions[d].LowerBound, array.Dimensions[d].UpperBound);
                    return rebuilt;
                case GenericInstanceType generic:
                    TypeReference[] arguments = generic.GenericArguments.Select(Map).ToArray();
                    if (Unchanged(arguments, generic.GenericArguments))
                        return type;
                    var copy = new GenericInstanceType(generic.ElementType);
                    foreach (TypeReference argument in arguments)
                        copy.GenericArguments.Add(argument);
                    return copy;
            }

            if (!targets.TryGetValue(type.FullName, out TypeDefinition? target))
                return type;
            used.Add(type.FullName);
            return target;
        }

        MethodReference MapMethod(MethodReference method)
        {
            switch (method)
            {
                case MethodDefinition:
                    return method; // defined here: its signature is mapped in place
                case GenericInstanceMethod generic:
                    MethodReference element = MapMethod(generic.ElementMethod);
                    TypeReference[] arguments = generic.GenericArguments.Select(Map).ToArray();
                    if (ReferenceEquals(element, generic.ElementMethod) && Unchanged(arguments, generic.GenericArguments))
                        return method;
                    var instance = new GenericInstanceMethod(element);
                    foreach (TypeReference argument in arguments)
                        instance.GenericArguments.Add(argument);
                    return instance;
            }

            if (targets.TryGetValue(method.DeclaringType.FullName, out TypeDefinition? to))
            {
                used.Add(method.DeclaringType.FullName);
                return FindMember(to, method, Map);
            }

            // A member of a type built over a redirected one (List<From>, From[,]).
            TypeReference declaring = Map(method.DeclaringType);
            if (ReferenceEquals(declaring, method.DeclaringType))
                return method;
            var reference = new MethodReference(method.Name, Map(method.ReturnType), declaring)
            {
                HasThis = method.HasThis,
                ExplicitThis = method.ExplicitThis,
                CallingConvention = method.CallingConvention,
            };
            foreach (GenericParameter parameter in method.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(parameter.Name, reference));
            foreach (ParameterDefinition parameter in method.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, Map(parameter.ParameterType)));
            return reference;
        }

        // isStatic is null when the opcode does not say (ldtoken): the field matches by name and type alone.
        FieldReference MapField(FieldReference field, bool? isStatic)
        {
            if (field is FieldDefinition)
                return field;
            if (targets.TryGetValue(field.DeclaringType.FullName, out TypeDefinition? to))
            {
                used.Add(field.DeclaringType.FullName);
                string fieldType = Map(field.FieldType).FullName;
                string kind = isStatic switch { true => "static ", false => "instance ", null => "" };
                return to.Fields.FirstOrDefault(f => f.Name == field.Name && (isStatic is not { } wanted || f.IsStatic == wanted)
                                                     && f.FieldType.FullName == fieldType)
                    ?? throw new InvalidOperationException($"{to.FullName} has no {kind}field {fieldType} {field.Name}");
            }

            TypeReference declaring = Map(field.DeclaringType);
            return ReferenceEquals(declaring, field.DeclaringType) ? field : new FieldReference(field.Name, Map(field.FieldType), declaring);
        }

        bool Changes(CustomAttributeArgument argument) =>
            !ReferenceEquals(Map(argument.Type), argument.Type)
            || argument.Value switch
            {
                TypeReference type => !ReferenceEquals(Map(type), type),
                CustomAttributeArgument boxed => Changes(boxed),
                CustomAttributeArgument[] array => array.Any(Changes),
                _ => false,
            };

        CustomAttributeArgument MapArgument(CustomAttributeArgument argument) => new(
            Map(argument.Type),
            argument.Value switch
            {
                TypeReference type => Map(type),
                CustomAttributeArgument boxed => MapArgument(boxed),
                CustomAttributeArgument[] array => array.Select(MapArgument).ToArray(),
                var value => value,
            });

        void MapAttributes(ICustomAttributeProvider provider)
        {
            if (!provider.HasCustomAttributes)
                return;
            for (int index = 0; index < provider.CustomAttributes.Count; index++)
            {
                CustomAttribute attribute = provider.CustomAttributes[index];
                MethodReference constructor = MapMethod(attribute.Constructor);
                if (!ReferenceEquals(constructor, attribute.Constructor))
                    attribute.Constructor = constructor;
                // Reading the arguments makes Cecil re-encode the attribute, so only a blob that
                // may name a redirected type is opened, and one whose arguments turn out not to
                // hold one goes back as its original bytes.
                byte[] blob = attribute.GetBlob();
                if (!blobNames.Any(name => blob.AsSpan().IndexOf(name) >= 0))
                    continue;
                IEnumerable<CustomAttributeArgument> all = attribute.ConstructorArguments
                    .Concat(attribute.Fields.Select(f => f.Argument))
                    .Concat(attribute.Properties.Select(p => p.Argument));
                if (!all.Any(Changes))
                {
                    provider.CustomAttributes[index] = new CustomAttribute(attribute.Constructor, blob);
                    continue;
                }

                for (int i = 0; i < attribute.ConstructorArguments.Count; i++)
                    attribute.ConstructorArguments[i] = MapArgument(attribute.ConstructorArguments[i]);
                foreach (Mono.Collections.Generic.Collection<CustomAttributeNamedArgument> named in new[] { attribute.Fields, attribute.Properties })
                {
                    for (int i = 0; i < named.Count; i++)
                        named[i] = new CustomAttributeNamedArgument(named[i].Name, MapArgument(named[i].Argument));
                }
            }
        }

        void MapGenericParameters(IGenericParameterProvider provider)
        {
            if (!provider.HasGenericParameters)
                return;
            foreach (GenericParameter parameter in provider.GenericParameters)
            {
                MapAttributes(parameter);
                foreach (GenericParameterConstraint constraint in parameter.Constraints)
                    constraint.ConstraintType = Map(constraint.ConstraintType);
            }
        }

        MapAttributes(module.Assembly);
        MapAttributes(module);
        foreach (TypeDefinition type in module.GetTypes().Where(t => !standIns.Contains(t)).ToList())
        {
            MapAttributes(type);
            MapGenericParameters(type);
            if (type.BaseType is not null)
                type.BaseType = Map(type.BaseType);
            foreach (InterfaceImplementation implementation in type.Interfaces)
                implementation.InterfaceType = Map(implementation.InterfaceType);
            foreach (FieldDefinition field in type.Fields)
            {
                MapAttributes(field);
                field.FieldType = Map(field.FieldType);
            }

            foreach (PropertyDefinition property in type.Properties)
            {
                MapAttributes(property);
                property.PropertyType = Map(property.PropertyType);
                foreach (ParameterDefinition parameter in property.Parameters)
                    parameter.ParameterType = Map(parameter.ParameterType);
            }

            foreach (EventDefinition @event in type.Events)
            {
                MapAttributes(@event);
                @event.EventType = Map(@event.EventType);
            }

            foreach (MethodDefinition method in type.Methods)
            {
                MapAttributes(method);
                MapAttributes(method.MethodReturnType);
                MapGenericParameters(method);
                method.ReturnType = Map(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    MapAttributes(parameter);
                    parameter.ParameterType = Map(parameter.ParameterType);
                }

                for (int i = 0; i < method.Overrides.Count; i++)
                    method.Overrides[i] = MapMethod(method.Overrides[i]);
                if (!method.HasBody)
                    continue;
                foreach (VariableDefinition variable in method.Body.Variables)
                    variable.VariableType = Map(variable.VariableType);
                foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                {
                    if (handler.CatchType is not null)
                        handler.CatchType = Map(handler.CatchType);
                }

                foreach (Instruction instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference callee:
                            instruction.Operand = MapMethod(callee);
                            break;
                        case FieldReference field:
                            instruction.Operand = MapField(field, instruction.OpCode.Code switch
                            {
                                Code.Ldsfld or Code.Stsfld or Code.Ldsflda => true,
                                Code.Ldfld or Code.Stfld or Code.Ldflda => false,
                                _ => null,
                            });
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

    private static bool Unchanged(IEnumerable<TypeReference> mapped, IEnumerable<TypeReference> original) =>
        mapped.Zip(original).All(pair => ReferenceEquals(pair.First, pair.Second));

    /// <summary>
    /// Re-reads the written assembly and fails if any redirected type is still in a member
    /// reference's signature or in the type references.
    /// </summary>
    private static void VerifyNoneLeft(string assemblyPath, IAssemblyResolver resolver, IReadOnlyList<(string From, string To)> maps)
    {
        using ModuleDefinition written = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { AssemblyResolver = resolver });
        var froms = maps.Select(m => m.From).ToHashSet();

        bool Holds(TypeReference type) => type switch
        {
            GenericInstanceType generic => Holds(generic.ElementType) || generic.GenericArguments.Any(Holds),
            RequiredModifierType required => Holds(required.ModifierType) || Holds(required.ElementType),
            OptionalModifierType optional => Holds(optional.ModifierType) || Holds(optional.ElementType),
            TypeSpecification specification => Holds(specification.ElementType),
            _ => froms.Contains(type.FullName),
        };

        foreach (MemberReference member in written.GetMemberReferences())
        {
            bool holds = Holds(member.DeclaringType) || member switch
            {
                MethodReference method => Holds(method.ReturnType) || method.Parameters.Any(p => Holds(p.ParameterType)),
                FieldReference field => Holds(field.FieldType),
                _ => false,
            };
            if (holds)
                throw new InvalidOperationException($"{member.FullName} still uses a redirected type in {written.Name} after the redirect");
        }

        foreach (TypeReference reference in written.GetTypeReferences())
        {
            if (froms.Contains(reference.FullName))
                throw new InvalidOperationException($"{reference.FullName} is still referenced in {written.Name} after the redirect");
        }
    }

    private static MethodDefinition FindMember(TypeDefinition to, MethodReference callee, Func<TypeReference, TypeReference> map)
    {
        string returnType = map(callee.ReturnType).FullName;
        string[] parameters = callee.Parameters.Select(p => map(p.ParameterType).FullName).ToArray();
        return to.Methods.FirstOrDefault(candidate =>
                   candidate.IsPublic && candidate.Name == callee.Name && candidate.HasThis == callee.HasThis
                   && candidate.ReturnType.FullName == returnType
                   && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
               ?? throw new InvalidOperationException(
                   $"{to.FullName} has no {(callee.HasThis ? "instance" : "static")} {returnType} {callee.Name}({string.Join(", ", parameters)})");
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
        && a.Parameters.Count == b.Parameters.Count
        && a.Parameters.Zip(b.Parameters).All(pair => SameParameterType(pair.First.ParameterType, pair.Second.ParameterType));

    /// <summary>
    /// The same type, or by-ref structs with the same instance fields (names and types, in
    /// order): a stand-in can't name a P/Invoke's private nested struct (UiDropTargets' POINT),
    /// so it declares its own of the same layout.
    /// </summary>
    private static bool SameParameterType(TypeReference a, TypeReference b)
    {
        if (a.FullName == b.FullName)
            return true;
        if (a is not ByReferenceType { ElementType: var ea } || b is not ByReferenceType { ElementType: var eb })
            return false;
        if (ea.Resolve() is not { IsValueType: true } da || eb.Resolve() is not { IsValueType: true } db)
            return false;
        FieldDefinition[] fa = da.Fields.Where(f => !f.IsStatic).ToArray();
        FieldDefinition[] fb = db.Fields.Where(f => !f.IsStatic).ToArray();
        return fa.Length == fb.Length
            && fa.Zip(fb).All(pair => pair.First.Name == pair.Second.Name && pair.First.FieldType.FullName == pair.Second.FieldType.FullName);
    }

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
