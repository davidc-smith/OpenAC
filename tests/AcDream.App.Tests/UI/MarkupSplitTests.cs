using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupSplitTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"split-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private sealed class Binding
    {
        public float At { get; set; } = 120f;
        public List<float> Changes { get; } = new();
        public Action<float> SetAt => v => { At = v; Changes.Add(v); };
        public Action<float> Notify => v => Changes.Add(v);
        public int NotFloat { get; set; }
        public Action<int> WrongAction => _ => { };
        public int LeftClicks { get; private set; }
        public int RightClicks { get; private set; }
        public Action LeftClick => () => LeftClicks++;
        public Action RightClick => () => RightClicks++;
    }

    [Fact]
    public void FloatReader_ReadsTheProperty()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredFloatReader("{At}", b, "split position");
        Assert.Equal(120f, read());
        b.At = 7.5f;
        Assert.Equal(7.5f, read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("{Missing}")]
    [InlineData("{NotFloat}")]
    public void FloatReader_RejectsAbsentLiteralsAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredFloatReader(expression, new Binding(), "split position"));
        Assert.Contains("split position", ex.Message);
        Assert.Contains("float", ex.Message);
    }

    [Fact]
    public void FloatAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindFloatAction(null, b, "ctx"));
        MarkupBindings.BindFloatAction("{SetAt}", b, "ctx")!(3f);
        Assert.Equal(3f, b.At);
    }

    [Theory]
    [InlineData("SetAt")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void FloatAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindFloatAction(attribute, new Binding(), "split onchange"));
        Assert.Contains("split onchange", ex.Message);
        Assert.Contains("Action<float>", ex.Message);
    }

    // The split sits at (10, 10). Horizontal 405×200: room 400, default 200, bar x 210-215 on screen.
    private static string Markup(string attributes, string size = "w=\"405\" h=\"200\"") => $"""
        <panel x="0" y="0" w="500" h="300">
          <split name="main" x="10" y="10" {size} {attributes}>
            <group><button name="l" x="4" y="4" w="60" h="20" text="L" onclick="{"{LeftClick}"}"/></group>
            <group><button name="r" x="4" y="4" w="60" h="20" text="R" onclick="{"{RightClick}"}"/></group>
          </split>
        </panel>
        """;

    private static (UiRoot Root, UiNineSlicePanel Panel, UiMarkupSplit Split) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null, string size = "w=\"405\" h=\"200\"")
    {
        var panel = MarkupDocument.Build(Markup(attributes, size), binding, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel, panel.Children.OfType<UiMarkupSplit>().Single());
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Drag(UiRoot root, int fromX, int toX, int y = 100)
    {
        root.OnMouseDown(UiMouseButton.Left, fromX, y);
        root.OnMouseMove(toX, y);
        root.OnMouseUp(UiMouseButton.Left, toX, y);
    }

    private static void DragY(UiRoot root, int fromY, int toY, int x = 100)
    {
        root.OnMouseDown(UiMouseButton.Left, x, fromY);
        root.OnMouseMove(x, toY);
        root.OnMouseUp(UiMouseButton.Left, x, toY);
    }

    private static (float X, float Y, float W, float H) Rect(UiElement e) => (e.Left, e.Top, e.Width, e.Height);

    /// <summary>Records every call, so a test can prove the state was never touched.</summary>
    private sealed class RecordingState : IMarkupControlState
    {
        public int Calls { get; private set; }
        public int Generation { get { Calls++; return 1; } }
        public string? Get(string control, string kind) { Calls++; return null; }
        public void Set(string control, string kind, string value) => Calls++;
        public void Remove(string control, string kind) => Calls++;
    }

    [Fact]
    public void Split_DefaultsToHalfAndLaysOutThePanes()
    {
        var (_, _, split) = Mount("", new Binding());
        Assert.Equal((0f, 0f, 200f, 200f), Rect(split.First!));
        Assert.Equal((200f, 0f, 5f, 200f), Rect(split.Bar));
        Assert.Equal((205f, 0f, 200f, 200f), Rect(split.Second!));
    }

    [Fact]
    public void Split_VerticalStacksThePanes()
    {
        var (_, _, split) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        Assert.Equal((0f, 0f, 300f, 100f), Rect(split.First!));
        Assert.Equal((0f, 100f, 300f, 5f), Rect(split.Bar));
        Assert.Equal((0f, 105f, 300f, 100f), Rect(split.Second!));
    }

    [Fact]
    public void Split_VerticalDragMovesTheBarDown()
    {
        var (root, _, split) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        DragY(root, 10 + 100 + 2, 10 + 100 + 2 + 30);
        Assert.Equal(130f, split.First!.Height);
        Assert.Equal(135f, split.Second!.Top);
        Assert.Equal(70f, split.Second.Height);
    }

    [Fact]
    public void Split_DragMovesTheBarAndResizesThePanes()
    {
        var b = new Binding();
        var (root, _, split) = Mount("onchange=\"{Notify}\"", b);
        Drag(root, 212, 162);
        Assert.Equal(150f, split.First!.Width);
        Assert.Equal(155f, split.Second!.Left);
        Assert.Equal(250f, split.Second.Width);
        Assert.Equal(new[] { 150f }, b.Changes);
    }

    [Fact]
    public void Split_MinAndMaxClampADrag()
    {
        var (root, _, split) = Mount("min=\"80\" max=\"300\"", new Binding());
        Drag(root, 212, 0);
        Assert.Equal(80f, split.First!.Width);
        Drag(root, 10 + 80 + 2, 790);
        Assert.Equal(300f, split.First.Width);
    }

    [Fact]
    public void Split_WithoutMaxStopsAtTheRoom()
    {
        var (root, _, split) = Mount("", new Binding());
        Drag(root, 212, 790);
        Assert.Equal(400f, split.First!.Width);
        Assert.Equal(0f, split.Second!.Width);
    }

    [Fact]
    public void Split_ResizeKeepsTheFirstPaneAndClampsWhenTooSmall()
    {
        var (_, panel, split) = Mount("anchor=\"left top right\"", new Binding());
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        panel.Width = 600f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(505f, split.Width);
        Assert.Equal(200f, split.First!.Width);
        Assert.Equal(300f, split.Second!.Width);
        panel.Width = 250f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(155f, split.Width);
        Assert.Equal(150f, split.First.Width);
        Assert.Equal(0f, split.Second.Width);
        panel.Width = 500f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(200f, split.First.Width);   // the host value was never clamped, only its layout
    }

    [Fact]
    public void Split_PaneChildrenAreClickableAndTheBarTakesNoFocus()
    {
        var b = new Binding();
        var (root, _, _) = Mount("", b);
        Click(root, 10 + 4 + 5, 10 + 4 + 5);
        Click(root, 10 + 205 + 4 + 5, 10 + 4 + 5);
        Assert.Equal(1, b.LeftClicks);
        Assert.Equal(1, b.RightClicks);
        Click(root, 212, 100);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Split_BoundPositionBelongsToThePlugin()
    {
        var b = new Binding();
        var (root, _, split) = Mount("position=\"{At}\" onchange=\"{SetAt}\"", b);
        Assert.Equal(120f, split.First!.Width);
        Drag(root, 10 + 120 + 2, 10 + 170 + 2);
        Assert.Equal(new[] { 170f }, b.Changes);
        Assert.Equal(170f, split.First.Width);
        b.At = 60f;
        root.Tick(0.016, 0);
        Assert.Equal(60f, split.First.Width);
    }

    [Fact]
    public void Split_BoundWithoutOnchangeOnlyDisplays()
    {
        var b = new Binding();
        var (root, _, split) = Mount("position=\"{At}\"", b);
        Drag(root, 10 + 120 + 2, 10 + 170 + 2);
        Assert.Equal(120f, split.First!.Width);
        Assert.Equal(120f, b.At);
    }

    [Fact]
    public void Split_RemembersAtDragEndAndForgetsTheDefault()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Drag(root, 212, 162);
        Assert.Equal("150", state.Get("main", "position"));
        Drag(root, 10 + 150 + 2, 212);
        Assert.Equal(200f, split.First!.Width);
        Assert.Null(state.Get("main", "position"));
    }

    [Fact]
    public void Split_StoredPositionAppliesAtBuildAndMalformedIsIgnored()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        state.Set("main", "position", "150");
        var (_, _, remembered) = Mount("", new Binding(), state);
        Assert.Equal(150f, remembered.First!.Width);
        state.Set("main", "position", "abc");
        var (_, _, malformed) = Mount("", new Binding(), state);
        Assert.Equal(200f, malformed.First!.Width);
    }

    [Fact]
    public void Split_ACharacterSwitchReReads()
    {
        string character = "alice";
        // A real settings file: switching characters writes alice's choice and reads it back.
        var store = new MarkupControlStateStore(new SettingsStore(_settingsPath), () => character);
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Drag(root, 212, 162);
        character = "bob";
        root.Tick(0.016, 0);
        Assert.Equal(200f, split.First!.Width);
        character = "alice";
        root.Tick(0.016, 0);
        Assert.Equal(150f, split.First.Width);
    }

    [Fact]
    public void Split_UnnamedOrBoundIsNeverRemembered()
    {
        var unnamedState = new RecordingState();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="500" h="300">
              <split x="10" y="10" w="405" h="200"><group/><group/></split>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), controlState: unnamedState);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        Drag(root, 212, 162);
        root.Tick(0.016, 0);
        Assert.Equal(0, unnamedState.Calls);

        var boundState = new RecordingState();
        var (boundRoot, _, _) = Mount("position=\"{At}\" onchange=\"{SetAt}\"", new Binding(), boundState);
        Drag(boundRoot, 10 + 120 + 2, 10 + 170 + 2);
        boundRoot.Tick(0.016, 0);
        Assert.Equal(0, boundState.Calls);
    }

    [Fact]
    public void Split_ArrowKeysMoveTheFocusedBarAndAreRemembered()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Assert.True(split.Bar.TabStop);
        root.SetKeyboardFocus(split.Bar);
        root.OnKeyDown((int)Key.Right);
        Assert.Equal(208f, split.First!.Width);
        Assert.Equal("208", state.Get("main", "position"));
        root.OnKeyDown((int)Key.Left);
        Assert.Equal(200f, split.First.Width);
        Assert.Null(state.Get("main", "position"));
        Assert.False(split.Bar.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Up }));
        Assert.Equal(200f, split.First.Width);
    }

    [Fact]
    public void Split_DisabledIgnoresDragsAndKeysAndShowsNoResizeCursor()
    {
        var (root, _, split) = Mount("enabled=\"false\"", new Binding());
        Drag(root, 212, 162);
        split.Bar.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        split.Bar.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Right });
        Assert.Equal(200f, split.First!.Width);
        root.Tick(0.016, 0);
        Assert.False(split.Bar.ActiveCursor().IsValid);
    }

    [Fact]
    public void Split_BarShowsTheResizeCursorForItsAxis()
    {
        var (_, _, horizontal) = Mount("", new Binding());
        Assert.Equal(0x06006128u, horizontal.Bar.ActiveCursor().File);
        var (_, _, vertical) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        Assert.Equal(0x06005E66u, vertical.Bar.ActiveCursor().File);
    }

    [Fact]
    public void Split_TooltipShowsOnTheBar()
    {
        var (_, _, split) = Mount("tooltip=\"Drag to resize\"", new Binding());
        Assert.Equal("Drag to resize", split.Bar.GetTooltipText());
    }

    [Theory]
    [InlineData("<split w=\"100\" h=\"50\"><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\"><group/><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\"><group/><label text=\"x\"/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" orientation=\"diagonal\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" min=\"-1\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" max=\"x\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" at=\"wide\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" min=\"100\" max=\"50\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" position=\"{NotFloat}\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" position=\"12\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" onchange=\"{WrongAction}\"><group/><group/></split>")]
    public void Split_BadMarkupThrowsAtBuild(string split)
    {
        Assert.Throws<FormatException>(() => MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\">{split}</panel>", new Binding(), _ => (1u, 16, 16)));
    }
}
