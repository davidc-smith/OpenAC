using System.Numerics;
using static AcDream.App.UI.Hud.DeckChrome;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiSpellDeck
{
    private static readonly Vector4 DropAccept = Rgb(0x6FD38A);
    private static readonly Vector4 DropReject = Rgb(0xE0505E);
    /// <summary>The shown tab's bookmark: raised and lit.</summary>
    internal static readonly Vector4 TabLit = Rgb(0x5C4B2C);
    /// <summary>The other bookmarks and the <c>+</c>'s backing.</summary>
    internal static readonly Vector4 TabOff = Rgb(0x2F2D36);
    /// <summary>The lit bookmark's top edge.</summary>
    private static readonly Vector4 TabAccent = Rgb(0xEACB84);
    /// <summary>The <c>+</c> sign.</summary>
    internal static readonly Vector4 PlusInk = Rgb(0xB9AE93);
    /// <summary>How far an unlit bookmark sits below the lit one, in base points.</summary>
    private const float BookmarkDrop = 3f;

    /// <summary>A length in base points at the deck's scale.</summary>
    private float P(float points) => points * _geometry.Scale;

    /// <summary>A line weight at the deck's scale, in whole points, never under one.</summary>
    private float Line(float points) => DeckChrome.Line(points, _geometry.Scale);

    protected override void OnDraw(UiRenderContext ctx)
    {
        SpellDeckGeometry g = _geometry;
        DeckChrome.DrawFrame(ctx, g.Frame, g.Scale, squareBottom: SquareBottomCorners);
        DeckRect d = g.Divider;
        ctx.DrawFill(d.X, d.Y, d.W, d.H, Bronze);

        DrawItemSocket(ctx);
        DrawTabStrip(ctx);
        uint? pending = _pendingSpell();
        SpellDeckMark outline = _controller.Outline;
        int tab = _controller.Tab;
        for (int row = 0; row < g.Rows && row < _controller.RowCount; row++)
        {
            for (int slot = 0; slot < g.SlotsPerRow; slot++)
            {
                DeckRect s = g.Slot(row, slot);
                uint? spell = _controller.SlotSpell(row, slot);
                if (spell is uint shown)
                    ctx.DrawSprite(_spellIcon(shown), s.X, s.Y, s.W, s.H, 0, 0, 1, 1, Vector4.One);
                else
                    ctx.FillRoundedRect(s.X, s.Y, s.W, s.H, P(4), Track);

                string key = KeyLabel(row, slot);
                if (key.Length > 0)
                {
                    DeckFace face = Face(small: true);
                    face.Draw(ctx, key, s.X + P(2), s.Y + P(2), LabelInk);
                }

                if (spell is not uint id) continue;
                if (outline == SpellDeckMark.Slot(tab, row, slot))
                    StrokeAround(ctx, s, gap: 1f, width: 2f, Handle);
                if (pending == id)
                    StrokeAround(ctx, s, gap: 3f, width: 2f, CastLit with { W = 0.8f });
            }
        }

        if (DropHighlight is { } drop && drop.Row < g.Rows)
            StrokeAround(ctx, g.Slot(drop.Row, drop.Slot), gap: 1f, width: 2f, drop.Accept ? DropAccept : DropReject);

        if (BookmarkDropHighlight is { } mark && mark.Tab < _controller.TabCount)
        {
            DeckRect b = g.Bookmark(mark.Tab, _controller.TabCount);
            StrokeAround(ctx, b, gap: 1f, width: 2f, mark.Accept ? DropAccept : DropReject);
        }

        if (!_controller.Attached)
            DeckChrome.DrawResizeGrip(ctx, FindRoot()?.ResizeEdgesFor(this) ?? ResizeEdges.None, g.Frame, g.Scale);
    }

    /// <summary>A rounded stroke <paramref name="width"/> wide whose inner edge is <paramref name="gap"/> outside <paramref name="r"/>.</summary>
    private void StrokeAround(UiRenderContext ctx, DeckRect r, float gap, float width, Vector4 ink)
    {
        float w = Line(width), grow = P(gap) + w / 2f;
        ctx.StrokeRoundedRect(r.X - grow, r.Y - grow, r.W + 2f * grow, r.H + 2f * grow, P(4) + grow, ink, w);
    }

    /// <summary>The bookmarks, numbered from 1, the shown one raised and lit; then the <c>+</c>
    /// (not at 16 tabs). A bronze rule under them joins the strip to the rows.</summary>
    private void DrawTabStrip(UiRenderContext ctx)
    {
        SpellDeckGeometry g = _geometry;
        int count = _controller.TabCount, shown = _controller.Tab;
        DeckRect band = g.TabStripBand;
        ctx.DrawFill(band.X, band.Y + band.H - Line(1), band.W, Line(1), Bronze);
        for (int tab = 0; tab < count; tab++)
        {
            DeckRect b = g.Bookmark(tab, count);
            bool lit = tab == shown;
            DeckRect face = lit ? b : Lowered(b);
            ctx.FillRoundedRect(face.X, face.Y, face.W, face.H, new CanvasCornerRadii(P(4), P(4), 0f, 0f), lit ? TabLit : TabOff);
            if (lit) ctx.DrawFill(face.X + P(3), face.Y, face.W - P(6), Line(1), TabAccent);
            DrawTextCentered(ctx, (tab + 1).ToString(), face.X + face.W / 2f, face, lit ? TextInk : StudOffInk);
        }
        if (!SpellDeckGeometry.ShowsPlus(count)) return;

        DeckRect p = Lowered(g.Plus(count));
        ctx.FillRoundedRect(p.X, p.Y, p.W, p.H, new CanvasCornerRadii(P(4), P(4), 0f, 0f), TabOff with { W = 0.6f });
        float arm = MathF.Min(p.W, p.H) * 0.5f, bar = Line(1.5f);
        float cx = p.X + p.W / 2f, cy = p.Y + p.H / 2f;
        ctx.FillRoundedRect(cx - arm / 2f, cy - bar / 2f, arm, bar, 0f, PlusInk);
        ctx.FillRoundedRect(cx - bar / 2f, cy - arm / 2f, bar, arm, 0f, PlusInk);
    }

    /// <summary>An unlit bookmark: the same rectangle, its top lowered.</summary>
    private DeckRect Lowered(DeckRect b) => new(b.X, b.Y + P(BookmarkDrop), b.W, b.H - P(BookmarkDrop));

    /// <summary>The round item socket: gold-rimmed with the item's spell icon, or a dim empty socket.</summary>
    private void DrawItemSocket(UiRenderContext ctx)
    {
        DeckRect s = _geometry.ItemSlot;
        float radius = s.W / 2f;
        if (_controller.ItemSpell is not { } item)
        {
            ctx.FillRoundedRect(s.X, s.Y, s.W, s.H, radius, Track with { W = 0.6f });
            ctx.StrokeRoundedRect(s.X, s.Y, s.W, s.H, radius, Bronze with { W = 0.35f }, Line(1));
            return;
        }

        ctx.FillRoundedRect(s.X, s.Y, s.W, s.H, radius, Track);
        // No round clip in the context: the square icon sits inside the circle.
        float side = s.W * 0.7f, inset = (s.W - side) / 2f;
        ctx.DrawSprite(_spellIcon(item.SpellId), s.X + inset, s.Y + inset, side, side, 0, 0, 1, 1, Vector4.One);
        float rim = Line(1.5f);
        ctx.StrokeRoundedRect(s.X, s.Y, s.W, s.H, radius, Handle, rim);
        if (_controller.Outline.Kind == SpellDeckMarkKind.Item)
        {
            float w = Line(2), grow = P(1) + rim / 2f + w / 2f;
            ctx.StrokeEllipse(s.X - grow, s.Y - grow, s.W + 2f * grow, s.H + 2f * grow, Handle, w);
        }
    }

    private DeckFace Face(bool small) => DeckFace.For(_fonts, _geometry.Scale, small);

    /// <summary>Small text centred on <paramref name="centre"/> and on <paramref name="band"/>'s height.</summary>
    private void DrawTextCentered(UiRenderContext ctx, string text, float centre, DeckRect band, Vector4 ink)
    {
        DeckFace face = Face(small: true);
        face.Draw(ctx, text, centre - face.Measure(ctx, text) / 2f, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);
    }
}
