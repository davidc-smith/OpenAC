using AcDream.ClientExtensions;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host;

/// <summary>
/// OpenAC's keyboard for RynthCore's chat input line: the key handling of
/// RynthCore's Win32Backend (which subclasses acclient's window and is not
/// used here), done over the overlay's keyboard sink on every OS. Keys go to
/// their ChatModel through the Win32Backend stand-in's OnChat* hooks. Enter
/// and Esc end capture at once but keep the keyboard until they come back up
/// (their _swallowEnterTail), so the same keypress never reaches OpenAC. The
/// send runs on the next tick (their deferred WM_RYNTHCORE_CHAT). Off Windows,
/// paste/copy/cut use the overlay clipboard: their ChatModel uses the Win32
/// clipboard. Game thread.
/// </summary>
internal sealed class RynthChatKeyboard : IOverlayKeyboardSink
{
    private readonly Action<string> _log;
    private readonly Func<string> _getClipboard;
    private readonly Action<string> _setClipboard;
    private readonly bool _isWindows;
    private readonly Func<string> _inputText;
    private OverlayKey? _swallowUntilUp;
    private bool _sendPending;
    private bool _enterDown;
    private bool _ignoreEnterUntilUp;

    public RynthChatKeyboard(Action<string> log, Func<string> getClipboard, Action<string> setClipboard, bool isWindows, Func<string> inputText)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(getClipboard);
        ArgumentNullException.ThrowIfNull(setClipboard);
        ArgumentNullException.ThrowIfNull(inputText);
        _log = log;
        _getClipboard = getClipboard;
        _setClipboard = setClipboard;
        _isWindows = isWindows;
        _inputText = inputText;
    }

    public bool Capturing => Win32Backend.ChatCaptureActive || _swallowUntilUp is not null;

    public void KeyDown(OverlayKey key, OverlayModifiers modifiers)
    {
        if (key == OverlayKey.Enter)
            _enterDown = true;
        // A new keystroke began: the Enter/Esc tail is over (their Win32Backend
        // stops guarding on any WM_KEYDOWN).
        if (_swallowUntilUp is { } held && held != key)
            _swallowUntilUp = null;
        if (!Win32Backend.ChatCaptureActive)
            return;
        // The Enter OpenAC just handed us through the claim may reach this sink
        // after OpenAC's input (handler order isn't fixed), and held it repeats:
        // it started typing, it doesn't send, until it comes back up.
        if (_ignoreEnterUntilUp && key == OverlayKey.Enter)
            return;
        // Paste/copy/cut go through Run too: off Windows they touch the overlay clipboard.
        bool ctrl = (modifiers & OverlayModifiers.Control) != 0;
        bool shift = (modifiers & OverlayModifiers.Shift) != 0;
        switch (key)
        {
            case OverlayKey.V when ctrl:
            case OverlayKey.Insert when shift && !ctrl:
                Run(Paste, "paste");
                break;
            case OverlayKey.C when ctrl:
            case OverlayKey.Insert when ctrl:
                Run(Copy, "copy");
                break;
            case OverlayKey.X when ctrl:
                Run(Cut, "cut");
                break;
            case OverlayKey.Enter:
                Win32Backend.ChatCaptureActive = false;
                _swallowUntilUp = OverlayKey.Enter;
                _sendPending = true;
                break;
            case OverlayKey.Escape:
                Win32Backend.ChatCaptureActive = false;
                _swallowUntilUp = OverlayKey.Escape;
                Run(Win32Backend.OnChatCancel, "cancel");
                break;
            case OverlayKey.Backspace: Run(Win32Backend.OnChatBackspace, "backspace"); break;
            case OverlayKey.Delete: Run(Win32Backend.OnChatDelete, "delete"); break;
            case OverlayKey.Left: Run(Win32Backend.OnChatLeft, "left"); break;
            case OverlayKey.Right: Run(Win32Backend.OnChatRight, "right"); break;
            case OverlayKey.Home: Run(Win32Backend.OnChatHome, "home"); break;
            case OverlayKey.End: Run(Win32Backend.OnChatEnd, "end"); break;
            case OverlayKey.Up: Run(Win32Backend.OnChatUp, "up"); break;
            case OverlayKey.Down: Run(Win32Backend.OnChatDown, "down"); break;
        }
    }

    public void KeyUp(OverlayKey key)
    {
        if (key == OverlayKey.Enter)
        {
            _enterDown = false;
            _ignoreEnterUntilUp = false;
        }

        if (_swallowUntilUp == key)
            _swallowUntilUp = null;
    }

    public void Character(char value)
    {
        if (Win32Backend.ChatCaptureActive)
            Type(value);
    }

    /// <summary>Runs the deferred send and Tell. Call once per tick.</summary>
    public void Tick()
    {
        // By now the claiming keypress has reached every handler: a claim made by
        // another key (a rebound chat key, '/') leaves no Enter to wait out.
        if (_ignoreEnterUntilUp && !_enterDown)
            _ignoreEnterUntilUp = false;
        if (_sendPending)
        {
            _sendPending = false;
            Run(Win32Backend.OnChatSend, "send");
        }

        if (Win32Backend.TellRequested)
        {
            Win32Backend.TellRequested = false;
            Run(Win32Backend.OnChatTellSelected, "tell");
        }
    }

    /// <summary>
    /// OpenAC's chat-entry key, offered first (IClientChat.ClaimChatEntry):
    /// taken only under their rule (ChatHooks.RynthChatOwnsChat), as their
    /// Win32Backend takes Enter.
    /// </summary>
    public bool TryClaimEntry()
    {
        if (Win32Backend.ChatCaptureActive || !ChatHooks.RynthChatOwnsChat || Win32Backend.OnChatCaptureActivated is null)
            return false;
        Win32Backend.ChatCaptureActive = true;
        _ignoreEnterUntilUp = true;
        Run(Win32Backend.OnChatCaptureActivated, "activate");
        return true;
    }

    private void Type(char value)
    {
        if (value < 0x20 || value == (char)0x7F)
            return;
        Action<char>? hook = Win32Backend.OnChatChar;
        if (hook is null)
            return;
        try { hook(value); }
        catch (Exception error) { Fail("char", error); }
    }

    private void Paste()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatPaste, "paste");
            return;
        }

        // Their OnChatPaste's rules (ChatInputPaste.Insert): one line, breaks and tabs
        // made spaces, cut to the room left under MaxLineChars without splitting a pair.
        string add = ChatInputPaste.Clean(_getClipboard());
        int room = Math.Max(0, ChatInputPaste.MaxLineChars - _inputText().Length);
        if (add.Length > room)
        {
            add = add[..room];
            if (add.Length > 0 && char.IsHighSurrogate(add[^1]))
                add = add[..^1];
        }

        foreach (char c in add)
            Type(c);
    }

    private void Copy()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatCopy, "copy");
            return;
        }

        string text = _inputText();
        if (text.Length > 0)
            _setClipboard(text);
    }

    private void Cut()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatCut, "cut");
            return;
        }

        string text = _inputText();
        if (text.Length == 0)
            return;
        _setClipboard(text);
        Run(Win32Backend.OnChatCancel, "cut");
    }

    private void Run(Action? hook, string what)
    {
        if (hook is null)
            return;
        try { hook(); }
        catch (Exception error) { Fail(what, error); }
    }

    private void Fail(string what, Exception error) =>
        _log($"[Rynth] chat keyboard {what}: {error.GetType().Name}: {error.Message}");
}
