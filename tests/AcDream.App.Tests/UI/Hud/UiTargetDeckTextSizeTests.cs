using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Targeting;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiTargetDeckTextSizeTests
{
    private sealed class NoFrame : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiTargetDeck Make(TargetPlateModel model, HudText text, DeckFonts? fonts = null)
    {
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0, fonts, text);
        deck.Refresh();
        return deck;
    }

    public static IEnumerable<object[]> Plates()
    {
        yield return [Build(Monster(), pluginRows: [PluginRow])];
        yield return [Build(Player())];
        yield return [Build(new ClientObject { ObjectId = 4, Name = "Thorsten the Very Long Named Vendor", Type = ItemType.Creature,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Vendor })];
    }

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void ThePlateKeepsItsSize(int percent)
    {
        UiTargetDeck deck = Make(Build(Monster()), At(percent), FakeDeckFonts.Make());
        Assert.Equal(TargetDeckGeometry.Width, deck.Width);
        Assert.Equal(TargetDeckGeometry.Height, deck.Height);
    }

    [Theory]
    [MemberData(nameof(Plates))]
    public void AtLargeText_EveryFaceFitsItsBox(TargetPlateModel model)
    {
        var fits = new List<DeckFit>();
        UiTargetDeck deck = Make(model, At(150), FakeDeckFonts.Make(fits));
        MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - TargetDeckGeometry.Name.H) < 0.01f
            && f.Pixels < DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, 1.5f));
    }

    [Theory]
    [MemberData(nameof(Plates))]
    public void AtSmallText_ThePlateDraws_AndTheChipFaceIsTheMinimum(TargetPlateModel model)
    {
        var fits = new List<DeckFit>();
        UiTargetDeck deck = Make(model, At(80), FakeDeckFonts.Make(fits));
        MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        DeckFit[] chips = fits.Where(f => f.BasePixels == 9f).ToArray();   // ChipPixels: 9 × 0.8 rounds to 7
        if (model.Chips.Count > 0 || model.Level is not null) Assert.NotEmpty(chips);
        Assert.All(chips, f => Assert.Equal(BundledUiFont.MinimumPixelHeight, f.Pixels));
    }

    [Fact]
    public void ChipAndLevelWidths_AreKept_AtLargeText()
    {
        var model = Build(Monster());
        DeckFonts fonts = FakeDeckFonts.Make();
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NoFrame(), "unused");
        var ctx = new UiRenderContext(renderer, new Vector2(400f, 300f));
        UiTargetDeck normal = Make(model, new HudText(), fonts);
        UiTargetDeck large = Make(model, At(150), fonts);
        foreach (string s in new[] { "45", "YOURS", "PKL" })
            Assert.Equal(normal.ChipWidth(ctx, s), large.ChipWidth(ctx, s));
    }

    [Fact]
    public void MenuLinesGrowWithLargeText()
    {
        TargetPlateModel model = Build(Monster());
        DeckFonts fonts = FakeDeckFonts.Make();
        UiTargetDeck normal = Make(model, new HudText(), fonts);
        UiTargetDeck large = Make(model, At(150), fonts);
        Assert.Equal(normal.MenuEntryRect(0).H + 6f, large.MenuEntryRect(0).H, 3);
    }

    [Fact]
    public void ARightClickOnTheName_ListsTextSize_OnAStud_Nothing()
    {
        var text = new HudText();
        UiTargetDeck deck = Make(Build(Monster()), text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(deck);
        DeckRect name = TargetDeckGeometry.Name;
        HudTestInput.RightClick(root, deck, name.X + 4f, name.Y + name.H / 2f);

        Assert.True(deck.TextMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Target), deck.TextMenu.ItemsSource());
        deck.TextMenu.Selected!(2);   // 80%
        Assert.Equal(80, text.Settings.PercentFor(HudDeck.Target));
        deck.TextMenu.Close();

        DeckRect stud = TargetDeckGeometry.Stud(0);
        HudTestInput.RightClick(root, deck, stud.X + stud.W / 2f, stud.Y + stud.H / 2f);
        Assert.False(deck.TextMenu.IsOpen);
    }
}
