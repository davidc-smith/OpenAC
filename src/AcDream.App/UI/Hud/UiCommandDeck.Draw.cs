using System.Numerics;
using AcDream.Core.Combat;
using static AcDream.App.UI.Hud.DeckChrome;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiCommandDeck
{
    private static readonly string[] HeightLetters = ["H", "M", "L"];
    private static readonly AttackHeight[] HeightValues = [AttackHeight.High, AttackHeight.Medium, AttackHeight.Low];
    private static readonly string[] RecallIcons =
    [
        HudIcons.RecallLifestone, HudIcons.RecallMarketplace, HudIcons.RecallHouse,
        HudIcons.RecallMansion, HudIcons.RecallAllegiance,
    ];

    /// <summary>A length in base points at the deck's scale.</summary>
    private float P(float points) => points * _geometry.Scale;

    /// <summary>A line weight at the deck's scale, in whole points, never under one.</summary>
    private float Line(float points) => DeckChrome.Line(points, _geometry.Scale);

    /// <summary>The right-edge grip while it is hovered or being dragged.</summary>
    private void DrawResizeGrip(UiRenderContext ctx) =>
        DeckChrome.DrawResizeGrip(ctx, FindRoot()?.ResizeEdgesFor(this) ?? ResizeEdges.None, _geometry.Frame, _geometry.Scale);

    protected override void OnDraw(UiRenderContext ctx)
    {
        CommandDeckView v = _view;
        double now = _clock();

        DrawFrame(ctx);
        DrawResizeGrip(ctx);
        DrawBar(ctx, _geometry.HealthBar, "HEALTH", v.Health, HealthInk,
            pulse: v.LowHealth ? 0.25f + 0.25f * MathF.Sin((float)now * 6f) : 0f);
        DrawBar(ctx, _geometry.StaminaBar, "STAMINA", v.Stamina, StaminaInk, 0f);
        DrawBar(ctx, _geometry.ManaBar, "MANA", v.Mana, ManaInk, 0f);

        switch (v.Row)
        {
            case DeckContextRow.Power: DrawPowerRow(ctx, v); break;
            case DeckContextRow.Casting: DrawCastingRow(ctx, v); break;
            case DeckContextRow.Recall: DrawRecallRow(ctx); break;
        }
        DrawJump(ctx, v);
        DrawOrb(ctx, v);
        if (v.ShowStuds) DrawStuds(ctx, v);
    }

    private void DrawFrame(UiRenderContext ctx) =>
        DeckChrome.DrawFrame(ctx, _geometry.Frame, _geometry.Scale, squareTop: SquareTopCorners);

    private void DrawBar(UiRenderContext ctx, DeckRect r, string label, DeckVitalView vital,
        (Vector4 Top, Vector4 Bottom) ink, float pulse)
    {
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, P(4), Track);
        if (vital.Known)
        {
            if (vital.Trail > vital.Fill)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Trail, r.H, P(3), TrailInk);
            if (vital.Fill > 0f)
                ctx.FillVerticalGradient(r.X, r.Y, r.W * vital.Fill, r.H,
                    new CanvasCornerRadii(P(3), P(3), P(3), P(3)), ink.Top, ink.Bottom);
            if (pulse > 0f)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Fill, r.H, P(3), new Vector4(1f, 1f, 1f, pulse * 0.4f));
        }
        DrawText(ctx, label, r.X + P(8), r, LabelInk, small: true);
        DrawTextRight(ctx, vital.Text, r.X + r.W - P(8), r, TextInk);
    }

    private void DrawPowerRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, v.PowerLabel, label.X, label, LabelInk, small: true);

        DeckRect t = _geometry.PowerTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, P(5), Track);
        for (int i = 1; i < 4; i++)
            ctx.DrawFill(t.X + t.W * i / 4f, t.Y, Line(1), t.H, Rgb(0x4A4752));
        if (v.PowerFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.PowerFill, t.H, P(4), PowerInk);
        float hx = t.X + t.W * v.DesiredPower;
        ctx.FillRoundedRect(hx - P(5), t.Y - P(5), P(10), t.H + P(10), P(3), Handle);

        for (int i = 0; i < 3; i++)
            DrawStud(ctx, _geometry.HeightButton(i), on: v.Height == HeightValues[i], icon: null, letter: HeightLetters[i]);
    }

    private void DrawCastingRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, "CASTING", label.X, label, LabelInk, small: true);
        DeckRect text = _geometry.CastingText;
        DrawText(ctx, FitCastingText(v.CastingText, t => Face(small: true).Measure(ctx, t), text.W), text.X, text,
            v.CastingLit ? CastLit : v.CastingDim ? StudOffInk : TextInk, small: true);
        DrawCastButton(ctx, _geometry.Cast, dim: !v.CastEnabled);
    }

    /// <summary>The CAST button: the cast icon and the word CAST in the small SemiBold face.</summary>
    private void DrawCastButton(UiRenderContext ctx, DeckRect r, bool dim)
    {
        Vector4 ink = DeckChrome.DrawStud(ctx, r, _geometry.Scale, on: false, dim);
        DeckFace face = DeckFace.SmallBoldFor(_fonts, _geometry.Scale);
        const string word = "CAST";
        var (iconX, iconY, iconSize, textX) = DeckGeometry.CastLabel(r, face.Measure(ctx, word), _geometry.Scale);
        DrawIcon(ctx, HudIcons.Cast, iconX, iconY, iconSize, ink);
        face.Draw(ctx, word, textX, r.Y + (r.H - face.LineHeight(ctx)) / 2f, ink);
    }

    /// <summary>The spell name, shortened with "..." so it stops short of the CAST button;
    /// the button's tooltip carries the full name.</summary>
    internal static string FitCastingText(string text, Func<string, float> measure, float width) =>
        PluginWindowChrome.Ellipsize(text, measure, width);

    private void DrawRecallRow(UiRenderContext ctx)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, "RECALL", label.X, label, LabelInk, small: true);
        for (int i = 0; i < 5; i++)
            DrawStud(ctx, _geometry.RecallStud(i), on: false, icon: RecallIcons[i], letter: null);
    }

    private void DrawJump(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.JumpLabel;
        DrawText(ctx, "JUMP", label.X, label, v.JumpCharging ? JumpInk : LabelInk with { W = 0.5f }, small: true);
        DeckRect t = _geometry.JumpTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, P(3), Track);
        if (v.JumpFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.JumpFill, t.H, P(3), JumpInk);
    }

    private void DrawOrb(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect o = _geometry.Orb;
        bool combat = v.Orb != DeckOrbMode.Peace;
        ctx.FillEllipse(o.X - P(6), o.Y - P(6), o.W + P(12), o.H + P(12), combat ? CombatGlow : PeaceGlow);
        ctx.FillEllipse(o.X - P(2), o.Y - P(2), o.W + P(4), o.H + P(4), FrameEdge);
        ctx.FillEllipse(o.X, o.Y, o.W, o.H, Rgb(0x6B5A3A));
        ctx.FillEllipse(o.X + P(3), o.Y + P(3), o.W - P(6), o.H - P(6), Rgb(0x0B0C12));
        ctx.FillEllipse(o.X + P(8), o.Y + P(6), o.W - P(24), o.H - P(24), Rgb(0x2A2D3C, 0.9f));
        ctx.FillEllipse(o.X + P(22), o.Y + P(12), P(22), P(12), new Vector4(1f, 1f, 1f, 0.12f));

        string icon = v.Orb switch
        {
            DeckOrbMode.Melee => HudIcons.Melee,
            DeckOrbMode.Missile => HudIcons.Missile,
            DeckOrbMode.Magic => HudIcons.Magic,
            _ => HudIcons.Peace,
        };
        DrawIcon(ctx, icon, o.X + o.W / 2 - P(16), o.Y + o.H / 2 - P(22), P(32), TextInk);
        string label = v.Orb switch
        {
            DeckOrbMode.Melee => "MELEE",
            DeckOrbMode.Missile => "MISSILE",
            DeckOrbMode.Magic => "MAGIC",
            _ => "PEACE",
        };
        DrawTextCentered(ctx, label, o.X + o.W / 2, new DeckRect(o.X, o.Y + o.H / 2 + P(12), o.W, P(16)), LabelInk, small: true);
    }

    private void DrawStuds(UiRenderContext ctx, CommandDeckView v)
    {
        DrawStud(ctx, _geometry.Stud(0), v.RepeatAttacks, HudIcons.Repeat, null);
        DrawStud(ctx, _geometry.Stud(1), v.AutoTarget, HudIcons.AutoTarget, null);
        DrawStud(ctx, _geometry.Stud(2), v.KeepInView, HudIcons.KeepInView, null);
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, bool on, string? icon, string? letter, bool dim = false)
    {
        Vector4 ink = DeckChrome.DrawStud(ctx, r, _geometry.Scale, on, dim);
        if (icon is not null)
            DrawIcon(ctx, icon, r.X + (r.W - P(14)) / 2, r.Y + (r.H - P(14)) / 2, P(14), ink);
        else if (letter is not null)
            DrawTextCentered(ctx, letter, r.X + r.W / 2, r, ink, small: true);
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    private DeckFace Face(bool small) => DeckFace.For(_fonts, _geometry.Scale, small);

    /// <summary>Text from <paramref name="x"/>, centred on <paramref name="band"/>'s height.</summary>
    private void DrawText(UiRenderContext ctx, string text, float x, DeckRect band, Vector4 ink, bool small = false)
    {
        DeckFace face = Face(small);
        face.Draw(ctx, text, x, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);
    }

    private void DrawTextRight(UiRenderContext ctx, string text, float right, DeckRect band, Vector4 ink) =>
        DrawText(ctx, text, right - Face(small: false).Measure(ctx, text), band, ink);

    private void DrawTextCentered(UiRenderContext ctx, string text, float centre, DeckRect band, Vector4 ink, bool small = false) =>
        DrawText(ctx, text, centre - Face(small).Measure(ctx, text) / 2f, band, ink, small);
}
