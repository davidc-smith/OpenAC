using System.Numerics;
using AcDream.Core.Combat;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiCommandDeck
{
    private static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    private static readonly Vector4 FrameTop = Rgb(0x2B2A31);
    private static readonly Vector4 FrameBottom = Rgb(0x17161B);
    private static readonly Vector4 FrameEdge = Rgb(0x0B0A0D);
    private static readonly Vector4 Bronze = Rgb(0x6B5A3A, 0.7f);
    private static readonly Vector4 Track = Rgb(0x0C0B0F);
    private static readonly Vector4 TrailInk = Rgb(0xF3E2C0, 0.45f);
    private static readonly Vector4 TextInk = Rgb(0xFFFFFF);
    private static readonly Vector4 LabelInk = Rgb(0xC9BFA6);
    private static readonly Vector4 StudOff = Rgb(0x34323B);
    private static readonly Vector4 StudOffInk = Rgb(0xA8A3B5);
    private static readonly Vector4 StudOn = Rgb(0xD14A5C);
    private static readonly Vector4 CombatGlow = Rgb(0xC2233A, 0.55f);
    private static readonly Vector4 PeaceGlow = Rgb(0x78C8A0, 0.35f);
    private static readonly (Vector4 Top, Vector4 Bottom) HealthInk = (Rgb(0xFF6B7D), Rgb(0x7D0F1C));
    private static readonly (Vector4 Top, Vector4 Bottom) StaminaInk = (Rgb(0xFFE27A), Rgb(0x8A6C10));
    private static readonly (Vector4 Top, Vector4 Bottom) ManaInk = (Rgb(0x8FB0FF), Rgb(0x1A3590));
    private static readonly Vector4 PowerInk = Rgb(0xFFF6D8);
    private static readonly Vector4 Handle = Rgb(0xE2C27A);
    private static readonly Vector4 JumpInk = Rgb(0x7FD6A8);
    private static readonly Vector4 CastLit = Rgb(0xCFE0FF);

    protected override void OnDraw(UiRenderContext ctx)
    {
        CommandDeckView v = _view;
        double now = _clock();

        DrawFrame(ctx);
        DrawBar(ctx, CommandDeckLayout.HealthBar, "HEALTH", v.Health, HealthInk,
            pulse: v.LowHealth ? 0.25f + 0.25f * MathF.Sin((float)now * 6f) : 0f);
        DrawBar(ctx, CommandDeckLayout.StaminaBar, "STAMINA", v.Stamina, StaminaInk, 0f);
        DrawBar(ctx, CommandDeckLayout.ManaBar, "MANA", v.Mana, ManaInk, 0f);

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

    private static void DrawFrame(UiRenderContext ctx)
    {
        DeckRect f = CommandDeckLayout.Frame;
        ctx.FillRoundedRect(f.X - 2, f.Y - 2, f.W + 4, f.H + 4, 10f, FrameEdge);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, new CanvasCornerRadii(8f, 8f, 8f, 8f), FrameTop, FrameBottom);
        ctx.StrokeRoundedRect(f.X + 3, f.Y + 3, f.W - 6, f.H - 6, 5f, Bronze, 1f);
    }

    private void DrawBar(UiRenderContext ctx, DeckRect r, string label, DeckVitalView vital,
        (Vector4 Top, Vector4 Bottom) ink, float pulse)
    {
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 4f, Track);
        if (vital.Known)
        {
            if (vital.Trail > vital.Fill)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Trail, r.H, 3f, TrailInk);
            if (vital.Fill > 0f)
                ctx.FillVerticalGradient(r.X, r.Y, r.W * vital.Fill, r.H,
                    new CanvasCornerRadii(3f, 3f, 3f, 3f), ink.Top, ink.Bottom);
            if (pulse > 0f)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Fill, r.H, 3f, new Vector4(1f, 1f, 1f, pulse * 0.4f));
        }
        DrawText(ctx, label, r.X + 8, r.Y + 4, LabelInk, small: true);
        DrawTextRight(ctx, vital.Text, r.X + r.W - 8, r.Y + 3, TextInk);
    }

    private void DrawPowerRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, v.PowerLabel, label.X, label.Y + 6, LabelInk, small: true);

        DeckRect t = CommandDeckLayout.PowerTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, 5f, Track);
        for (int i = 1; i < 4; i++)
            ctx.DrawFill(t.X + t.W * i / 4f, t.Y, 1f, t.H, Rgb(0x4A4752));
        if (v.PowerFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.PowerFill, t.H, 4f, PowerInk);
        float hx = t.X + t.W * v.DesiredPower;
        ctx.FillRoundedRect(hx - 5f, t.Y - 5f, 10f, t.H + 10f, 3f, Handle);

        string[] letters = ["H", "M", "L"];
        AttackHeight[] heights = [AttackHeight.High, AttackHeight.Medium, AttackHeight.Low];
        for (int i = 0; i < 3; i++)
            DrawStud(ctx, CommandDeckLayout.HeightButton(i), on: v.Height == heights[i], icon: null, letter: letters[i]);
    }

    private void DrawCastingRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, "CASTING", label.X, label.Y + 6, LabelInk, small: true);
        DeckRect text = CommandDeckLayout.CastingText;
        DrawText(ctx, v.CastingText, text.X, text.Y + 4, v.CastingLit ? CastLit : StudOffInk);
        DeckRect recast = CommandDeckLayout.Recast;
        DrawStud(ctx, recast, on: false, icon: HudIcons.Recast, letter: "RECAST", dim: !v.RecastEnabled);
    }

    private void DrawRecallRow(UiRenderContext ctx)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, "RECALL", label.X, label.Y + 6, LabelInk, small: true);
        string[] icons =
        [
            HudIcons.RecallLifestone, HudIcons.RecallMarketplace, HudIcons.RecallHouse,
            HudIcons.RecallMansion, HudIcons.RecallAllegiance,
        ];
        for (int i = 0; i < 5; i++)
            DrawStud(ctx, CommandDeckLayout.RecallStud(i), on: false, icon: icons[i], letter: null);
    }

    private void DrawJump(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.JumpLabel;
        DrawText(ctx, "JUMP", label.X, label.Y, v.JumpCharging ? JumpInk : LabelInk with { W = 0.5f }, small: true);
        DeckRect t = CommandDeckLayout.JumpTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, 3f, Track);
        if (v.JumpFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.JumpFill, t.H, 3f, JumpInk);
    }

    private void DrawOrb(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect o = CommandDeckLayout.Orb;
        bool combat = v.Orb != DeckOrbMode.Peace;
        ctx.FillEllipse(o.X - 6, o.Y - 6, o.W + 12, o.H + 12, combat ? CombatGlow : PeaceGlow);
        ctx.FillEllipse(o.X - 2, o.Y - 2, o.W + 4, o.H + 4, FrameEdge);
        ctx.FillEllipse(o.X, o.Y, o.W, o.H, Rgb(0x6B5A3A));
        ctx.FillEllipse(o.X + 3, o.Y + 3, o.W - 6, o.H - 6, Rgb(0x0B0C12));
        ctx.FillEllipse(o.X + 8, o.Y + 6, o.W - 24, o.H - 24, Rgb(0x2A2D3C, 0.9f));
        ctx.FillEllipse(o.X + 22, o.Y + 12, 22, 12, new Vector4(1f, 1f, 1f, 0.12f));

        string icon = v.Orb switch
        {
            DeckOrbMode.Melee => HudIcons.Melee,
            DeckOrbMode.Missile => HudIcons.Missile,
            DeckOrbMode.Magic => HudIcons.Magic,
            _ => HudIcons.Peace,
        };
        DrawIcon(ctx, icon, o.X + o.W / 2 - 16, o.Y + o.H / 2 - 22, 32f, TextInk);
        string label = v.Orb switch
        {
            DeckOrbMode.Melee => "MELEE",
            DeckOrbMode.Missile => "MISSILE",
            DeckOrbMode.Magic => "MAGIC",
            _ => "PEACE",
        };
        DrawTextCentered(ctx, label, o.X + o.W / 2, o.Y + o.H / 2 + 14, LabelInk, small: true);
    }

    private void DrawStuds(UiRenderContext ctx, CommandDeckView v)
    {
        DrawStud(ctx, CommandDeckLayout.Stud(0), v.RepeatAttacks, HudIcons.Repeat, null);
        DrawStud(ctx, CommandDeckLayout.Stud(1), v.AutoTarget, HudIcons.AutoTarget, null);
        DrawStud(ctx, CommandDeckLayout.Stud(2), v.KeepInView, HudIcons.KeepInView, null);
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, bool on, string? icon, string? letter, bool dim = false)
    {
        ctx.FillRoundedRect(r.X - 1, r.Y - 1, r.W + 2, r.H + 2, 6f, FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, on ? StudOn : StudOff);
        Vector4 ink = on ? TextInk : StudOffInk;
        if (dim) ink = ink with { W = 0.4f };
        float x = r.X + 4;
        if (icon is not null)
        {
            DrawIcon(ctx, icon, r.X + (letter is null ? (r.W - 14) / 2 : 6), r.Y + (r.H - 14) / 2, 14f, ink);
            x = r.X + 24;
        }
        if (letter is not null)
        {
            if (icon is null) DrawTextCentered(ctx, letter, r.X + r.W / 2, r.Y + 5, ink, small: true);
            else DrawText(ctx, letter, x, r.Y + 6, ink, small: true);
        }
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    private static void DrawText(UiRenderContext ctx, string text, float x, float y, Vector4 ink, bool small = false) =>
        ctx.DrawString(text, x, y, ink);

    private static void DrawTextRight(UiRenderContext ctx, string text, float right, float y, Vector4 ink) =>
        ctx.DrawString(text, right - MeasureText(ctx, text), y, ink);

    private static void DrawTextCentered(UiRenderContext ctx, string text, float centre, float y, Vector4 ink, bool small = false) =>
        ctx.DrawString(text, centre - MeasureText(ctx, text) / 2f, y, ink);

    private static float MeasureText(UiRenderContext ctx, string text) =>
        ctx.DefaultFont?.MeasureWidth(text) ?? 0f;
}
