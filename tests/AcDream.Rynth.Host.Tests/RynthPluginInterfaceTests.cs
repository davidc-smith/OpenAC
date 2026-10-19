using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthPluginInterfaceTests
{
    private readonly List<string> _log = new();

    private static IntPtr Call(RynthHost host, string? plugin, string? iface, uint version)
    {
        IntPtr name = plugin is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(plugin);
        IntPtr ifaceName = iface is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(iface);
        try
        {
            return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)host.Api->GetPluginInterfaceFn)(name, ifaceName, version);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(ifaceName);
        }
    }

    [Fact]
    public void TheSlotIsSet()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider)]);
        Assert.NotEqual(IntPtr.Zero, host.Api->GetPluginInterfaceFn);
    }

    [Fact]
    public void AnExactReportedNameIsAskedWithTheInterfaceAndVersion()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider)]);
        Assert.Equal((IntPtr)0x1111, Call(host, "provider", "Provider.Table", 3));
        Assert.Equal("Provider.Table", Provider.LastInterface);
        Assert.Equal(3u, Provider.LastVersion);
    }

    [Fact]
    public void AnExactNameWinsOverAContainsMatch()
    {
        // "ProviderPlus" contains "Provider" and loads first; the exact one still answers.
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(ProviderPlus), typeof(Provider)]);
        Assert.Equal((IntPtr)0x1111, Call(host, "Provider", "Provider.Table", 1));
    }

    [Fact]
    public void AContainsMatchFindsAPluginByPartOfItsName()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(ProviderPlus)]);
        Assert.Equal((IntPtr)0x2222, Call(host, "plus", "Any", 1));
    }

    [Fact]
    public void UnknownMissingOrSilentPluginsGiveZero()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider), typeof(NoQuery), typeof(Silent)]);
        Assert.Equal(IntPtr.Zero, Call(host, "Nobody", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "NoQuery", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "Silent", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, null, "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "Provider", null, 1));
    }

    [Fact]
    public void AFailedOrShutDownPluginIsNotAsked()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(FailsInit), typeof(Provider)]);
        // A plugin whose Init fails is never added to the host's list, so this half checks it is unreachable by name.
        Assert.Equal(IntPtr.Zero, Call(host, "FailsInit", "Any", 1));

        host.Plugins.Single(p => p.ReportedName == "Provider").Shutdown();
        Assert.Equal(IntPtr.Zero, Call(host, "Provider", "Provider.Table", 1));
    }

    private static class Provider
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("Provider");
        internal static string? LastInterface;
        internal static uint LastVersion;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version)
        {
            LastInterface = Marshal.PtrToStringAnsi(iface);
            LastVersion = version;
            return 0x1111;
        }
    }

    private static class ProviderPlus
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("ProviderPlus");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => 0x2222;
    }

    private static class NoQuery
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("NoQuery");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;
    }

    private static class Silent
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("Silent");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => IntPtr.Zero;
    }

    private static class FailsInit
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("FailsInit");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => -1;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => 0x3333;
    }
}
