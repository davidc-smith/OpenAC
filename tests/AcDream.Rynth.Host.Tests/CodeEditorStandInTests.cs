using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class CodeEditorStandInTests
{
    private static Action Draw(CodeEditor editor, Action<bool>? focused = null) => () =>
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(600, 400));
        ImGui.Begin("editor-host");
        bool f = editor.Render(new Vector2(560, 300));
        ImGui.End();
        focused?.Invoke(f);
    };

    /// <summary>Clicks into the box (a frame first, so the new window shows) and returns whether it has the keyboard.</summary>
    private static bool Focus(HeadlessImGui imgui, CodeEditor editor)
    {
        bool focused = false;
        imgui.Frame(Draw(editor));
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMousePosEvent(100, 100);
        io.AddMouseButtonEvent(0, true);
        imgui.Frame(Draw(editor, f => focused = f));
        io.AddMouseButtonEvent(0, false);
        imgui.Frame(Draw(editor, f => focused = f));
        return focused;
    }

    [Fact]
    public void SetTextIsCleanAndReadsBack()
    {
        using var imgui = new HeadlessImGui();
        using var editor = new CodeEditor("t", CodeLanguage.Lua, CodeEditor.RynthPalette());
        editor.SetText("print('hi')");
        imgui.Frame(Draw(editor));

        Assert.Equal("print('hi')", editor.GetText());
        Assert.False(editor.IsDirty);
        Assert.True(CodeEditor.Available);
    }

    [Fact]
    public void APasteFarLargerThanTheStartingBufferLandsWhole()
    {
        using var imgui = new HeadlessImGui();
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigMacOSXBehaviors = false;
        using var editor = new CodeEditor("paste", CodeLanguage.Lua, CodeEditor.RynthPalette());
        string script = string.Concat(Enumerable.Range(0, 800).Select(i => $"print('line {i}')\n"));
        Assert.True(script.Length > 12_000);
        ImGui.SetClipboardText(script);

        editor.RequestFocus();
        Assert.True(Focus(imgui, editor));
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.LeftCtrl, true);
        io.AddKeyEvent(ImGuiKey.V, true);
        imgui.Frame(Draw(editor));
        io.AddKeyEvent(ImGuiKey.V, false);
        io.AddKeyEvent(ImGuiKey.LeftCtrl, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        imgui.Frame(Draw(editor));

        Assert.Equal(script, editor.GetText());
        Assert.True(editor.IsDirty);
        editor.MarkClean();
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void TypingUpdatesTheCursorLineAndPrefix()
    {
        using var imgui = new HeadlessImGui();
        ImGuiIOPtr io = ImGui.GetIO();
        using var editor = new CodeEditor("type", CodeLanguage.Lua, CodeEditor.RynthPalette());
        Assert.True(Focus(imgui, editor));
        foreach (char c in "a = 1\ngame.Ch")
        {
            if (c == '\n')
            {
                io.AddKeyEvent(ImGuiKey.Enter, true);
                imgui.Frame(Draw(editor));
                io.AddKeyEvent(ImGuiKey.Enter, false);
            }
            else
            {
                io.AddInputCharacter(c);
            }

            imgui.Frame(Draw(editor));
        }

        imgui.Frame(Draw(editor));
        Assert.Equal("a = 1\ngame.Ch", editor.GetText());
        Assert.Equal((1, 7), editor.Cursor);
        Assert.Equal("game.Ch", editor.LinePrefix());

        editor.ReplaceBeforeCursor(2, "Character");
        imgui.Frame(Draw(editor));
        Assert.Equal("a = 1\ngame.Character", editor.GetText());
    }
}
