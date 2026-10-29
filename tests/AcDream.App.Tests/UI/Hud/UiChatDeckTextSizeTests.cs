using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;
using Harness = AcDream.App.Tests.UI.Hud.UiChatDeckTests.Harness;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTextSizeTests
{
    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void TheWindowTabsAndChipKeepTheirSize(int percent)
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        var plain = new Harness(fonts: fonts);
        var h = new Harness(fonts: fonts, text: At(percent));
        Assert.Equal(plain.Deck.Width, h.Deck.Width);
        Assert.Equal(plain.Deck.Height, h.Deck.Height);
        Assert.Equal(plain.Deck.Geometry.Tabs, h.Deck.Geometry.Tabs);
        Assert.Equal(plain.Deck.Geometry.Chip, h.Deck.Geometry.Chip);
    }

    [Fact]
    public void TheTranscriptAndInputKeepTheirOwnSizes_TheMenusFollowTheFactor()
    {
        var h = new Harness(fonts: FakeDeckFonts.Make(), text: At(150));
        ChatDeckAppearance a = h.Model!.Settings.Appearance;
        float menuLine = FakeDeckFonts.Line(DeckFonts.PixelHeight(a.FontSize, 1.5f));
        Assert.Equal(FakeDeckFonts.Line(a.FontSize), h.Deck.Transcript.DatFont!.LineHeight);
        Assert.Equal(FakeDeckFonts.Line(a.InputFontSize), h.Deck.Input.DatFont!.LineHeight);
        Assert.Equal(menuLine, h.Deck.ChipMenu.DatFont!.LineHeight);
        Assert.Equal(menuLine, h.Deck.TabMenu.DatFont!.LineHeight);
        Assert.Equal(menuLine - FakeDeckFonts.Line(a.FontSize), h.Deck.ChipMenu.RowGrow, 3);
        Assert.Equal(menuLine - FakeDeckFonts.Line(a.FontSize), h.Deck.TabMenu.RowGrow, 3);
    }

    [Fact]
    public void PeekLinesGrow_SoFewerShow()
    {
        var a = new ChatDeckAppearance();
        var normal = new Harness(fonts: FakeDeckFonts.Make());
        var large = new Harness(fonts: FakeDeckFonts.Make(), text: At(150));
        float height = ChatDeckPeekFeed.MaxLines * UiChatDeck.PeekRowHeight(a);

        Assert.Equal(UiChatDeck.PeekRowHeight(a), normal.Deck.PeekRow(a));
        Assert.True(large.Deck.PeekRow(a) > normal.Deck.PeekRow(a));
        Assert.Equal(ChatDeckPeekFeed.MaxLines, UiChatDeck.PeekLinesFitting(height, normal.Deck.PeekRow(a)));
        Assert.True(UiChatDeck.PeekLinesFitting(height, large.Deck.PeekRow(a)) < ChatDeckPeekFeed.MaxLines);
    }

    /// <summary>The glyph quads (the fake fonts' texture) drawn inside <paramref name="box"/>.</summary>
    private static int GlyphsIn(AcDream.App.Rendering.TextRenderer renderer, DeckRect box)
    {
        int count = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 1u) continue;
            for (int q = 0; q + 48 <= s.Verts.Count; q += 48)
            {
                float x = s.Verts[q], y = s.Verts[q + 1];
                if (x >= box.X - 0.5f && x <= box.X + box.W + 0.5f && y >= box.Y - 0.5f && y <= box.Y + box.H + 0.5f)
                    count++;
            }
        }
        return count;
    }

    [Fact]
    public void AtNormalText_TheChipLabelDrawsWhole_ThoughSemiBoldIsWiderThanTheFaceItIsSizedWith()
    {
        // The chip is sized with the Regular face and drawn SemiBold; make SemiBold much wider so the
        // Regular-sized room cannot hold it. At 100% the label still draws whole, as it did before text sizes.
        var fonts = new DeckFonts((px, weight) =>
            FakeDeckFonts.Font(px, weight == BundledUiFontWeight.SemiBold ? 0.9f : 0.6f));
        var h = new Harness(fonts: fonts);
        string label = h.Deck.ChipLabel;
        AcDream.App.Rendering.TextRenderer renderer = MarkupDrawProbe.Draw(h.Deck, h.Deck.Width, h.Deck.Height);
        Assert.Equal(label.Length, GlyphsIn(renderer, h.Deck.Geometry.Chip with { W = h.Deck.Geometry.Chip.W + 200f }));
    }

    [Fact]
    public void AtLargeText_TabsChipAndBadgesFitTheirBoxes()
    {
        var fits = new List<DeckFit>();
        var h = new Harness(fonts: FakeDeckFonts.Make(fits), text: At(150));
        h.Say("one");   // an unread badge on the tabs that show it
        h.Deck.Refresh();
        MarkupDrawProbe.Draw(h.Deck, h.Deck.Width, h.Deck.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - ChatDeckGeometry.TabHeight) < 0.01f);
    }

    [Fact]
    public void ARightClickOnTheFrame_ListsTextSize_OnATab_TheTabMenu()
    {
        HudText text = At(100);
        var h = new Harness(text: text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(h.Deck);
        h.Deck.Refresh();
        HudTestInput.RightClick(root, h.Deck, 3f, 3f);

        Assert.True(h.Deck.TabMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Chat), h.Deck.TabMenu.ItemsSource());
        h.Deck.TabMenu.Selected!(7);   // 130%
        Assert.Equal(130, text.Settings.PercentFor(HudDeck.Chat));
        Assert.Equal(1.3f, h.Deck.TextFactor, 3);
        h.Deck.TabMenu.Close();

        DeckRect tab = h.Deck.Geometry.Tabs[0];
        HudTestInput.RightClick(root, h.Deck, tab.X + 2f, tab.Y + 2f);
        Assert.Equal(["Rename", "Edit tab…", "-", "Mark as read", "Mark all read", "-", "Delete"], h.Deck.TabMenu.ItemsSource());
    }

    [Fact]
    public void ARightClickOnTheInput_TheTranscriptOrTheSearchField_OpensNothing()
    {
        var h = new Harness(text: At(100));
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(h.Deck);
        h.Deck.Refresh();

        DeckRect input = h.Deck.Geometry.Input;
        HudTestInput.RightClick(root, h.Deck, input.X + 4f, input.Y + 4f);
        Assert.False(h.Deck.TabMenu.IsOpen);
        Assert.False(h.Deck.ChipMenu.IsOpen);

        DeckRect transcript = h.Deck.Geometry.Transcript;   // its blank area: no lines yet
        HudTestInput.RightClick(root, h.Deck, transcript.X + 10f, transcript.Y + 10f);
        Assert.False(h.Deck.TabMenu.IsOpen);

        h.Press(h.Deck.Geometry.SearchStud);
        h.Deck.Refresh();
        Assert.True(h.Deck.SearchOpen);
        DeckRect search = h.Deck.Geometry.SearchField;
        Assert.Empty(h.Deck.TabMenuItemsAt(search.X + 4f, search.Y + 4f));
        HudTestInput.RightClick(root, h.Deck, search.X + 4f, search.Y + 4f);
        Assert.False(h.Deck.TabMenu.IsOpen);
        Assert.Empty(h.Deck.TabMenuItemsAt(input.X + 4f, input.Y + 4f));
        Assert.Empty(h.Deck.TabMenuItemsAt(transcript.X + 10f, transcript.Y + 10f));
    }
}
