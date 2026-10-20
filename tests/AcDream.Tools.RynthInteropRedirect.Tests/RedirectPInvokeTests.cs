using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect.Tests;

/// <summary>
/// P/Invoke redirection to stand-in types over one small assembly built here (App):
/// two user32 imports on App.Native and two stand-in types, one per import.
/// </summary>
public sealed class RedirectPInvokeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rynth-pinvoke-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string AppPath => Path.Combine(_dir, "App.dll");

    [Fact]
    public void EachImportGoesToTheListedTypeThatHasIt()
    {
        Build();

        (int code, string output, string error) = Run("App.ClipStandIn,App.KeyStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("2 P/Invoke(s) redirected to App.ClipStandIn,App.KeyStandIn", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        Assert.Equal(["System.Int16 App.KeyStandIn::GetKeyState(System.Int32)"], Calls(app, "GetKeyState"));
        Assert.Equal(["System.Boolean App.ClipStandIn::OpenClipboard(System.IntPtr)"], Calls(app, "OpenClipboard"));
    }

    [Fact]
    public void OneTypeStillWorks()
    {
        Build();

        (int code, string output, string error) = Run("App.KeyStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("1 P/Invoke(s) redirected to App.KeyStandIn", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingListedTypeFailsAndNamesIt()
    {
        Build();

        (int code, _, string error) = Run("App.ClipStandIn,App.Nowhere");

        Assert.Equal(1, code);
        Assert.Contains("App.Nowhere is not in", error, StringComparison.Ordinal);
    }

    private static string[] Calls(ModuleDefinition app, string method) =>
        app.GetType("App.Native").Methods.Single(m => m.Name == method).Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.FullName).ToArray();

    private (int Code, string Output, string Error) Run(string standIns)
    {
        string references = Path.Combine(_dir, "refs.txt");
        File.WriteAllText(references, string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = Program.Run([AppPath, standIns, references], output, error);
        return (code, output.ToString(), error.ToString());
    }

    private ModuleDefinition Read() =>
        ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(AppPath)));

    private void Build()
    {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = assembly.MainModule;
        var user32 = new ModuleReference("user32.dll");
        app.ModuleReferences.Add(user32);

        var native = new TypeDefinition("App", "Native", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        app.Types.Add(native);
        native.Methods.Add(Import(app, user32, "GetKeyState", app.TypeSystem.Int16, app.TypeSystem.Int32));
        native.Methods.Add(Import(app, user32, "OpenClipboard", app.TypeSystem.Boolean, app.TypeSystem.IntPtr));

        app.Types.Add(StandIn(app, "KeyStandIn", "GetKeyState", app.TypeSystem.Int16, app.TypeSystem.Int32));
        app.Types.Add(StandIn(app, "ClipStandIn", "OpenClipboard", app.TypeSystem.Boolean, app.TypeSystem.IntPtr));
        assembly.Write(AppPath);
    }

    private static MethodDefinition Import(ModuleDefinition app, ModuleReference module, string name, TypeReference returns, TypeReference parameter)
    {
        var method = new MethodDefinition(name,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig, returns)
        {
            PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvWinapi, name, module),
            ImplAttributes = MethodImplAttributes.PreserveSig,
        };
        method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }

    private static TypeDefinition StandIn(ModuleDefinition app, string type, string name, TypeReference returns, TypeReference parameter)
    {
        var standIn = new TypeDefinition("App", type, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, returns);
        method.Parameters.Add(new ParameterDefinition(parameter));
        ILProcessor il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        standIn.Methods.Add(method);
        return standIn;
    }
}
