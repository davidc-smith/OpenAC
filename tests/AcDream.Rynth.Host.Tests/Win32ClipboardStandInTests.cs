using System.Reflection;
using AcDream.Rynth.ChatWindow;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// Their ChatModel's Win32 clipboard calls, redirected at build time to
/// <see cref="Win32ClipboardStandIn"/>: copy lines and the filter editor's
/// Import/Export reach OpenAC's clipboard on every OS.
/// </summary>
public sealed class Win32ClipboardStandInTests : IDisposable
{
    private string _clipboard = "";
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    public Win32ClipboardStandInTests()
    {
        _get = () => _clipboard;
        _set = text => _clipboard = text;
        Win32ClipboardStandIn.GetText = _get;
        Win32ClipboardStandIn.SetText = _set;
    }

    public void Dispose()
    {
        Win32ClipboardStandIn.GetText = null;
        Win32ClipboardStandIn.SetText = null;
    }

    [Theory]
    [InlineData("OpenClipboard")]
    [InlineData("CloseClipboard")]
    [InlineData("EmptyClipboard")]
    [InlineData("SetClipboardData")]
    [InlineData("GetClipboardData")]
    [InlineData("IsClipboardFormatAvailable")]
    [InlineData("GlobalAlloc")]
    [InlineData("GlobalLock")]
    [InlineData("GlobalUnlock")]
    [InlineData("GlobalFree")]
    [InlineData("GlobalSize")]
    public void TheirClipboardExternIsRedirected(string name)
    {
        MethodInfo? method = typeof(ChatModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(MethodAttributes.Static, method.Attributes & (MethodAttributes.PinvokeImpl | MethodAttributes.Static));
    }

    [Fact]
    public void CopyLinesReachesTheClipboard()
    {
        Assert.Equal("Copied 2 lines", ChatModel.CopyLines(["one", "two \U0001F600"]));
        Assert.Equal("one\r\ntwo \U0001F600", _clipboard);
    }

    [Fact]
    public void TheClipboardIsReadBack()
    {
        _clipboard = "from elsewhere";
        Assert.Equal("from elsewhere", ChatModel.GetClipboardText());
        Assert.Equal("from elsewhere", ChatModel.GetClipboardText());
    }

    [Fact]
    public void AnEmptyClipboardReadsAsNone()
    {
        Assert.Null(ChatModel.GetClipboardText());
        Assert.Equal("Clipboard is empty.", ChatModel.ImportRules(ChatModel.GetClipboardText()));
    }

    [Fact]
    public void WithNoClipboardTheCopyIsBusy()
    {
        Win32ClipboardStandIn.SetText = null;
        Assert.False(ChatModel.SetClipboardText("lost"));
        Assert.Equal("", _clipboard);
    }

    [Fact]
    public void AThrowingClipboardIsBusyNotThrown()
    {
        Win32ClipboardStandIn.SetText = _ => throw new InvalidOperationException("no window");
        Win32ClipboardStandIn.GetText = () => throw new InvalidOperationException("no window");
        Assert.False(ChatModel.SetClipboardText("lost"));
        Assert.Null(ChatModel.GetClipboardText());
    }

    [Fact]
    public void ExportPutsTheirJsonOnTheClipboard()
    {
        Assert.True(ChatModel.SetClipboardText(ChatModel.ExportRules()));
        Assert.Equal(ChatModel.ExportRules(), ChatModel.GetClipboardText());
    }
}
