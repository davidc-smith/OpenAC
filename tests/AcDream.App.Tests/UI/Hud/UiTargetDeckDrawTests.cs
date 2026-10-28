using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Targeting;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiTargetDeckDrawTests
{
    private static int QuadsInside(UiTargetDeck deck)
    {
        var renderer = MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                Assert.InRange(s.Verts[i], -0.01f, TargetDeckGeometry.Width + 0.01f);
                Assert.InRange(s.Verts[i + 1], -0.01f, TargetDeckGeometry.Height + 0.01f);
            }
        return quads;
    }

    [Theory]
    [MemberData(nameof(EveryKindWithAPluginRow), MemberType = typeof(TargetDeckModels))]
    public void DrawsEveryKindInsideTheDeck(TargetPlateModel model)
    {
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        Assert.True(QuadsInside(deck) > 0);
    }

    [Fact]
    public void TwoPluginRowsUnderVitalsStayInside()
    {
        TargetPlateModel model = Build(Monster(), pluginRows: [PluginRow, PluginRow with { Rgba = null }]);
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        Assert.True(QuadsInside(deck) > 0);
    }

    [Fact]
    public void ANullModelDrawsNothing()
    {
        var deck = new UiTargetDeck(() => null, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        Assert.Equal(0, QuadsInside(deck));
    }

    [Fact]
    public void PluginRowsHaveAGreenLeftEdge()
    {
        TargetPlateModel model = Build(Monster(), pluginRows: [PluginRow]);
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        var renderer = MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        DeckRect line = TargetDeckGeometry.PluginLine(0);
        Assert.Contains(MarkupDrawProbe.QuadBounds(renderer, DeckChrome.JumpInk),
            b => MathF.Abs(b.X - line.X) < 0.5f && MathF.Abs(b.W - 2f) < 0.5f);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StaleAndFailedVitalsDrawAtReducedAlpha(bool succeeded, bool fresh)
    {
        var log = new TargetAppraisalLog();
        log.Record(7, succeeded, 1f, now: fresh ? 49.0 : 30.0);
        TargetPlateModel model = Build(Monster(), log: log);
        Assert.Equal(succeeded ? TargetVitalsState.Stale : TargetVitalsState.Failed, model.Vitals!.Value.State);
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        Assert.True(HasVertex(deck, DeckChrome.Track with { W = UiTargetDeck.DimAlpha }));

        log.Record(7, true, 1f, now: 49.0);
        model = Build(Monster(), log: log);
        Assert.Equal(TargetVitalsState.Fresh, model.Vitals!.Value.State);
        Assert.False(HasVertex(deck, DeckChrome.Track with { W = UiTargetDeck.DimAlpha }));
    }

    private static bool HasVertex(UiTargetDeck deck, System.Numerics.Vector4 c)
    {
        deck.Refresh();
        var renderer = MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
                if (MathF.Abs(s.Verts[i + 4] - c.X) < 0.01f && MathF.Abs(s.Verts[i + 5] - c.Y) < 0.01f
                    && MathF.Abs(s.Verts[i + 6] - c.Z) < 0.01f && MathF.Abs(s.Verts[i + 7] - c.W) < 0.01f)
                    return true;
        }
        return false;
    }

    [Fact]
    public void LongNamesAreShortenedWithTheCastingTextHelper()
    {
        static float Measure(string t) => t.Length * 8f;
        string fitted = UiTargetDeck.FitName("Ancient Drudge Ravener of the Deep Hollow Below", Measure, TargetDeckGeometry.Name.W);
        Assert.Equal(UiCommandDeck.FitCastingText("Ancient Drudge Ravener of the Deep Hollow Below", Measure, TargetDeckGeometry.Name.W), fitted);
        Assert.NotEqual("Ancient Drudge Ravener of the Deep Hollow Below", fitted);
        Assert.True(Measure(fitted) <= TargetDeckGeometry.Name.W);
    }

    [Fact]
    public void OpenMenuDrawsInTheOverlayBelowThePlate()
    {
        TargetPlateModel model = Build(Monster());
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        Assert.Empty(MarkupDrawProbe.DrawOverlays(deck).DebugSpriteSegmentVerts);

        DeckRect more = TargetDeckGeometry.Stud(3);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)(more.X + more.W / 2), Data2: (int)(more.Y + more.H / 2)));
        Assert.True(deck.MenuOpen);
        var renderer = MarkupDrawProbe.DrawOverlays(deck);
        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                Assert.True(s.Verts[i + 1] >= TargetDeckGeometry.Height - 0.01f);
            }
        Assert.True(quads > 0);
    }

    [Fact]
    public void TheNameIsDrawnInTheBoldFace()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        TargetPlateModel model = Build(Monster());
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0, fonts);
        deck.Refresh();
        MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        Assert.Contains((DeckFonts.TextPixelHeight, BundledUiFontWeight.SemiBold), baked);
    }
}
