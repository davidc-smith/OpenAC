using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckTextSizeTests
{
    private sealed class NoActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiCommandDeck Make(CombatMode mode, HudText text, DeckFonts? fonts = null, float scale = 1f)
    {
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, PowerBarLevel = 0.3f, SessionKey = 1,
            Health = new DeckVitalInput(0.2f, 1234u, 2000u), Stamina = new DeckVitalInput(0.6f, 600u, 1000u),
            Mana = new DeckVitalInput(1f, 100u, 100u),
            OutlinedSpellName = "Incantation of Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0, fonts, text);
        deck.Refresh();
        if (scale != 1f) deck.ApplyGeometry(DeckGeometry.Create(scale, 1f));
        return deck;
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(150)]
    public void TheDeckKeepsItsSize_AtEveryTextSize(int percent)
    {
        UiCommandDeck deck = Make(CombatMode.Melee, At(percent), FakeDeckFonts.Make());
        Assert.Equal(DeckGeometry.Default.Width, deck.Width);
        Assert.Equal(DeckGeometry.Default.Height, deck.Height);
    }

    [Theory]
    [InlineData(CombatMode.Melee, 1f)]
    [InlineData(CombatMode.Magic, 1f)]
    [InlineData(CombatMode.NonCombat, 1f)]
    [InlineData(CombatMode.Magic, 0.75f)]
    public void AtLargeText_EveryFaceFitsItsBox_AndTheValuesAreCapped(CombatMode mode, float scale)
    {
        var fits = new List<DeckFit>();
        UiCommandDeck deck = Make(mode, At(150), FakeDeckFonts.Make(fits), scale);
        MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);

        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        float uncapped = DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, scale * 1.5f);
        DeckFit[] values = fits
            .Where(f => f.BasePixels == DeckFonts.TextPixelHeight && MathF.Abs(f.BoxHeight - deck.Geometry.HealthBar.H) < 0.01f)
            .ToArray();
        Assert.NotEmpty(values);
        Assert.All(values, f => Assert.True(f.Pixels < uncapped));
    }

    [Fact]
    public void ARightClickOnTheFrame_ListsTextSize_OnTheOrb_Nothing()
    {
        var text = new HudText();
        UiCommandDeck deck = Make(CombatMode.Melee, text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(deck);
        DeckRect frame = deck.Geometry.Frame;
        HudTestInput.RightClick(root, deck, frame.X + 20f, frame.Y + 6f);

        Assert.True(deck.TextMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Command), deck.TextMenu.ItemsSource());
        deck.TextMenu.Selected!(8);   // 140%
        Assert.Equal(140, text.Settings.PercentFor(HudDeck.Command));
        Assert.Equal(1.4f, deck.TextFactor, 3);
        deck.TextMenu.Close();

        DeckRect orb = deck.Geometry.Orb;
        HudTestInput.RightClick(root, deck, orb.X + orb.W / 2f, orb.Y + orb.H / 2f);
        Assert.False(deck.TextMenu.IsOpen);
    }

    [Fact]
    public void ABarLabel_IsCutBeforeTheValue_At150()
    {
        DeckRect bar = DeckGeometry.Default.HealthBar;
        float px = DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, 1.5f);
        float valueW = 16 * px * 0.6f;   // a long value
        float room = UiCommandDeck.BarLabelRoom(bar, valueW, 1f);
        Assert.True(room < bar.W - valueW);
        float labelPx = DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1.5f);
        string cut = UiCommandDeck.FitCastingText("HEALTH", t => t.Length * labelPx * 0.6f, room);
        Assert.NotEqual("HEALTH", cut);
        Assert.EndsWith("...", cut);
        Assert.True(cut.Length * labelPx * 0.6f <= room);
        Assert.True(UiCommandDeck.BarLabelRoom(bar, 0f, 1f) > 0f);
        Assert.Equal(0f, UiCommandDeck.BarLabelRoom(bar, bar.W, 1f));
    }

    [Fact]
    public void TheCastWord_IsShortenedToTheRoomBesideItsIcon()
    {
        DeckRect cast = DeckGeometry.Default.Cast;
        float room = UiCommandDeck.CastWordRoom(cast, 1f);
        Assert.Equal(cast.W - 21f, room, 3);
        string cut = UiCommandDeck.FitCastingText("CAST", t => t.Length * 12f, room);
        Assert.True(cut.Length * 12f <= room);
        Assert.NotEqual("CAST", cut);
        Assert.Equal("CAST", UiCommandDeck.FitCastingText("CAST", t => t.Length * 2f, room));
    }
}
