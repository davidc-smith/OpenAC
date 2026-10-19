// RynthCore's CodeEditor draws ImGuiColorTextEdit, compiled into their own
// cimgui.dll. OpenAC's cimgui has no such editor, so this stand-in of the same
// name and members is ImGui's multi-line text box: no syntax colours, but typing,
// pasting (any size: the buffer grows), undo and Tab work, so the Lua and Meta
// source editors can be used.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend;

internal enum CodeLanguage { None = 0, Meta = 1, Lua = 2 }

internal sealed unsafe class CodeEditor : IDisposable
{
    private const int StartCapacity = 4096;

    // One callback for every editor; the instance rides in UserData.
    private static readonly ImGuiInputTextCallback Callback = OnCallback;

    private readonly string _id;
    private readonly byte[] _label;
    private IntPtr _buffer;
    private int _capacity;
    private int _length;
    private string? _text = string.Empty;
    private uint _changes, _cleanCount;
    private int _cursorByte;
    private int _cursorLine, _cursorColumn;
    private string _linePrefix = string.Empty;
    private (int Bytes, string Text)? _pendingReplace;
    private bool _focusRequested;
    private Vector2 _cursorScreenPos;

    /// <summary>Always: the editor is ImGui's own text box.</summary>
    public static bool Available => true;

    /// <summary>Their editor colours Meta keywords; the plain text box has no colours.</summary>
    public static void SetMetaVocabulary(string[] structWords, string[] conditionWords, string[] actionWords)
    {
    }

    /// <summary>Their palette slots, kept so their calls build; unused here.</summary>
    public static uint[] RynthPalette() => Array.Empty<uint>();

    public CodeEditor(string id, CodeLanguage language, uint[] palette)
    {
        _id = id;
        _label = Encoding.UTF8.GetBytes("##" + id + "\0");
    }

    public bool Created => _buffer != IntPtr.Zero;

    /// <summary>Replaces the text; the editor is clean afterwards.</summary>
    public void SetText(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        EnsureCapacity(bytes.Length + 1);
        Marshal.Copy(bytes, 0, _buffer, bytes.Length);
        ((byte*)_buffer)[bytes.Length] = 0;
        _length = bytes.Length;
        _text = text ?? string.Empty;
        _changes++;
        _cleanCount = _changes;
    }

    public string GetText() =>
        _text ??= _buffer == IntPtr.Zero ? string.Empty : Encoding.UTF8.GetString((byte*)_buffer, _length);

    /// <summary>Counts text changes (edits and SetText).</summary>
    public uint ChangeCount => _changes;

    /// <summary>Edited since the last SetText or MarkClean.</summary>
    public bool IsDirty => _changes != _cleanCount;

    public void MarkClean() => _cleanCount = _changes;

    /// <summary>Draws the editor at <paramref name="size"/>; returns whether it has keyboard focus.</summary>
    public bool Render(Vector2 size)
    {
        EnsureCapacity(StartCapacity);
        if (_focusRequested)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRequested = false;
        }

        GCHandle self = GCHandle.Alloc(this);
        bool changed;
        try
        {
            fixed (byte* label = _label)
            {
                changed = ImGuiNative.igInputTextMultiline(
                    label,
                    (byte*)_buffer,
                    (uint)_capacity,
                    size,
                    ImGuiInputTextFlags.AllowTabInput | ImGuiInputTextFlags.CallbackResize | ImGuiInputTextFlags.CallbackAlways,
                    Callback,
                    (void*)GCHandle.ToIntPtr(self)) != 0;
            }
        }
        finally { self.Free(); }

