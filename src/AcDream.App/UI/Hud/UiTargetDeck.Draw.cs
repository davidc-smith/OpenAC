using System.Numerics;
using AcDream.App.Rendering;
using AcDream.Core.Targeting;
using static AcDream.App.UI.Hud.DeckChrome;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiTargetDeck
{
    private const float ChipPixels = 9f;
    private const float ChipHeight = 12f;
    private const float ChipGap = 4f;
    private const float IconSize = 14f;
    private const float PortraitIconSize = 18f;

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_view is not { } v) return;

        DeckChrome.DrawFrame(ctx, TargetDeckGeometry.Body, 1f);
        DrawPortrait(ctx, v);
        DrawIdentity(ctx, v);
        DrawText(ctx, Fit(ctx, Small, v.SubLine, TargetDeckGeometry.Sub.W), TargetDeckGeometry.Sub.X,
            TargetDeckGeometry.Sub, LabelInk, Small);

        if (v.Vitals is { } vitals) DrawVitals(ctx, vitals);
        for (int i = 0; i < KindRowCount(v); i++)
            DrawRow(ctx, TargetDeckGeometry.Line(i), v.Rows[i]);
        for (int i = 0; i < PluginRowCount(v); i++)
            DrawPluginRow(ctx, PluginRowRect(v, i), v.PluginRows[i]);
        DrawStuds(ctx, v);
    }

    // ---- faces ----

    private DeckFace Small => new(_fonts?.Small);
    /// <summary>The name: the 14 px SemiBold face.</summary>
    private DeckFace NameFace => new(_fonts?.Bold);
    private DeckFace ChipFace => new(_fonts?.Face(ChipPixels, BundledUiFontWeight.SemiBold));

    /// <summary>A name shortened with "..." to fit <paramref name="width"/>; the same helper as the Command Deck's casting line.</summary>
    internal static string FitName(string text, Func<string, float> measure, float width) =>
        UiCommandDeck.FitCastingText(text, measure, width);

    private static string Fit(UiRenderContext ctx, DeckFace face, string text, float width) =>
        FitName(text, t => face.Measure(ctx, t), width);

    /// <summary>Text from <paramref name="x"/>, centred on <paramref name="band"/>'s height and kept inside the plate's frame.</summary>
    private static void DrawText(UiRenderContext ctx, string text, float x, DeckRect band, Vector4 ink, DeckFace face)
    {
        if (text.Length == 0) return;
        float lh = face.LineHeight(ctx);
        float y = band.Y + (band.H - lh) / 2f;
        if (band.Y < TargetDeckGeometry.Height) y = MathF.Min(y, TargetDeckGeometry.Height - 2f - lh);
        face.Draw(ctx, text, x, y, ink);
    }

    // ---- identity ----

    private void DrawPortrait(UiRenderContext ctx, TargetPlateModel v)
    {
        DeckRect p = TargetDeckGeometry.Portrait;
        ctx.FillEllipse(p.X - 1f, p.Y - 1f, p.W + 2f, p.H + 2f, FrameEdge);
        ctx.FillEllipse(p.X, p.Y, p.W, p.H, BronzeSolid);
        ctx.FillEllipse(p.X + 2f, p.Y + 2f, p.W - 4f, p.H - 4f, FrameEdge);
        if (_icons is not null)
        {
            DrawIcon(ctx, KindIcon(v.Kind), p.X + (p.W - PortraitIconSize) / 2f, p.Y + (p.H - PortraitIconSize) / 2f,
                PortraitIconSize, LabelInk);
        }
        else
        {
            string glyph = v.Kind.ToString()[..1];
            DrawText(ctx, glyph, p.X + (p.W - NameFace.Measure(ctx, glyph)) / 2f, p, LabelInk, NameFace);
        }
    }

    /// <summary>The name line: the level pill and chips right-aligned, the name shortened into what is left.</summary>
    private void DrawIdentity(UiRenderContext ctx, TargetPlateModel v)
    {
        DeckRect n = TargetDeckGeometry.Name;
        DrawClose(ctx);
        float right = TargetDeckGeometry.Close.X - ChipGap;
        DeckFace chipFace = ChipFace;

        if (v.Level is int level)
        {
            string text = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Vector4? pill = v.Con switch
            {
                TargetCon.Higher => StudOn,
                TargetCon.Even => ChipAmber,
                TargetCon.Lower => ConBlue,
                _ => null,
            };
            float w = chipFace.Measure(ctx, text) + 8f;
            right -= w;
            if (pill is { } ink)
            {
                DrawChip(ctx, right, n, w, ink, text, chipFace);
            }
            else
            {
                DrawText(ctx, text, right + 4f, n, LabelInk, Small);
            }
            right -= ChipGap;
        }

        for (int i = v.Chips.Count - 1; i >= 0; i--)
        {
            if (ChipStyle(v.Chips[i]) is not { } chip) continue;
            float w = chipFace.Measure(ctx, chip.Text) + 8f;
            if (right - w < n.X + 40f) break;   // keep room for the name
            right -= w;
            DrawChip(ctx, right, n, w, chip.Ink, chip.Text, chipFace);
            right -= ChipGap;
        }

        DeckFace face = NameFace;
        DrawText(ctx, Fit(ctx, face, v.Name, right - n.X), n.X, n, TextInk, face);
    }

    /// <summary>The deselect cross: two strokes, brighter on a soft backing while hovered.</summary>
    private void DrawClose(UiRenderContext ctx)
    {
        DeckRect c = TargetDeckGeometry.Close;
        bool hot = _hover == TargetZone.Close;
        if (hot) ctx.FillRoundedRect(c.X, c.Y, c.W, c.H, 3f, LabelInk with { W = 0.25f });
        Vector4 ink = hot ? TextInk : LabelInk;
        const float inset = 4f;
        ctx.DrawLine(c.X + inset, c.Y + inset, c.X + c.W - inset, c.Y + c.H - inset, ink, 1.5f);
        ctx.DrawLine(c.X + c.W - inset, c.Y + inset, c.X + inset, c.Y + c.H - inset, ink, 1.5f);
    }

    private static (string Text, Vector4 Ink)? ChipStyle(TargetChip chip) => chip switch
    {
        TargetChip.Yours => ("YOURS", Handle),
        TargetChip.Fellow => ("FELLOW", Handle),
        TargetChip.Pk => ("PK", StudOn),
        TargetChip.Npk => ("NPK", JumpInk),
        TargetChip.PkLite => ("PKL", ChipAmber),
        _ => null,
    };

    private static void DrawChip(UiRenderContext ctx, float x, DeckRect band, float w, Vector4 ink, string text, DeckFace face)
    {
        float y = band.Y + (band.H - ChipHeight) / 2f;
        ctx.FillRoundedRect(x, y, w, ChipHeight, 3f, ink);
        DrawText(ctx, text, x + 4f, new DeckRect(x, y, w, ChipHeight), FrameBottom, face);
    }

    // ---- vitals and rows ----

    private void DrawVitals(UiRenderContext ctx, TargetVitals vitals)
    {
        DeckRect h = TargetDeckGeometry.Health;
        DrawBar(ctx, h, vitals.Health, HealthInk, 1f, HealthText(vitals));

        // Health is live; stamina and mana come from the last assess, kept dimmed when it is old or failed.
        float alpha = vitals.State is TargetVitalsState.Stale or TargetVitalsState.Failed ? DimAlpha : 1f;
        DrawBar(ctx, TargetDeckGeometry.Stamina, vitals.Stamina ?? 0f, StaminaInk, alpha, StaminaText(vitals));
        DrawBar(ctx, TargetDeckGeometry.Mana, vitals.Mana ?? 0f, ManaInk, alpha, ManaText(vitals));
    }

    private void DrawBar(UiRenderContext ctx, DeckRect r, float fraction, (Vector4 Top, Vector4 Bottom) ink, float alpha, string? text)
    {
        DeckChrome.DrawGauge(ctx, r, fraction, ink, alpha);
        if (text is null) return;
        ctx.PushAlpha(alpha);
        try { DrawText(ctx, text, r.X + (r.W - Small.Measure(ctx, text)) / 2f, r, TextInk, Small); }
        finally { ctx.PopAlpha(); }
    }

    private void DrawRow(UiRenderContext ctx, DeckRect r, TargetRow row)
    {
        ctx.PushAlpha(row.Dim ? DimAlpha : 1f);
        try { DrawLabelValue(ctx, r.X, r, row.Label, row.Value, TextInk); }
        finally { ctx.PopAlpha(); }
    }

    private void DrawPluginRow(UiRenderContext ctx, DeckRect r, TargetPluginRow row)
    {
        ctx.DrawFill(r.X, r.Y + 1f, 2f, r.H - 2f, JumpInk);
        DrawLabelValue(ctx, r.X + 6f, r, row.Label, row.Value, row.Rgba is uint rgba ? RgbaInk(rgba) : TextInk);
    }

    private void DrawLabelValue(UiRenderContext ctx, float x, DeckRect r, string label, string value, Vector4 valueInk)
    {
        DeckFace face = Small;
        float right = r.X + r.W;
        if (label.Length > 0)
        {
            string fitted = Fit(ctx, face, label, (right - x) / 2f);
            DrawText(ctx, fitted, x, r, LabelInk, face);
            x += face.Measure(ctx, fitted) + 6f;
        }
        DrawText(ctx, Fit(ctx, face, value, right - x), x, r, valueInk, face);
    }

    /// <summary>A plugin colour packed as 0xRRGGBBAA.</summary>
    internal static Vector4 RgbaInk(uint rgba) =>
        new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);

    // ---- studs and menu ----

    private void DrawStuds(UiRenderContext ctx, TargetPlateModel v)
    {
        for (int i = 0; i < Math.Min(v.Studs.Count, TargetDeckGeometry.StudCount); i++)
        {
            TargetStud stud = v.Studs[i];
            DeckRect r = TargetDeckGeometry.Stud(i);
            bool lit = i == 0 || (stud.Action == TargetAction.More && _menuOpen);
            Vector4 ink = DeckChrome.DrawStud(ctx, r, 1f, on: lit, dim: !stud.Enabled);
            if (_icons is not null)
            {
                DrawIcon(ctx, IconFor(stud.Action), r.X + (r.W - IconSize) / 2f, r.Y + (r.H - IconSize) / 2f, IconSize, ink);
            }
            else
            {
                string letter = stud.Action == TargetAction.More ? "..." : LabelFor(stud.Action)[..1];
                DrawText(ctx, letter, r.X + (r.W - Small.Measure(ctx, letter)) / 2f, r, ink, Small);
            }
        }
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    /// <summary>The open ⋯ menu below the plate: the deck's frame, one line per entry,
    /// plugin names as headings over their actions.</summary>
    private void DrawMenu(UiRenderContext ctx, TargetPlateModel v)
    {
        List<TargetMenuEntry> entries = Entries();
        DeckRect m = MenuRect(entries.Count);
        ctx.PushAlphaAbsolute(1f);
        try
        {
            DeckChrome.DrawFrame(ctx, m, 1f);
            DeckFace face = Small, header = new(_fonts?.At(1f).SmallBold);
            for (int i = 0; i < entries.Count; i++)
            {
                TargetMenuEntry e = entries[i];
                DeckRect r = MenuEntryRect(i);
                if (i == _menuHover && !e.Header && e.Enabled)
                    ctx.FillRoundedRect(r.X + 4f, r.Y, r.W - 8f, r.H, 3f, MenuHighlight);
                Vector4 ink = e.Header ? LabelInk : e.Enabled ? TextInk : StudOffInk with { W = 0.5f };
                float x = r.X + (e.Header ? 10f : 16f);
                DrawText(ctx, Fit(ctx, e.Header ? header : face, e.Label, r.X + r.W - 10f - x), x, r, ink,
                    e.Header ? header : face);
            }
        }
        finally { ctx.PopAlpha(); }
    }
}
