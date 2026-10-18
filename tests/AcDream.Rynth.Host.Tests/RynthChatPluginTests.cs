using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthChatPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-chat-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public void Dispose() => _sandbox.Dispose();

    private RynthHost CreateChatHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);

    [Fact]
    public void ChatLoads()
    {
        using RynthHost host = CreateChatHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void ALineReachesTheScrollbackExport()
    {
        using RynthHost host = CreateChatHost();
        RynthPlugin chat = host.Plugins[0];
        chat.OnChatWindowText("Bob says, \"hi\"", 2);

        Assert.True(chat.TryGetExport("RynthChatGetScrollbackJson", out IntPtr get));
        IntPtr json = ((delegate* unmanaged[Cdecl]<ulong, IntPtr>)get)(0);
        string text = Marshal.PtrToStringAnsi(json) ?? "";

        Assert.Contains("\"sender\":\"Bob\"", text, StringComparison.Ordinal);
        Assert.Contains("\"chan\":\"Chat\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SendLineRunsThroughTheChatParser()
    {
        using RynthHost host = CreateChatHost();
        Assert.True(host.Plugins[0].TryGetExport("RynthChatSendLine", out IntPtr send));

        IntPtr text = Marshal.StringToHGlobalAnsi("/hello");
        try { ((delegate* unmanaged[Cdecl]<IntPtr, void>)send)(text); }
        finally { Marshal.FreeHGlobal(text); }

        Assert.Contains("/hello", _view.SubmittedCommands);
    }

    [Fact]
    public void TryGetExportIsFalseForUnknownNamesAndAfterShutdown()
    {
        RynthHost host = CreateChatHost();
        RynthPlugin chat = host.Plugins[0];
        Assert.False(chat.TryGetExport("NoSuchExport", out _));
        host.Dispose();
        Assert.False(chat.TryGetExport("RynthChatSendLine", out _));
    }
}
