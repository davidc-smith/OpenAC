using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect.Tests;

/// <summary>
/// <c>--redirect-member</c> over two small assemblies built here: Ext (standing for
/// System.Runtime's Environment) and App (code calling it, with a stand-in).
/// </summary>
public sealed class RedirectMemberTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rynth-member-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string AppPath => Path.Combine(_dir, "App.dll");

    private static readonly string[] Map = ["--redirect-member", "Ext.Env::get_Path=App.PathStandIn"];

    [Fact]
    public void CallsToTheMemberMoveToTheStandInAndOthersStay()
    {
        Build();

        (int code, string output, string error) = Run(Map);

        Assert.True(code == 0, error);
        Assert.Contains("1 member call(s) redirected", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        string[] calls = Calls(app, "App.User", "Find");
        Assert.Equal(["System.String App.PathStandIn::get_Path()", "System.String Ext.Env::get_Other()"], calls);
    }

    [Fact]
    public void TheStandInsOwnCallIsLeftAlone()
    {
        Build();

        Assert.Equal(0, Run(Map).Code);

        using ModuleDefinition app = Read();
        Assert.Equal(["System.String Ext.Env::get_Path()"], Calls(app, "App.PathStandIn", "get_Path"));
    }

    [Fact]
    public void AMissingStandInMemberFailsAndNamesIt()
    {
        Build(standInHasPath: false);

        (int code, _, string error) = Run(Map);

        Assert.Equal(1, code);
        Assert.Contains("App.PathStandIn has no public static System.String get_Path()", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ARerunFindsNothingToDo()
    {
        Build();
        Assert.Equal(0, Run(Map).Code);
        byte[] once = File.ReadAllBytes(AppPath);

        (int code, string output, string error) = Run(Map);

        Assert.True(code == 0, error);
        Assert.Contains("0 member call(s) redirected", output, StringComparison.Ordinal);
        Assert.Equal(once, File.ReadAllBytes(AppPath));
    }

    [Theory]
    [InlineData("Ext.Env.get_Path=App.PathStandIn")]
    [InlineData("Ext.Env::get_Path")]
    public void AMalformedMapIsAUsageError(string map)
    {
        Build();

        (int code, _, string error) = Run(["--redirect-member", map]);

        Assert.Equal(2, code);
        Assert.StartsWith("usage:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameMemberTwiceIsAUsageError()
    {
        Build();

        (int code, _, string error) = Run([.. Map, .. Map]);

        Assert.Equal(2, code);
        Assert.StartsWith("usage:", error, StringComparison.Ordinal);
    }

    private static string[] Calls(ModuleDefinition app, string type, string method) =>
        app.GetType(type).Methods.Single(m => m.Name == method).Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.FullName).ToArray();

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

    private void Build(bool standInHasPath = true)
    {
        using AssemblyDefinition extAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Ext", new Version(1, 0)), "Ext", ModuleKind.Dll);
        ModuleDefinition ext = extAssembly.MainModule;
        var env = new TypeDefinition("Ext", "Env", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, ext.TypeSystem.Object);
        ext.Types.Add(env);
        MethodDefinition getPath = StaticGetter(env, "get_Path", ext.TypeSystem.String);
        MethodDefinition getOther = StaticGetter(env, "get_Other", ext.TypeSystem.String);
        extAssembly.Write(Path.Combine(_dir, "Ext.dll"));

        using AssemblyDefinition appAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = appAssembly.MainModule;
        MethodReference path = app.ImportReference(getPath), other = app.ImportReference(getOther);

        var standIn = new TypeDefinition("App", "PathStandIn", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        app.Types.Add(standIn);
        var standInGet = new MethodDefinition(standInHasPath ? "get_Path" : "get_Elsewhere",
            MethodAttributes.Public | MethodAttributes.Static, app.TypeSystem.String);
        standIn.Methods.Add(standInGet);
        ILProcessor s = standInGet.Body.GetILProcessor();
        s.Emit(OpCodes.Call, path);   // the fallback: the real member
        s.Emit(OpCodes.Ret);

        var user = new TypeDefinition("App", "User", TypeAttributes.Public, app.TypeSystem.Object);
        app.Types.Add(user);
        var find = new MethodDefinition("Find", MethodAttributes.Public | MethodAttributes.Static, app.TypeSystem.Void);
        user.Methods.Add(find);
        ILProcessor f = find.Body.GetILProcessor();
        f.Emit(OpCodes.Call, path);
        f.Emit(OpCodes.Pop);
        f.Emit(OpCodes.Call, other);
        f.Emit(OpCodes.Pop);
        f.Emit(OpCodes.Ret);
        appAssembly.Write(AppPath);
    }

    private static MethodDefinition StaticGetter(TypeDefinition type, string name, TypeReference returns)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, returns);
        type.Methods.Add(method);
        ILProcessor il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
