using AcDream.ClientExtensions;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthChatKeyboardTests : IDisposable
{
    private readonly List<string> _calls = new();
    private readonly List<string> _log = new();
    private string _clipboard = "";
    private readonly RynthChatKeyboard _keys;
    // The hooks are process-wide and ChatModel.InstallInput sets them once per
    // process, so they are put back as they were for the other tests.
    private readonly BackendHooks _saved = BackendHooks.Save();

    public RynthChatKeyboardTests()
    {
        Win32Backend.ChatCaptureActive = false;
        Win32Backend.TellRequested = false;
        Win32Backend.OnChatChar = c => _calls.Add("char:" + c);
        Win32Backend.OnChatBackspace = () => _calls.Add("back");
        Win32Backend.OnChatDelete = () => _calls.Add("del");
        Win32Backend.OnChatLeft = () => _calls.Add("left");
        Win32Backend.OnChatRight = () => _calls.Add("right");
        Win32Backend.OnChatHome = () => _calls.Add("home");
        Win32Backend.OnChatEnd = () => _calls.Add("end");
        Win32Backend.OnChatUp = () => _calls.Add("up");
        Win32Backend.OnChatDown = () => _calls.Add("down");
        Win32Backend.OnChatSend = () => _calls.Add("send");
        Win32Backend.OnChatCancel = () => _calls.Add("cancel");
        Win32Backend.OnChatPaste = () => _calls.Add("paste");
        Win32Backend.OnChatCopy = () => _calls.Add("copy");
        Win32Backend.OnChatCut = () => _calls.Add("cut");
        Win32Backend.OnChatCaptureActivated = () => _calls.Add("activated");
        Win32Backend.OnChatTellSelected = () => _calls.Add("tell");
        _keys = new RynthChatKeyboard(_log.Add, () => _clipboard, text => _clipboard = text, isWindows: false, inputText: () => "typed");
    }

    public void Dispose()
    {
        _saved.Restore();
        ChatHooks.SuppressOriginalChat = false;
        ChatHooks.ChatPanelShown = false;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
    }

    [Fact]
    public void NothingHappensWhileNotCapturing()
    {
        _keys.KeyDown(OverlayKey.Backspace, OverlayModifiers.None);
        _keys.Character('a');
        Assert.Empty(_calls);
        Assert.False(_keys.Capturing);
    }

    [Theory]
    [InlineData(OverlayKey.Backspace, "back")]
    [InlineData(OverlayKey.Delete, "del")]
    [InlineData(OverlayKey.Left, "left")]
    [InlineData(OverlayKey.Right, "right")]
    [InlineData(OverlayKey.Home, "home")]
    [InlineData(OverlayKey.End, "end")]
    [InlineData(OverlayKey.Up, "up")]
    [InlineData(OverlayKey.Down, "down")]
    public void EditingKeysReachTheirHooks(OverlayKey key, string call)
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(key, OverlayModifiers.None);
        Assert.Equal([call], _calls);
    }

    [Fact]
    public void PrintableCharactersAreTypedAndControlCharactersAreNot()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.Character('h');
        _keys.Character('\r');
        _keys.Character((char)0x7F);
        Assert.Equal(["char:h"], _calls);
    }

    [Fact]
    public void EnterSendsOnTheNextTickAndSwallowsItsTail()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.True(_keys.Capturing);
        Assert.Empty(_calls);
        _keys.Tick();
        Assert.Equal(["send"], _calls);
        _keys.KeyUp(OverlayKey.Enter);
        Assert.False(_keys.Capturing);
    }

    [Fact]
    public void EscapeCancelsAtOnceAndSwallowsItsTail()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Escape, OverlayModifiers.None);
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.Equal(["cancel"], _calls);
        Assert.True(_keys.Capturing);
        _keys.KeyUp(OverlayKey.Escape);
        Assert.False(_keys.Capturing);
    }

    [Fact]
    public void OffWindowsPasteTypesTheClipboard()
    {
        _clipboard = "a\nb";
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.V, OverlayModifiers.Control);
        Assert.Equal(["char:a", "char:b"], _calls);
    }

    [Fact]
    public void OffWindowsCopyAndCutUseTheClipboard()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.C, OverlayModifiers.Control);
        Assert.Equal("typed", _clipboard);
        _clipboard = "";
        _keys.KeyDown(OverlayKey.X, OverlayModifiers.Control);
        Assert.Equal("typed", _clipboard);
        Assert.Equal(["cancel"], _calls);
        Assert.True(Win32Backend.ChatCaptureActive);
    }

    [Fact]
    public void OnWindowsClipboardKeysUseTheirHooks()
    {
        var keys = new RynthChatKeyboard(_log.Add, () => "", _ => { }, isWindows: true, inputText: () => "");
        Win32Backend.ChatCaptureActive = true;
        keys.KeyDown(OverlayKey.V, OverlayModifiers.Control);
        keys.KeyDown(OverlayKey.Insert, OverlayModifiers.Shift);
        keys.KeyDown(OverlayKey.C, OverlayModifiers.Control);
        keys.KeyDown(OverlayKey.X, OverlayModifiers.Control);
        Assert.Equal(["paste", "paste", "copy", "cut"], _calls);
    }

    [Fact]
    public void TellRunsOnTheNextTick()
    {
        Win32Backend.RequestTellSelected();
        Assert.Empty(_calls);
        _keys.Tick();
        Assert.Equal(["tell"], _calls);
        _keys.Tick();
        Assert.Single(_calls);
    }

    [Fact]
    public void TheClaimFollowsTheirOwnershipRule()
    {
        Assert.False(_keys.TryClaimEntry());
        ChatHooks.SuppressOriginalChat = true;
        ChatHooks.ChatPanelShown = true;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(_keys.TryClaimEntry());
        Assert.True(Win32Backend.ChatCaptureActive);
        Assert.Equal(["activated"], _calls);
        Assert.False(_keys.TryClaimEntry());
    }

    [Fact]
    public void TheClaimedEnterIsNotASend()
    {
        ChatHooks.SuppressOriginalChat = true;
        ChatHooks.ChatPanelShown = true;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(_keys.TryClaimEntry());
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);   // the same keypress, reaching the sink second
        _keys.Tick();
        Assert.True(Win32Backend.ChatCaptureActive);
        Assert.Equal(["activated"], _calls);
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);   // a later Enter sends
        _keys.Tick();
        Assert.Equal(["activated", "send"], _calls);
    }

    [Fact]
    public void AThrowingHookIsLoggedNotThrown()
    {
        Win32Backend.OnChatBackspace = () => throw new InvalidOperationException("hook failed");
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Backspace, OverlayModifiers.None);
        Assert.Contains(_log, l => l.Contains("hook failed", StringComparison.Ordinal));
    }

    /// <summary>Every Win32Backend stand-in hook and flag, as they were.</summary>
    private sealed record BackendHooks(
        bool CaptureActive,
        bool TellRequested,
        Action? CaptureActivated,
        Action? TellSelected,
        Action<char>? Char,
        Action? Backspace,
        Action? Delete,
        Action? Send,
        Action? Cancel,
        Action? Left,
        Action? Right,
        Action? Home,
        Action? End,
        Action? Up,
        Action? Down,
        Action? Paste,
        Action? Copy,
        Action? Cut)
    {
        public static BackendHooks Save() => new(
            Win32Backend.ChatCaptureActive,
            Win32Backend.TellRequested,
            Win32Backend.OnChatCaptureActivated,
            Win32Backend.OnChatTellSelected,
            Win32Backend.OnChatChar,
            Win32Backend.OnChatBackspace,
            Win32Backend.OnChatDelete,
            Win32Backend.OnChatSend,
            Win32Backend.OnChatCancel,
            Win32Backend.OnChatLeft,
            Win32Backend.OnChatRight,
            Win32Backend.OnChatHome,
            Win32Backend.OnChatEnd,
            Win32Backend.OnChatUp,
            Win32Backend.OnChatDown,
            Win32Backend.OnChatPaste,
            Win32Backend.OnChatCopy,
            Win32Backend.OnChatCut);

        public void Restore()
        {
            Win32Backend.ChatCaptureActive = CaptureActive;
            Win32Backend.TellRequested = TellRequested;
            Win32Backend.OnChatCaptureActivated = CaptureActivated;
            Win32Backend.OnChatTellSelected = TellSelected;
            Win32Backend.OnChatChar = Char;
            Win32Backend.OnChatBackspace = Backspace;
            Win32Backend.OnChatDelete = Delete;
            Win32Backend.OnChatSend = Send;
            Win32Backend.OnChatCancel = Cancel;
            Win32Backend.OnChatLeft = Left;
            Win32Backend.OnChatRight = Right;
            Win32Backend.OnChatHome = Home;
            Win32Backend.OnChatEnd = End;
            Win32Backend.OnChatUp = Up;
            Win32Backend.OnChatDown = Down;
            Win32Backend.OnChatPaste = Paste;
            Win32Backend.OnChatCopy = Copy;
            Win32Backend.OnChatCut = Cut;
        }
    }
}