        bool active = ImGui.IsItemActive();
        if (active)
            ImGuiTextFocus.NoteActive(_id);
        if (changed)
            Changed();
        // The text box scrolls itself; the cursor cell is placed as if unscrolled, kept inside the box.
        Vector2 min = ImGui.GetItemRectMin(), max = ImGui.GetItemRectMax();
        float lineHeight = ImGui.GetTextLineHeight();
        Vector2 pad = ImGui.GetStyle().FramePadding;
        float x = min.X + pad.X + ImGui.CalcTextSize(_linePrefix).X;
        float y = min.Y + pad.Y + (_cursorLine + 1) * lineHeight;
        _cursorScreenPos = new Vector2(Math.Clamp(x, min.X, max.X), Math.Clamp(y, min.Y, max.Y));
        return active;
    }

    /// <summary>Zero-based line and column of the cursor (as of the last frame it was active).</summary>
    public (int Line, int Column) Cursor => (_cursorLine, _cursorColumn);

    public void SetCursor(int line, int charIndex)
    {
    }

    /// <summary>Screen position of the bottom-left corner of the cursor's cell (after Render).</summary>
    public Vector2 CursorScreenPos => _cursorScreenPos;

    /// <summary>The cursor's line up to the cursor.</summary>
    public string LinePrefix() => _linePrefix;

    /// <summary>Replaces the <paramref name="bytes"/> UTF-8 bytes before the cursor with <paramref name="text"/> (on the next frame).</summary>
    public void ReplaceBeforeCursor(int bytes, string text) => _pendingReplace = (bytes, text);

    /// <summary>Their editor leaves list keys to a completion list; the text box keeps them.</summary>
    public bool CompletionKeysHeld { get; set; }

    public void RequestFocus() => _focusRequested = true;

    public void Dispose()
    {
        _text = GetText();
        if (_buffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        _capacity = 0;
        if (_text.Length > 0)
            SetText(_text);
    }

    private void Changed()
    {
        _length = Strlen((byte*)_buffer, _capacity);
        _text = null;
        _changes++;
    }

    private void EnsureCapacity(int needed)
    {
        if (_capacity >= needed && _buffer != IntPtr.Zero)
            return;
        int capacity = Math.Max(needed, Math.Max(StartCapacity, _capacity * 2));
        IntPtr grown = Marshal.AllocHGlobal(capacity);
        if (_buffer != IntPtr.Zero)
        {
            Buffer.MemoryCopy((void*)_buffer, (void*)grown, capacity, _capacity);
            Marshal.FreeHGlobal(_buffer);
        }
        else
        {
            ((byte*)grown)[0] = 0;
            _length = 0;
        }

        _buffer = grown;
        _capacity = capacity;
    }

    private static int OnCallback(ImGuiInputTextCallbackData* raw)
    {
        var data = new ImGuiInputTextCallbackDataPtr(raw);
        if (GCHandle.FromIntPtr(data.UserData).Target is not CodeEditor editor)
            return 0;

        if (data.EventFlag == ImGuiInputTextFlags.CallbackResize)
        {
            editor.EnsureCapacity(data.BufSize);
            data.Buf = editor._buffer;
            return 0;
        }

        if (editor._pendingReplace is { } replace)
        {
            editor._pendingReplace = null;
            int from = Math.Max(0, data.CursorPos - replace.Bytes);
            data.DeleteChars(from, data.CursorPos - from);
            data.InsertChars(data.CursorPos, replace.Text);
            editor.Changed();
        }

        editor._cursorByte = data.CursorPos;
        byte* text = (byte*)data.Buf;
        int lineStart = editor._cursorByte;
        while (lineStart > 0 && text[lineStart - 1] != (byte)'\n')
            lineStart--;
        int line = 0;
        for (int i = 0; i < lineStart; i++)
            if (text[i] == (byte)'\n') line++;
        editor._cursorLine = line;
        editor._linePrefix = Encoding.UTF8.GetString(text + lineStart, editor._cursorByte - lineStart);
        editor._cursorColumn = editor._linePrefix.Length;
        return 0;
    }

    private static int Strlen(byte* text, int max)
    {
        int n = 0;
        while (n < max && text[n] != 0) n++;
        return n;
    }
}
