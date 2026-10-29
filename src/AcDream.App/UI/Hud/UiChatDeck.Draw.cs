using System.Numerics;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    /// <summary>The label face for text in <paramref name="box"/>: the deck's text size, dropped to the largest that fits.</summary>
    private DeckFace LabelIn(DeckRect box) =>
        DeckFace.In(_fonts, LabelPixels, BundledUiFontWeight.Regular, 1f, TextFactor, box.H);

    private DeckFace LabelBoldIn(DeckRect box) =>
        DeckFace.In(_fonts, LabelPixels, BundledUiFontWeight.SemiBold, 1f, TextFactor, box.H);

    /// <summary>The label face at the deck's own size: badge widths are measured with it, so badges keep their size.</summary>
    private DeckFace LabelBase => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.Regular));

    private DeckFace LabelBoldBase => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.SemiBold));

    protected override void OnDraw(UiRenderContext ctx)
    {
        float opacity = _model?.Settings.Appearance.BackgroundOpacity ?? new ChatDeckAppearance().BackgroundOpacity;
        ChatDeckPalette.DrawFrame(ctx, _geometry.Frame, opacity * _fade.Factor);
        DrawTabs(ctx);
        DrawStud(ctx, _geometry.SearchStud, HudIcons.Search, _searchOpen);
        DrawStud(ctx, _geometry.FiltersStud, HudIcons.Filters, _b.EditorOpen?.Invoke() == true);
        if (_geometry.AddTab.W > 0)
        {
            DeckRect add = _geometry.AddTab;
            ctx.FillRoundedRect(add.X, add.Y, add.W, add.H, 5f, ChatDeckPalette.StudOff);
            DeckFace plus = LabelBoldIn(add);
            float w = plus.Measure(ctx, "+");
            DrawLabel(ctx, plus, "+", add.X + (add.W - w) / 2f, add, ChatDeckPalette.LabelInk);
        }
        DrawStud(ctx, _geometry.SettingsStud, HudIcons.Settings, _b.SettingsOpen?.Invoke() == true);
        DrawStud(ctx, _geometry.CollapseStud, HudIcons.Collapse, on: false);
        if (_searchOpen) ctx.FillRoundedRect(_geometry.SearchField.X, _geometry.SearchField.Y,
            _geometry.SearchField.W, _geometry.SearchField.H, 4f, ChatDeckPalette.FieldFill);
        DrawInputRow(ctx);        // UiChatDeck.Input.cs
    }

    private void DrawTabs(UiRenderContext ctx)
    {
        if (_model is null) return;
        IReadOnlyList<ChatDeckTab> tabs = _model.Settings.Tabs;
        for (int i = 0; i < _geometry.Tabs.Count && i < tabs.Count; i++)
        {
            DeckRect r = _geometry.Tabs[i];
            if (r.W <= 0) continue;
            bool selected = tabs[i].Id == _model.SelectedTabId;
            if (selected)
            {
                ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, ChatDeckPalette.TabSelected);
                ctx.DrawFill(r.X + 4f, r.Y + r.H - 2f, r.W - 8f, 2f, ChatDeckPalette.TabUnderline);
            }

            string badge = _labels[i].Badge;
            float badgeW = ChatDeckGeometry.BadgeWidth(badge, s => LabelBase.Measure(ctx, s));
            float nameRoom = r.W - 2 * ChatDeckGeometry.TabPadX - (badge.Length > 0 ? badgeW + ChatDeckGeometry.BadgeGap : 0f);
            DeckFace nameFace = LabelIn(r);
            string name = Fit(ctx, nameFace, tabs[i].Name, nameRoom);
            DrawLabel(ctx, nameFace, name, r.X + ChatDeckGeometry.TabPadX, r,
                selected ? ChatDeckPalette.TextInk : ChatDeckPalette.LabelInk);

            if (badge.Length == 0) continue;
            bool alert = _model.FindTab(tabs[i].Id)?.Alert == true;
            var pill = new DeckRect(r.X + r.W - ChatDeckGeometry.TabPadX - badgeW,
                r.Y + (r.H - ChatDeckGeometry.BadgeHeight) / 2f, badgeW, ChatDeckGeometry.BadgeHeight);
            ctx.FillRoundedRect(pill.X, pill.Y, pill.W, pill.H, pill.H / 2f,
                alert ? ChatDeckPalette.BadgeRed : ChatDeckPalette.BadgeGold);
            DeckFace badgeFace = LabelBoldIn(pill);
            // The pill is sized with the Regular face and drawn SemiBold, so the room never drops below
            // the deck's own bold badge: at 100% the badge draws whole, as it always has.
            float badgeRoom = MathF.Max(pill.W - 2 * ChatDeckGeometry.BadgePadX, LabelBoldBase.Measure(ctx, badge));
            DrawLabel(ctx, badgeFace, Fit(ctx, badgeFace, badge, badgeRoom),
                pill.X + ChatDeckGeometry.BadgePadX, pill,
                alert ? ChatDeckPalette.TextInk : ChatDeckPalette.BadgeInk);
        }
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, string icon, bool on)
    {
        ctx.FillRoundedRect(r.X - 1f, r.Y - 1f, r.W + 2f, r.H + 2f, 6f, ChatDeckPalette.FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, on ? ChatDeckPalette.StudOn : ChatDeckPalette.StudOff);
        DrawIcon(ctx, icon, r.X + (r.W - 14f) / 2f, r.Y + (r.H - 14f) / 2f, 14f,
            on ? ChatDeckPalette.TextInk : ChatDeckPalette.StudOffInk);
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    private void DrawNewLines(UiRenderContext ctx, float w, float h)
    {
        ctx.FillRoundedRect(0f, 0f, w, h, h / 2f, ChatDeckPalette.MarkerFill);
        DrawIcon(ctx, HudIcons.ChevronDown, 8f, (h - 10f) / 2f, 10f, ChatDeckPalette.TabUnderline);
        var band = new DeckRect(0f, 0f, w, h);
        DeckFace face = LabelIn(band);
        DrawLabel(ctx, face, Fit(ctx, face, $"{NewLineCount} new", w - 26f), 22f, band, ChatDeckPalette.TextInk);
    }

    private static void DrawLabel(UiRenderContext ctx, DeckFace face, string text, float x, DeckRect band, Vector4 ink) =>
        face.Draw(ctx, text, x, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);

    /// <summary>The text cut to fit <paramref name="room"/>, ending in "…" when cut.</summary>
    private static string Fit(UiRenderContext ctx, DeckFace face, string text, float room)
    {
        if (face.Measure(ctx, text) <= room) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            string cut = text[..n] + "…";
            if (face.Measure(ctx, cut) <= room) return cut;
        }
        return "";
    }
}
