using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit.Abstractions;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// RynthAi's Win32 P/Invokes, made inert at build time (tools/RynthInteropRedirect
/// --inert): in OpenAC they would read our own process's memory (FellowshipTracker
/// walks acclient structures at fixed addresses) or fail off Windows.
/// </summary>
public sealed class RynthAiInteropTests(ITestOutputHelper output)
{
    private static string Bare(string module) =>
        module.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? module[..^4] : module;

    private static Assembly Theirs => typeof(RynthCore.Plugin.RynthAi.PluginExports).Assembly;

    private static IEnumerable<(MethodInfo Method, string Module)> PInvokes() =>
        Theirs.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => (method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
            .Select(method => (method, method.GetCustomAttribute<DllImportAttribute>()?.Value ?? string.Empty));

    [Fact]
    public void ARedirectLeavesSignaturesWithoutTheRedirectedTypeUntouched()
    {
        // Their `float[,] HeightsZ` is System.Single[0...,0...] to the compiler; the rewriter
        // once rebuilt it as System.Single[,], so the field no longer matched its call sites.
        using ModuleDefinition module = ModuleDefinition.ReadModule(Theirs.Location);
        TypeDefinition data = module.GetType("RynthCore.Plugin.RynthAi.Raycasting.LandblockData");
        FieldDefinition field = data.Fields.Single(f => f.Name == "<HeightsZ>k__BackingField");
        MethodReference[] calls = module.GetTypes()
            .SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand).OfType<MethodReference>()
            .Where(c => c.Name == "get_HeightsZ").ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Equal(call.ReturnType.FullName, field.FieldType.FullName));
        Assert.Equal(field.FieldType.FullName, data.Methods.Single(m => m.Name == "get_HeightsZ").ReturnType.FullName);
        Assert.Equal("System.Single[0...,0...]", field.FieldType.FullName);
    }

    [Fact]
    public void NoWin32CallIsLeft()
    {
        // Allowlist: the install-folder registry read is the only native call RynthAi keeps.
        string[] left = PInvokes()
            .Where(p => !Bare(p.Module).Equals("advapi32", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Method.DeclaringType?.Name}.{p.Method.Name} ({p.Module})")
            .ToArray();
        Assert.Empty(left);
    }

    [Fact]
    public void TheirRegistryReadIsUntouched() =>
        Assert.Contains(PInvokes(), p => p.Module.Equals("advapi32.dll", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void FellowshipMemoryReadsSeeNothingReadable()
    {
        Type tracker = Theirs.GetType("RynthCore.Plugin.RynthAi.FellowshipTracker", throwOnError: true)!;
        MethodInfo readable = tracker.GetMethod("IsReadablePointer", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)readable.Invoke(null, [new IntPtr(0x00400000)])!);
    }

    [Fact]
    public void AnInertCallFailsWithItsOutputsZeroed()
    {
        Type module = Theirs.GetType("RynthCore.Plugin.RynthAi.RynthAiPlugin+PluginModule", throwOnError: true)!;
        MethodInfo getHandle = module.GetMethod("GetModuleHandleExW", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [0u, new IntPtr(1234), new IntPtr(5678)];
        Assert.False((bool)getHandle.Invoke(null, args)!);
        Assert.Equal(IntPtr.Zero, args[2]);
    }

    private static bool IsThreadStaticSwap(Instruction call) =>
        call.OpCode == OpCodes.Call
        && call.Operand is MethodReference { Name: "Exchange", DeclaringType.FullName: "System.Threading.Interlocked" } callee
        && callee.ReturnType.FullName == "System.IntPtr"
        && call.Previous?.Previous is { OpCode.Code: Code.Ldsflda, Operand: FieldReference field }
        && field.Resolve().CustomAttributes.Any(a => a.AttributeType.FullName == "System.ThreadStaticAttribute");

    // The redirect tool (--plain-threadstatic-swaps) turns RynthAi's Interlocked swaps of [ThreadStatic] pointers into plain ones; read the built IL to prove none is left.
    [Fact]
    public void NoInterlockedOnAThreadStaticIsLeft()
    {
        using var module = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(Theirs.Location)));
        string[] left = module.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody && method.Body.Instructions.Any(IsThreadStaticSwap))
            .Select(method => $"{method.DeclaringType.Name}.{method.Name}")
            .ToArray();
        Assert.Empty(left);
        Assert.Contains(module.GetTypes(), type => type.Name == "<RynthInteropRedirect>"
            && type.Methods.Any(method => method.Name == "ExchangeThreadStatic"));
    }

    // Their snapshot export swaps a [ThreadStatic] pointer with Interlocked.Exchange. On .NET 10 osx-arm64 the runtime can give an 8-byte thread static a 4-byte-aligned address, where Interlocked faults (DataMisalignedException, swallowed by their catch: the export answers null). The redirect tool turns that swap into a plain one (a thread static is only its own thread's); NoInterlockedOnAThreadStaticIsLeft guards it at IL level.
    [Fact]
    public void TheSnapshotAnswersOnAFreshThread()
    {
        using var sandbox = new AcDream.TestSupport.RynthConfigSandbox("rynth-ai-thread-");
        using var host = new AcDream.Rynth.Host.RynthHost(new FakeGameView { PlayerId = 0x50000001u }, _ => { }, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        string? json = null;
        var thread = new Thread(() => json = host.Plugins[0].CallStringExport("RynthPluginGetSnapshotJson"));
        thread.Start();
        thread.Join();
        Assert.NotNull(json);
    }

    [Fact]
    public void NoRegistryReferenceIsLeft()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(Theirs.Location);
        // A member reference's full name holds its declaring type and its signature.
        string[] left = module.GetTypeReferences().Select(t => t.FullName)
            .Concat(module.GetMemberReferences().Select(m => m.FullName))
            .Where(name => name.Contains("Microsoft.Win32.Registry", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        Assert.Empty(left);
    }

    [Fact]
    public void ComponentDatabaseFindsThePortalDatThroughTheStandIn()
    {
        string dir = Directory.CreateTempSubdirectory("rynth-reg").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "client_portal.dat"), []);
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = dir;
            Type db = Theirs.GetType("RynthCore.Plugin.RynthAi.ComponentDatabase", throwOnError: true)!;
            MethodInfo find = db.GetMethod("FindPortalDat", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(Path.Combine(dir, "client_portal.dat"), find.Invoke(null, null));
        }
        finally
        {
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GeometryLoaderOpensTheInstalledDatsThroughTheStandIn()
    {
        string dats = TestDats.Directory;
        if (!File.Exists(Path.Combine(dats, "client_cell_1.dat")))
        {
            // xunit 2 (this project) has no dynamic skip; the note shows in the test's output.
            output.WriteLine($"skipped: no dats at {dats}");
            return;
        }

        AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = dats;
        try
        {
            using var loader = new RynthCore.Plugin.RynthAi.Raycasting.GeometryLoader();
            Assert.True(loader.Initialize());
        }
        finally
        {
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;
        }
    }
}
