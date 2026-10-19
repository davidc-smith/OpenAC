using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect.Tests;

/// <summary>
/// <c>--redirect-type</c> over two small assemblies built here: Ext (the "referenced"
/// assembly, standing for Microsoft.Win32.Registry) and App (the code that uses it,
/// with stand-ins for Ext's types).
/// </summary>
public sealed class RedirectTypeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rynth-redirect-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private enum StandInGet { Present, Missing, ReturnsInt }

    private string AppPath => Path.Combine(_dir, "App.dll");

    private static readonly string[] TypeMaps =
        ["--redirect-type", "Ext.Key=App.KeyStandIn", "--redirect-type", "Ext.IThing=App.ThingStandIn"];

    [Fact]
    public void CallsFieldsAndSignaturesMoveToTheStandIn()
    {
        Build();

        (int code, string output, string error) = Run(TypeMaps);

        Assert.True(code == 0, error);
        Assert.Contains("2 type(s) redirected", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        TypeDefinition user = app.GetType("App.User");
        Assert.Equal("App.KeyStandIn", user.Fields.Single(f => f.Name == "_key").FieldType.FullName);
        MethodReference[] calls = user.Methods.Single(m => m.Name == "Read").Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Equal(["App.KeyStandIn App.KeyStandIn::Open(System.String)", "System.String App.KeyStandIn::Get(System.String)"],
            calls.Select(c => c.FullName));
        Assert.DoesNotContain(app.GetTypeReferences(), t => t.Namespace == "Ext");
    }

    [Fact]
    public void ARerunFindsNothingToDo()
    {
        Build();
        Assert.Equal(0, Run(TypeMaps).Code);
        byte[] once = File.ReadAllBytes(AppPath);

        (int code, string output, string error) = Run(TypeMaps);

        Assert.True(code == 0, error);
        Assert.Contains("0 type(s) redirected", output, StringComparison.Ordinal);
        Assert.Equal(once, File.ReadAllBytes(AppPath));
    }

    [Fact]
    public void SignaturesWithoutARedirectedTypeAreUntouched()
    {
        Build();

        Assert.Equal(0, Run(TypeMaps).Code);

        using ModuleDefinition app = Read();
        TypeDefinition user = app.GetType("App.User");
        Assert.Equal("System.Single[0...,0...]", user.Fields.Single(f => f.Name == "Heights").FieldType.FullName);
        Assert.Equal("System.Int32 App.User::Plain(System.Int32[])", user.Methods.Single(m => m.Name == "Plain").FullName);
    }

    [Fact]
    public void BaseInterfaceCatchEventAttributeAndConstraintAreMapped()
    {
        Build();

        Assert.Equal(0, Run(TypeMaps).Code);

        using ModuleDefinition app = Read();
        TypeDefinition derived = app.GetType("App.Derived");
        Assert.Equal("App.KeyStandIn", derived.BaseType.FullName);
        Assert.Equal("App.ThingStandIn", derived.Interfaces.Single().InterfaceType.FullName);
        TypeDefinition user = app.GetType("App.User");
        Assert.Equal("App.KeyStandIn", user.Methods.Single(m => m.Name == "Guard").Body.ExceptionHandlers.Single().CatchType.FullName);
        Assert.Equal("App.KeyStandIn", user.Events.Single().EventType.FullName);
        Assert.Equal("App.KeyStandIn", ((TypeReference)user.CustomAttributes.Single().ConstructorArguments.Single().Value).FullName);
        Assert.Equal("App.ThingStandIn",
            user.Methods.Single(m => m.Name == "Constrained").GenericParameters.Single().Constraints.Single().ConstraintType.FullName);
    }

    [Fact]
    public void AMissingMemberFailsAndNamesIt()
    {
        Build(StandInGet.Missing);

        (int code, _, string error) = Run(TypeMaps);

        Assert.Equal(1, code);
        Assert.Contains("App.KeyStandIn has no instance System.String Get(System.String)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberWithTheWrongReturnTypeFails()
    {
        Build(StandInGet.ReturnsInt);

        (int code, _, string error) = Run(TypeMaps);

        Assert.Equal(1, code);
        Assert.Contains("System.String Get(System.String)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceThatStillHoldsAFromTypeFailsTheRun()
    {
        // Ext.Util.Take(Ext.Key) is not a member of a redirected type, so its signature keeps Ext.Key.
        Build(callUtil: true);

        (int code, _, string error) = Run(TypeMaps);

        Assert.Equal(1, code);
        Assert.Contains("Ext.Util::Take(Ext.Key) still uses a redirected type", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFromGivenTwiceIsAUsageError()
    {
        Build();

        (int code, _, string error) = Run(["--redirect-type", "Ext.Key=App.KeyStandIn", "--redirect-type", "Ext.Key=App.ThingStandIn"]);

        Assert.Equal(2, code);
        Assert.StartsWith("usage:", error, StringComparison.Ordinal);
    }

    private (int Code, string Output, string Error) Run(string[] options)
    {
        string references = Path.Combine(_dir, "refs.txt");
        File.WriteAllText(references, Path.Combine(_dir, "Ext.dll"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = Program.Run([AppPath, "-", references, .. options], output, error);
        return (code, output.ToString(), error.ToString());
    }

    private ModuleDefinition Read()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_dir);
        return ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(AppPath)), new ReaderParameters { AssemblyResolver = resolver });
    }

    private void Build(StandInGet get = StandInGet.Present, bool callUtil = false)
    {
        using AssemblyDefinition extAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Ext", new Version(1, 0)), "Ext", ModuleKind.Dll);
        ModuleDefinition ext = extAssembly.MainModule;
        TypeDefinition key = Class(ext, "Ext", "Key", ext.TypeSystem.Object);
        MethodDefinition open = Method(key, "Open", key, MethodAttributes.Static, ext.TypeSystem.String);
        MethodDefinition getValue = Method(key, "Get", ext.TypeSystem.String, 0, ext.TypeSystem.String);
        var thing = new TypeDefinition("Ext", "IThing", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        ext.Types.Add(thing);
        TypeDefinition util = Class(ext, "Ext", "Util", ext.TypeSystem.Object);
        MethodDefinition take = Method(util, "Take", ext.TypeSystem.Void, MethodAttributes.Static, key);
        extAssembly.Write(Path.Combine(_dir, "Ext.dll"));

        using AssemblyDefinition appAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = appAssembly.MainModule;
        TypeReference extKey = app.ImportReference(key), extThing = app.ImportReference(thing);

        TypeDefinition keyStandIn = Class(app, "App", "KeyStandIn", app.TypeSystem.Object);
        Method(keyStandIn, "Open", keyStandIn, MethodAttributes.Static, app.TypeSystem.String);
        if (get != StandInGet.Missing)
            Method(keyStandIn, "Get", get == StandInGet.ReturnsInt ? app.TypeSystem.Int32 : app.TypeSystem.String, 0, app.TypeSystem.String);
        app.Types.Add(new TypeDefinition("App", "ThingStandIn", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract));

        TypeDefinition tag = Class(app, "App", "TagAttribute", app.ImportReference(typeof(Attribute)));
        MethodDefinition tagConstructor = Method(tag, ".ctor", app.TypeSystem.Void,
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, app.ImportReference(typeof(Type)));

        TypeDefinition user = Class(app, "App", "User", app.TypeSystem.Object);
        user.Fields.Add(new FieldDefinition("_key", FieldAttributes.Private, extKey));
        var heights = new ArrayType(app.TypeSystem.Single, 2);
        heights.Dimensions[0] = new ArrayDimension(0, null);
        heights.Dimensions[1] = new ArrayDimension(0, null);
        user.Fields.Add(new FieldDefinition("Heights", FieldAttributes.Public, heights));
        Method(user, "Plain", app.TypeSystem.Int32, 0, new ArrayType(app.TypeSystem.Int32));
        user.Events.Add(new EventDefinition("Changed", EventAttributes.None, extKey));
        var attribute = new CustomAttribute(tagConstructor);
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(app.ImportReference(typeof(Type)), extKey));
        user.CustomAttributes.Add(attribute);

        MethodDefinition read = Method(user, "Read", app.TypeSystem.String, 0);
        read.Body.Instructions.Clear();
        ILProcessor il = read.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "k");
        il.Emit(OpCodes.Call, app.ImportReference(open));
        il.Emit(OpCodes.Ldstr, "v");
        il.Emit(OpCodes.Callvirt, app.ImportReference(getValue));
        if (callUtil)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, app.ImportReference(take));
        }

        il.Emit(OpCodes.Ret);

        MethodDefinition guard = Method(user, "Guard", app.TypeSystem.Void, 0);
        guard.Body.Instructions.Clear();
        il = guard.Body.GetILProcessor();
        Instruction end = il.Create(OpCodes.Ret);
        Instruction tryStart = il.Create(OpCodes.Leave, end), handlerStart = il.Create(OpCodes.Pop);
        il.Append(tryStart);
        il.Append(handlerStart);
        il.Emit(OpCodes.Leave, end);
        il.Append(end);
        guard.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart, TryEnd = handlerStart, HandlerStart = handlerStart, HandlerEnd = end, CatchType = extKey,
        });

        MethodDefinition constrained = Method(user, "Constrained", app.TypeSystem.Void, 0);
        var parameter = new GenericParameter("T", constrained);
        parameter.Constraints.Add(new GenericParameterConstraint(extThing));
        constrained.GenericParameters.Add(parameter);

        TypeDefinition derived = Class(app, "App", "Derived", extKey);
        derived.Interfaces.Add(new InterfaceImplementation(extThing));
        appAssembly.Write(AppPath);
    }

    private static TypeDefinition Class(ModuleDefinition module, string ns, string name, TypeReference baseType)
    {
        var type = new TypeDefinition(ns, name, TypeAttributes.Public | TypeAttributes.Class, baseType);
        module.Types.Add(type);
        return type;
    }

    /// <summary>A public method whose body only returns (the IL is read, never run).</summary>
    private static MethodDefinition Method(TypeDefinition type, string name, TypeReference returnType, MethodAttributes extra, params TypeReference[] parameters)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.HideBySig | extra, returnType);
        foreach (TypeReference parameter in parameters)
            method.Parameters.Add(new ParameterDefinition(parameter));
        ILProcessor il = method.Body.GetILProcessor();
        if (returnType.MetadataType != MetadataType.Void)
            il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        type.Methods.Add(method);
        return method;
    }
}
