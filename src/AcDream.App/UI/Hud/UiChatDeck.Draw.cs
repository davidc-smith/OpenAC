using System.Numerics;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private DeckFace LabelFace => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.Regular));
    private DeckFace LabelBold => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.SemiBold));

    protected override void OnDraw(UiRenderContext ctx)
    {
        float opacity = _model?.Settings.Appearance.BackgroundOpacity ?? new ChatDeckAppearance().BackgroundOpacity;
        ChatDeckPalette.DrawFrame(ctx, _geometry.Frame, opacity * _fade.Factor);
        DrawTabs(ctx);
        DrawStud(ctx, _geometry.SearchStud, HudIcons.Search, _searchOpen);
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
            float badgeW = ChatDeckGeometry.BadgeWidth(badge, s => LabelFace.Measure(ctx, s));
            float nameRoom = r.W - 2 * ChatDeckGeometry.TabPadX - (badge.Length > 0 ? badgeW + ChatDeckGeometry.BadgeGap : 0f);
            string name = Fit(ctx, LabelFace, tabs[i].Name, nameRoom);
            DrawLabel(ctx, LabelFace, name, r.X + ChatDeckGeometry.TabPadX, r,
                selected ? ChatDeckPalette.TextInk : ChatDeckPalette.LabelInk);

            if (badge.Length == 0) continue;
            bool alert = _model.FindTab(tabs[i].Id)?.Alert == true;
            var pill = new DeckRect(r.X + r.W - ChatDeckGeometry.TabPadX - badgeW,
                r.Y + (r.H - ChatDeckGeometry.BadgeHeight) / 2f, badgeW, ChatDeckGeometry.BadgeHeight);
            ctx.FillRoundedRect(pill.X, pill.Y, pill.W, pill.H, pill.H / 2f,
                alert ? ChatDeckPalette.BadgeRed : ChatDeckPalette.BadgeGold);
            DrawLabel(ctx, LabelBold, badge, pill.X + ChatDeckGeometry.BadgePadX, pill,
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
        DrawLabel(ctx, LabelFace, $"{NewLineCount} new", 22f, new DeckRect(0f, 0f, w, h), ChatDeckPalette.TextInk);
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
