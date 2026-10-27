using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat.Deck;
using Harness = AcDream.App.Tests.UI.Hud.UiChatDeckTests.Harness;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The search field, driven through a real root the way the runtime mounts the deck.</summary>
public sealed class UiChatDeckSearchTests
{
    private sealed class World
    {
        public readonly Harness H = new();
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public UiChatDeck Deck => H.Deck;

        public World()
        {
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0), UiChatDeck.MountOptions(10f, 600f) with { Visible = true });
            Frame();
        }

        /// <summary>One client frame: tick, then draw (the draw runs the anchor layout pass).</summary>
        public void Frame()
        {
            _now += 16;
            Root.Tick(0.016, _now);
            MarkupDrawProbe.Draw(Root, Root.Width, Root.Height);
        }

        private long _now;

        public void Click(DeckRect r)
        {
            Vector2 origin = Deck.ScreenPosition;
            int x = (int)(origin.X + r.X + r.W / 2f), y = (int)(origin.Y + r.Y + r.H / 2f);
            Root.OnMouseDown(UiMouseButton.Left, x, y);
            Root.OnMouseUp(UiMouseButton.Left, x, y);
            Frame();
        }

        public void Type(string text)
        {
            foreach (char c in text) Root.OnChar(c);
        }
    }

    [Fact]
    public void TheStud_PutsFocusInTheSearchField_AndTypingFillsIt()
    {
        var w = new World();
        w.Click(w.Deck.Geometry.SearchStud);

        Assert.True(w.Deck.SearchOpen);
        Assert.Same(w.Deck.SearchField, w.Root.KeyboardFocus);
        w.Type("dog");
        Assert.Equal("dog", w.Deck.SearchField.Text);
        AssertPlaced(w.Deck.SearchField, w.Deck.Geometry.SearchField);
    }

    private static void AssertPlaced(UiElement e, DeckRect r)
    {
        Assert.Equal(r.X, e.Left, 2);
        Assert.Equal(r.Y, e.Top, 2);
        Assert.Equal(r.W, e.Width, 2);
        Assert.Equal(r.H, e.Height, 2);
    }

    [Fact]
    public void TheStud_TakesFocusFromTheInput()
    {
        var w = new World();
        w.Root.SetKeyboardFocus(w.Deck.Input);
        w.Click(w.Deck.Geometry.SearchStud);

        Assert.Same(w.Deck.SearchField, w.Root.KeyboardFocus);
        w.Type("d");
        Assert.Equal("d", w.Deck.SearchField.Text);
        Assert.Equal("", w.Deck.Input.Text);
    }

    [Fact]
    public void ClickingTheOpenSearchField_FocusesIt_AndTypingFillsIt()
    {
        var w = new World();
        w.Click(w.Deck.Geometry.SearchStud);
        w.Root.SetKeyboardFocus(w.Deck.Input);

        w.Click(w.Deck.Geometry.SearchField);

        Assert.Same(w.Deck.SearchField, w.Root.KeyboardFocus);
        w.Type("dog");
        Assert.Equal("dog", w.Deck.SearchField.Text);
        Assert.Equal("", w.Deck.Input.Text);
    }

    [Fact]
    public void AfterAResize_TheDrawKeepsTheDecksPlacement()
    {
        var w = new World();
        w.Deck.Width = 600f;
        w.Deck.Height = 300f;
        w.Frame();

        AssertPlaced(w.Deck.Input, w.Deck.Geometry.Input);
        AssertPlaced(w.Deck.Transcript, w.Deck.Geometry.Transcript);
    }
}
