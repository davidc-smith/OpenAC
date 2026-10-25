using System;
using System.IO;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupExpanderTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"expander-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private sealed class Binding
    {
        public bool AdvOpen { get; set; } = true;
        public int Changes { get; private set; }
        public bool? LastChange { get; private set; }
        public Action<bool> SetAdvOpen => v => { AdvOpen = v; LastChange = v; Changes++; };
        public Action<bool> Notify => v => { LastChange = v; Changes++; };
        public int InnerClicks { get; private set; }
        public Action Inner => () => InnerClicks++;
        public int NotBool { get; set; }
        public Action<int> WrongAction => _ => { };
    }

    // Expander at (8, 8), 200×100; its header is y 8-26, content from y 26.
    // "Below" (y 120) and "Low" (y 150) sit under its bottom (108); "Beside" (y 40, x 220) beside it.
    private static string Markup(string attributes) => $"""
        <panel x="0" y="0" w="400" h="300">
          <expander name="adv" x="8" y="8" w="200" h="100" text="Advanced" {attributes}>
            <button x="4" y="4" w="80" h="20" text="Inner" onclick="{"{Inner}"}"/>
          </expander>
          <label name="below" x="8" y="120" text="Below"/>
          <label name="beside" x="220" y="40" text="Beside"/>
          <label name="low" x="8" y="150" text="Low"/>
        </panel>
        """;

    private static (UiRoot Root, UiNineSlicePanel Panel, UiMarkupExpander Expander) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null)
    {
        var panel = MarkupDocument.Build(Markup(attributes), binding, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel, panel.Children.OfType<UiMarkupExpander>().Single());
    }

    private static UiElement Named(UiElement parent, string name) => parent.Children.Single(c => c.Name == name);

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void ClickHeader(UiRoot root) => Click(root, 30, 14);

    [Fact]
    public void Expander_ChildrenSitBelowTheHeaderAndAreClickable()
    {
        var b = new Binding();
        var (root, _, expander) = Mount("", b);
        Assert.Equal(100f, expander.Height);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Content.Top);
        Click(root, 8 + 4 + 5, 26 + 4 + 5);
        Assert.Equal(1, b.InnerClicks);
    }

    [Fact]
    public void Expander_CollapsingMovesWhatIsBelowAndKeepsWhatIsBeside()
    {
        var b = new Binding();
        var (root, panel, expander) = Mount("", b);
        ClickHeader(root);
        Assert.False(expander.Expanded);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);
        Assert.False(expander.Content.Visible);
        Assert.Equal(120f - 82f, Named(panel, "below").Top);
        Assert.Equal(150f - 82f, Named(panel, "low").Top);
        Assert.Equal(40f, Named(panel, "beside").Top);

        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(38f, Named(panel, "below").Top);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);

        Click(root, 8 + 4 + 5, 26 + 4 + 5);
        Assert.Equal(0, b.InnerClicks);

        ClickHeader(root);
        Assert.True(expander.Expanded);
        Assert.Equal(100f, expander.Height);
        Assert.Equal(120f, Named(panel, "below").Top);
        Assert.Equal(150f, Named(panel, "low").Top);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(120f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_StackedExpandersCompose()
    {
        var b = new Binding();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="400">
              <expander name="one" x="8" y="8" w="200" h="100" text="One"/>
              <expander name="two" x="8" y="120" w="200" h="60" text="Two"/>
              <label name="end" x="8" y="200" text="End"/>
            </panel>
            """, b, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var one = (UiMarkupExpander)Named(panel, "one");
        var two = (UiMarkupExpander)Named(panel, "two");
        var end = Named(panel, "end");

        Click(root, 30, 14);                 // collapse one (-82)
        Assert.Equal(38f, two.Top);
        Assert.Equal(118f, end.Top);
        Click(root, 30, 38 + 6);              // collapse two (-42)
        Assert.Equal(76f, end.Top);
        Click(root, 30, 14);                 // expand one (+82)
        Assert.Equal(120f, two.Top);
        Assert.Equal(158f, end.Top);
        Click(root, 30, 120 + 6);             // expand two (+42)
        Assert.Equal(200f, end.Top);
    }

    [Fact]
    public void Expander_OpenFalseStartsCollapsedAfterATick()
    {
        var (root, panel, expander) = Mount("open=\"false\"", new Binding());
        root.Tick(0.016, 0);
        Assert.False(expander.Expanded);
        Assert.Equal(38f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_UnboundNotifiesOnchange()
    {
        var b = new Binding();
        var (root, _, expander) = Mount("onchange=\"{Notify}\"", b);
        ClickHeader(root);
        Assert.False(expander.Expanded);
        Assert.Equal(false, b.LastChange);
        Assert.Equal(1, b.Changes);
    }

    [Fact]
    public void Expander_BoundStateBelongsToThePlugin()
    {
        var b = new Binding { AdvOpen = true };
        var (root, panel, expander) = Mount("expanded=\"{AdvOpen}\" onchange=\"{SetAdvOpen}\"", b);
        ClickHeader(root);
        Assert.Equal(false, b.LastChange);
        root.Tick(0.016, 0);
        Assert.False(expander.Expanded);
        Assert.Equal(38f, Named(panel, "below").Top);

        b.AdvOpen = true;
        root.Tick(0.016, 0);
        Assert.True(expander.Expanded);
        Assert.Equal(120f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_BoundWithoutOnchangeOnlyDisplays()
    {
        var b = new Binding { AdvOpen = true };
        var (root, _, expander) = Mount("expanded=\"{AdvOpen}\"", b);
        ClickHeader(root);
        root.Tick(0.016, 0);
        Assert.True(expander.Expanded);
    }

    [Fact]
    public void Expander_RemembersPerCharacterAndForgetsTheDefault()
    {
        string character = "alice";
        // A real settings file: switching characters writes alice's choice and reads it back.
        var store = new MarkupControlStateStore(new SettingsStore(_settingsPath), () => character);
        var state = store.ForWindow("win");
        var (root, _, _) = Mount("", new Binding(), state);
        root.Tick(0.016, 0);
        ClickHeader(root);
        Assert.Equal("false", state.Get("adv", "expanded"));
        ClickHeader(root);
        Assert.Null(state.Get("adv", "expanded"));
        ClickHeader(root);
        Assert.Equal("false", state.Get("adv", "expanded"));

        var (root2, panel2, expander2) = Mount("", new Binding(), state);
        root2.Tick(0.016, 0);
        Assert.False(expander2.Expanded);
        Assert.Equal(38f, Named(panel2, "below").Top);

        character = "bob";
        root2.Tick(0.016, 0);
        Assert.True(expander2.Expanded);
        Assert.Equal(120f, Named(panel2, "below").Top);
        character = "alice";
        root2.Tick(0.016, 0);
        Assert.False(expander2.Expanded);
    }

    [Fact]
    public void Expander_UnnamedOrBoundIsNeverRemembered()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var b = new Binding();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="Unnamed"/>
              <expander name="bound" x="8" y="150" w="200" h="100" text="Bound" expanded="{AdvOpen}" onchange="{SetAdvOpen}"/>
            </panel>
            """, b, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        Click(root, 30, 150 + 6);   // the bound one first: collapsing the unnamed one would move it up
        Click(root, 30, 14);
        Assert.Null(state.Get("bound", "expanded"));
        Assert.False(((UiMarkupExpander)Named(panel, "bound")).IsRemembered);
        store.Save();
        Assert.All(panel.Children.OfType<UiMarkupExpander>(), e => Assert.False(e.IsRemembered));
    }

    [Fact]
    public void Expander_KeyboardTogglesAndClicksDoNotTakeFocus()
    {
        var (root, _, expander) = Mount("", new Binding());
        ClickHeader(root);
        Assert.Null(root.KeyboardFocus);
        Assert.True(expander.Header.TabStop);
        root.SetKeyboardFocus(expander.Header);
        root.OnKeyDown((int)Key.Space);
        Assert.True(expander.Expanded);
        root.OnKeyDown((int)Key.Enter);
        Assert.False(expander.Expanded);
    }

    [Fact]
    public void Expander_DisabledIgnoresClicksAndKeys()
    {
        var (root, _, expander) = Mount("enabled=\"false\"", new Binding());
        ClickHeader(root);
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Space });
        Assert.True(expander.Expanded);
    }

    [Fact]
    public void Expander_TwoColumnsDoNotDragEachOthersNeighbours()
    {
        var b = new Binding();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="400">
              <expander name="one" x="8" y="8" w="200" h="100" text="One"/>
              <expander name="two" x="8" y="120" w="200" h="60" text="Two"/>
              <label name="side" x="220" y="100" text="Side"/>
            </panel>
            """, b, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var one = (UiMarkupExpander)Named(panel, "one");
        var two = (UiMarkupExpander)Named(panel, "two");
        var side = Named(panel, "side");

        Click(root, 30, 14);                 // collapse one: side (authored 100) is above one's bottom 108
        Assert.Equal(100f, side.Top);
        Click(root, 30, 38 + 6);             // collapse two
        Assert.Equal(100f, side.Top);
        Click(root, 30, 38 + 6);             // expand two first
        Click(root, 30, 14);                 // then one
        Assert.Equal(100f, side.Top);
        Assert.Equal(120f, two.Top);
    }

    [Fact]
    public void Expander_StretchedSiblingsGrowInsteadOfMoving()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" text="Adv"/>
              <group name="fill" x="8" y="120" w="200" h="170" anchor="left top bottom"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var fill = Named(panel, "fill");
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        ClickHeader(root);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(38f, fill.Top);
        Assert.Equal(252f, fill.Height);
        Assert.Equal(10f, 300f - (fill.Top + fill.Height));
        ClickHeader(root);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(120f, fill.Top);
        Assert.Equal(170f, fill.Height);
    }

    // "adv" header y 8-26; "fill" is an expander from y 120 stretched to 10 px above the bottom.
    private static (UiRoot Root, UiMarkupExpander Fill) MountStretchedPair()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" text="Adv"/>
              <expander name="fill" x="8" y="120" w="200" h="170" anchor="left top bottom" text="Fill"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        return (root, (UiMarkupExpander)Named(panel, "fill"));
    }

    private static void ClickAndDraw(UiRoot root, int y)
    {
        Click(root, 30, y);
        MarkupDrawProbe.Draw(root.Children[0], 800f, 600f);
    }

    [Fact]
    public void Expander_CollapsedStretchedSiblingKeepsItsHeaderWhenTheOneAboveExpands()
    {
        var (root, fill) = MountStretchedPair();
        ClickAndDraw(root, 14);           // collapse adv: fill grows to 38..290
        ClickAndDraw(root, 38 + 6);       // collapse fill
        ClickAndDraw(root, 14);           // expand adv
        Assert.Equal(120f, fill.Top);
        Assert.Equal(UiMarkupExpander.HeaderHeight, fill.Height);
        ClickAndDraw(root, 120 + 6);      // expand fill
        Assert.Equal(120f, fill.Top);
        Assert.Equal(170f, fill.Height);
    }

    [Fact]
    public void Expander_CollapsedStretchedSiblingGrowsWhenTheOneAboveCollapses()
    {
        var (root, fill) = MountStretchedPair();
        ClickAndDraw(root, 120 + 6);      // collapse fill
        ClickAndDraw(root, 14);           // collapse adv: fill moves up, stays a header
        Assert.Equal(38f, fill.Top);
        Assert.Equal(UiMarkupExpander.HeaderHeight, fill.Height);
        ClickAndDraw(root, 38 + 6);       // expand fill: reaches 10 px above the bottom
        Assert.Equal(38f, fill.Top);
        Assert.Equal(252f, fill.Height);
        ClickAndDraw(root, 14);           // expand adv
        Assert.Equal(120f, fill.Top);
        Assert.Equal(170f, fill.Height);
    }

    [Fact]
    public void Expander_SiblingMovedByACollapsedStretchedOneComesBackToItsAuthoredPlace()
    {
        // "fill" moves the button up by 132 (its own content). Collapsing "adv"
        // then grows what fill restores by 82, but fill must still bring the
        // button back by only the 132 it moved it.
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" text="Adv"/>
              <expander name="fill" x="8" y="120" w="200" h="150" anchor="left top bottom" text="Fill"/>
              <button name="ok" x="8" y="275" w="80" h="20" anchor="left bottom" text="OK"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        var fill = Named(panel, "fill");
        var ok = Named(panel, "ok");
        ClickAndDraw(root, 120 + 6);      // collapse fill: ok moves up 132 to 143
        ClickAndDraw(root, 14);           // collapse adv: fill to 38, ok to 61
        ClickAndDraw(root, 38 + 6);       // expand fill: ok back 132 to 193
        Assert.Equal(193f, ok.Top);
        ClickAndDraw(root, 14);           // expand adv
        Assert.Equal(120f, fill.Top);
        Assert.Equal(150f, fill.Height);
        Assert.Equal(275f, ok.Top);
    }

    [Fact]
    public void Expander_ResizeBeforeDrawIsNotAbsorbed()
    {
        var markup = """
            <panel x="0" y="0" w="300" h="300">
              <expander name="adv" x="8" y="8" w="100" h="100" text="Adv"/>
              <button name="right" x="200" y="150" w="80" h="20" anchor="right top" text="R"/>
            </panel>
            """;
        var panel = MarkupDocument.Build(markup, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        panel.Width = 400f;
        Click(root, 30, 14);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        var right = Named(panel, "right");
        Assert.Equal(300f, right.Left);
        Assert.Equal(150f - 82f, right.Top);
        Click(root, 30, 14);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(300f, right.Left);
        Assert.Equal(150f, right.Top);
    }

    [Fact]
    public void Expander_InsideAGroupStillRemembers()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <group x="0" y="0" w="400" h="300">
                <expander name="inner" x="8" y="8" w="200" h="100" text="Inner"/>
              </group>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        Click(root, 30, 14);
        Assert.Equal("false", state.Get("inner", "expanded"));
    }

    [Fact]
    public void Expander_StoredTrueOverridesOpenFalseAndDefaultForgets()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        state.Set("adv", "expanded", "true");
        var (root, _, expander) = Mount("open=\"false\"", new Binding(), state);
        root.Tick(0.016, 0);
        Assert.True(expander.Expanded);
        ClickHeader(root);
        Assert.False(expander.Expanded);
        Assert.Null(state.Get("adv", "expanded"));
    }

    private static (float X, float Y, float W, float H) FrameBounds(UiElement panel)
    {
        var quads = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(panel, 800f, 600f), UiMarkupExpander.ClassicFrame);
        Assert.NotEmpty(quads);
        float minX = quads.Min(q => q.X), minY = quads.Min(q => q.Y);
        return (minX, minY, quads.Max(q => q.X + q.W) - minX, quads.Max(q => q.Y + q.H) - minY);
    }

    [Fact]
    public void Expander_IsFramedAroundHeaderAndContent()
    {
        var (_, panel, _) = Mount("", new Binding());
        Assert.Equal((8f, 8f, 200f, 100f), FrameBounds(panel));
        // The header band, and a divider between it and the content.
        var band = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(panel, 800f, 600f), UiMarkupExpander.ClassicBand);
        Assert.Contains((8f, 8f, 200f, UiMarkupExpander.HeaderHeight), band);
        var frame = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(panel, 800f, 600f), UiMarkupExpander.ClassicFrame);
        Assert.Contains(frame, q => q.Y == 8f + UiMarkupExpander.HeaderHeight - 1f && q.H == 1f && q.W > 100f);
    }

    [Fact]
    public void Expander_FoldedFrameIsJustTheHeader()
    {
        var (root, panel, _) = Mount("", new Binding());
        ClickHeader(root);
        Assert.Equal((8f, 8f, 200f, UiMarkupExpander.HeaderHeight), FrameBounds(panel));
    }

    [Fact]
    public void Expander_BorderFalseDrawsNoFrame()
    {
        var (_, panel, expander) = Mount("border=\"false\"", new Binding());
        Assert.False(expander.Framed);
        var renderer = MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpander.ClassicFrame));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpander.ClassicBand));
    }

    [Fact]
    public void Expander_BorderOtherThanTrueOrFalseFailsTheBuild()
    {
        var ex = Assert.Throws<FormatException>(() => Mount("border=\"yes\"", new Binding()));
        Assert.Contains("border", ex.Message);
    }

    [Fact]
    public void Expander_ArrowFollowsTheLaidOutState()
    {
        var (root, _, expander) = Mount("open=\"false\"", new Binding());
        var before = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, before.Count);
        Assert.All(before, q => Assert.Equal(1f, q.H));   // rows: expanded arrow, matching the layout
        root.Tick(0.016, 0);
        var after = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, after.Count);
        Assert.All(after, q => Assert.Equal(1f, q.W));    // columns: collapsed arrow
    }

    [Fact]
    public void Expander_TopAndBottomAnchoredRestoresAfterTheParentShrank()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="300" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" anchor="left top bottom" text="Adv"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var expander = (UiMarkupExpander)Named(panel, "adv");
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        ClickHeader(root);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);
        panel.Height = 250f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        ClickHeader(root);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(250f - 8f - 192f, expander.Height);
        panel.Height = 300f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(100f, expander.Height);
    }

    [Fact]
    public void Expander_BottomOnlyAnchorKeepsFollowingTheBottom()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="300" h="300">
              <expander name="adv" x="8" y="192" w="200" h="100" anchor="left bottom" text="Adv"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var expander = (UiMarkupExpander)Named(panel, "adv");
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Click(root, 30, 192 + 6);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        panel.Height = 250f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(142f, expander.Top);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);
        Click(root, 30, 142 + 6);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(142f, expander.Top);
        Assert.Equal(100f, expander.Height);
    }

    [Theory]
    [InlineData("h=\"10\"")]
    [InlineData("expanded=\"{NotBool}\"")]
    [InlineData("onchange=\"{WrongAction}\"")]
    [InlineData("open=\"maybe\"")]
    public void Expander_BadAttributesThrowAtBuild(string attributes)
    {
        var markup = attributes.StartsWith("h=")
            ? "<panel x=\"0\" y=\"0\" w=\"300\" h=\"300\"><expander x=\"8\" y=\"8\" w=\"200\" " + attributes + " text=\"A\"/></panel>"
            : Markup(attributes);
        Assert.Throws<FormatException>(() => MarkupDocument.Build(markup, new Binding(), _ => (1u, 16, 16)));
    }

    [Fact]
    public void Expander_ArrowPointsDownWhenOpenAndRightWhenClosed()
    {
        var (root, _, expander) = Mount("", new Binding());
        var open = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, open.Count);
        Assert.All(open, q => Assert.Equal(1f, q.H, 3));
        ClickHeader(root);
        var closed = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, closed.Count);
        Assert.All(closed, q => Assert.Equal(1f, q.W, 3));
    }

    [Fact]
    public void Expander_DisabledDrawsFaded()
    {
        var (_, _, expander) = Mount("enabled=\"false\"", new Binding());
        var renderer = MarkupDrawProbe.Draw(expander.Header);
        var faded = UiMarkupExpanderHeader.ClassicArrow with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(3, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpanderHeader.ClassicArrow));
    }

    [Fact]
    public void Expander_FocusDrawsAnOutline()
    {
        var (_, _, expander) = Mount("", new Binding());
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        var renderer = MarkupDrawProbe.Draw(expander.Header);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpanderHeader.FocusColor));
    }

    [Fact]
    public void Expander_PluginThemeReachesTheHeaderAndTheChildren()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="400" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" text="Advanced">
                <toggle x="4" y="4" w="120" h="16" text="Inner" checked="{AdvOpen}"/>
              </expander>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), themes: themes);
        var expander = panel.Children.OfType<UiMarkupExpander>().Single();
        Assert.Same(PluginUiPalette.Moss, expander.Header.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Text, expander.Header.TextColor);
        var toggle = Assert.IsType<UiMarkupToggle>(expander.Content.Children[0]);
        Assert.Same(PluginUiPalette.Moss, toggle.ThemePalette);
    }

    [Fact]
    public void Expander_TooltipShowsOnTheHeader()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="A" tooltip="Hi"/>
              <expander name="plain" x="8" y="120" w="200" h="40" text="B"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16));
        var expander = (UiMarkupExpander)panel.Children[0];
        Assert.Equal("Hi", expander.Header.GetTooltipText());
        Assert.True(expander.Header.AuthoredTooltipEnabled);
        Assert.NotEqual(0u, expander.Header.AuthoredTooltipRootElementId);
        Assert.Null(((UiMarkupExpander)Named(panel, "plain")).Header.GetTooltipText());
    }

    [Fact]
    public void Expander_ThemeSwapsInTheModernFont()
    {
        var modern = new UiDatFont(
            fgTex: 2u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f,
            new System.Collections.Generic.Dictionary<char, DatReaderWriter.Types.FontCharDesc>());
        var themes = new PluginUiThemeSettings(modernFont: new(() => modern)) { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="A"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), themes: themes);
        Assert.Same(modern, panel.Children.OfType<UiMarkupExpander>().Single().Header.DatFont);
    }

    [Fact]
    public void Expander_ExplicitColorSurvivesTheTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="A" color="#FFFF0000"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), themes: themes);
        var expander = panel.Children.OfType<UiMarkupExpander>().Single();
        Assert.Equal(new System.Numerics.Vector4(1f, 0f, 0f, 1f), expander.Header.TextColor);
    }
}
