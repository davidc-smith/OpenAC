using System.Numerics;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private const float StripBadgePixels = 10f;
    private const float StripBadgeHeight = 13f;
    private const float PeekMaxWidth = 420f;
    private const float PeekGap = 8f;
    private const float PeekPadX = 6f;

    private readonly ChatDeckDrawer _drawer = new();
    private readonly ChatDeckPeekFeed _peek;
    private ChatDeckStripGeometry _strip = ChatDeckStripGeometry.Create(ChatDeckGeometry.MinHeight, 0);

    /// <summary>The collapsed strip: a plain child of the interface root, placed by the deck.</summary>
    internal UiElement Strip { get; private set; } = null!;
    /// <summary>The peek lines: a click-through child of an overlay layer, placed by the deck.</summary>
    internal UiElement Peek { get; private set; } = null!;
    internal ChatDeckStripGeometry StripGeometry => _strip;
    internal ChatDeckDrawer Drawer => _drawer;
    internal bool Pinned => _drawer.Pinned;
    internal IReadOnlyList<ChatDeckPeekFeed.Entry> PeekLines => _peek.Lines;

    private void CreateStrip()
    {
        // Placed by the deck, so no anchors: the root's anchor pass would otherwise
        // pin both to the 0×0 they had on the first frame laid out (see Place).
        Strip = new StripElement(this) { Visible = false, Anchors = AnchorEdges.None };
        Peek = new PeekElement(this) { Visible = false, Anchors = AnchorEdges.None };
    }

    /// <summary>Lays the strip's studs out for its current height and the current tabs.</summary>
    internal void LayOutStrip() => _strip = ChatDeckStripGeometry.Create(Strip.Height, _labels.Count);

    internal string? StripTooltipAt(float x, float y)
    {
        ChatDeckHit hit = _strip.HitAt(x, y);
        if (hit.Zone == ChatDeckZone.PinStud) return "Pin the chat";
        if (hit.Zone != ChatDeckZone.Tab || _model is null || hit.Tab >= _model.Settings.Tabs.Count) return null;
        return _model.Settings.Tabs[hit.Tab].Name;
    }

    private void PressStrip(ChatDeckHit hit)
    {
        if (hit.Zone == ChatDeckZone.Tab) PressStripTab(hit.Tab);
        else if (hit.Zone == ChatDeckZone.PinStud) PressStripPin();
    }

    private void DrawStrip(UiRenderContext ctx)
    {
        DeckRect f = _strip.Frame;
        float opacity = _model?.Settings.Appearance.BackgroundOpacity ?? new ChatDeckAppearance().BackgroundOpacity;
        // Square on the screen edge, rounded on the inside.
        var radii = _drawer.Edge == ChatDeckEdge.Left
            ? new CanvasCornerRadii(0f, 8f, 8f, 0f)
            : new CanvasCornerRadii(8f, 0f, 0f, 8f);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, radii,
            ChatDeckPalette.FrameTop with { W = opacity }, ChatDeckPalette.FrameBottom with { W = opacity });

        if (_model is { } model)
        {
            IReadOnlyList<ChatDeckTab> tabs = model.Settings.Tabs;
            for (int i = 0; i < _strip.Studs.Count && i < tabs.Count; i++)
            {
                DeckRect r = _strip.Studs[i];
                if (r.W <= 0) continue;
                bool selected = tabs[i].Id == model.SelectedTabId;
                ctx.FillRoundedRect(r.X - 1f, r.Y - 1f, r.W + 2f, r.H + 2f, 6f, ChatDeckPalette.FrameEdge);
                ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, selected ? ChatDeckPalette.TabSelected : ChatDeckPalette.StudOff);
                DrawIcon(ctx, ChatDeckIcons.Name(tabs[i].Icon), r.X + (r.W - 14f) / 2f, r.Y + (r.H - 14f) / 2f, 14f,
                    selected ? ChatDeckPalette.TextInk : ChatDeckPalette.StudOffInk);
                DrawStripBadge(ctx, r, i);
            }
        }
        DrawStud(ctx, _strip.PinStud, HudIcons.Expand, on: false);
    }

    private void DrawStripBadge(UiRenderContext ctx, DeckRect stud, int index)
    {
        string badge = index < _labels.Count ? _labels[index].Badge : "";
        if (badge.Length == 0 || _model is null) return;
        var face = new DeckFace(_fonts?.Face(StripBadgePixels, BundledUiFontWeight.SemiBold));
        float textW = face.Measure(ctx, badge);
        float w = MathF.Max(StripBadgeHeight, textW + 4f);
        var pill = new DeckRect(stud.X + stud.W - w + 6f, stud.Y - 5f, w, StripBadgeHeight);
        bool alert = _model.FindTab(_model.Settings.Tabs[index].Id)?.Alert == true;
        ctx.FillRoundedRect(pill.X, pill.Y, pill.W, pill.H, pill.H / 2f,
            alert ? ChatDeckPalette.BadgeRed : ChatDeckPalette.BadgeGold);
        DrawLabel(ctx, face, badge, pill.X + (pill.W - textW) / 2f, pill,
            alert ? ChatDeckPalette.TextInk : ChatDeckPalette.BadgeInk);
    }

    internal static float PeekRowHeight(ChatDeckAppearance appearance) => appearance.FontSize + 10f;

    /// <summary>Beside the strip, its bottom lined up with the strip's.</summary>
    private void PlacePeek(UiRoot root, DeckRect strip)
    {
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        float w = MathF.Min(PeekMaxWidth, MathF.Max(0f, root.Width - ChatDeckStripGeometry.Width - 2 * PeekGap));
        float h = ChatDeckPeekFeed.MaxLines * PeekRowHeight(appearance);
        Peek.Width = w;
        Peek.Height = h;
        Peek.Left = _drawer.Edge == ChatDeckEdge.Left ? strip.X + strip.W + PeekGap : strip.X - PeekGap - w;
        Peek.Top = MathF.Max(0f, strip.Y + strip.H - h - PeekGap);
    }

    private void DrawPeek(UiRenderContext ctx, float width, float height)
    {
        IReadOnlyList<ChatDeckPeekFeed.Entry> lines = _peek.Lines;
        if (lines.Count == 0) return;
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        var face = new DeckFace(_fonts?.Face(appearance.FontSize, BundledUiFontWeight.Regular));
        float row = PeekRowHeight(appearance);
        float y = height;
        for (int i = lines.Count - 1; i >= 0; i--)   // newest at the bottom
        {
            y -= row;
            float alpha = ChatDeckPeekFeed.Alpha(lines[i].Age);
            string text = Fit(ctx, face, lines[i].Text, width - 2 * PeekPadX);
            float w = face.Measure(ctx, text) + 2 * PeekPadX;
            float x = _drawer.Edge == ChatDeckEdge.Left ? 0f : width - w;
            ctx.FillRoundedRect(x, y + 1f, w, row - 2f, 5f, ChatDeckPalette.FieldFill with { W = 0.75f * alpha });
            Vector4 ink = lines[i].Line.LineColour is uint argb ? ChatDeckTranscript.Argb(argb) : ChannelColour(lines[i].Line, appearance);
            DrawLabel(ctx, face, text, x + PeekPadX, new DeckRect(x, y, w, row), ink with { W = ink.W * alpha });
        }
    }

    /// <summary>The strip: presses on its studs, a tooltip naming the tab under the pointer.</summary>
    private sealed class StripElement : UiElement
    {
        private readonly UiChatDeck _deck;
        private float _hoverX = -1f;
        private float _hoverY = -1f;
        private ChatDeckHit _tipFor;

        public StripElement(UiChatDeck deck)
        {
            _deck = deck;
            RuntimeTooltipTextSource = () => _deck.StripTooltipAt(_hoverX, _hoverY);
        }

        public override bool HandlesClick => true;
        public override bool ReceivesHoverMouseMove => true;

        protected override bool OnHitTest(float localX, float localY)
        {
            // A press anywhere on the strip is the strip's, never a window or world drag.
            CapturesPointerDrag = true;
            return _deck._strip.Frame.Contains(localX, localY);
        }

        public override bool OnEvent(in UiEvent e)
        {
            switch (e.Type)
            {
                case UiEventType.MouseDown:
                    _deck.PressStrip(_deck._strip.HitAt(e.Data1, e.Data2));
                    return true;
                case UiEventType.Click:
                    return true;
                case UiEventType.HoverEnter:
                case UiEventType.MouseMove:
                    _hoverX = e.Data1;
                    _hoverY = e.Data2;
                    ChatDeckHit hit = _deck._strip.HitAt(_hoverX, _hoverY);
                    if (hit != _tipFor)
                    {
                        _tipFor = hit;
                        NotifyTooltipTextChanged();
                    }
                    return false;
                case UiEventType.HoverLeave:
                    _hoverX = _hoverY = -1f;
                    _tipFor = default;
                    return false;
                default:
                    return false;
            }
        }

        protected override void OnDraw(UiRenderContext ctx) => _deck.DrawStrip(ctx);
    }

    /// <summary>The peek lines; never takes input.</summary>
    private sealed class PeekElement(UiChatDeck deck) : UiElement
    {
        protected override bool OnHitTest(float localX, float localY) => false;

        protected override void OnDraw(UiRenderContext ctx) => deck.DrawPeek(ctx, Width, Height);
    }
}
