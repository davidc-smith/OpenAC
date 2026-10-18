using System.Reflection;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// RynthAi's Win32 P/Invokes, made inert at build time (tools/RynthInteropRedirect
/// --inert): in OpenAC they would read our own process's memory (FellowshipTracker
/// walks acclient structures at fixed addresses) or fail off Windows.
/// </summary>
public sealed class RynthAiInteropTests
{
    private static readonly string[] InertModules = ["kernel32.dll", "user32.dll", "shell32.dll", "winmm.dll"];

    private static Assembly Theirs => typeof(RynthCore.Plugin.RynthAi.PluginExports).Assembly;

    private static IEnumerable<(MethodInfo Method, string Module)> PInvokes() =>
        Theirs.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method => (method, method.GetCustomAttribute<DllImportAttribute>()?.Value ?? string.Empty));

    [Fact]
    public void NoWin32CallIsLeft()
    {
        string[] left = PInvokes()
            .Where(p => InertModules.Contains(p.Module, StringComparer.OrdinalIgnoreCase))
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

    // Their snapshot export swaps a [ThreadStatic] pointer with Interlocked.Exchange. On .NET 10 osx-arm64, quick-JIT code faults there (DataMisalignedException, swallowed by their catch, so the export answers null) on a thread whose first touch of the field is the export; TieredCompilationQuickJitForLoops=false in every process that loads RynthAi avoids it.
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
}
