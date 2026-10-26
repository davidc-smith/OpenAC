using System.Numerics;
using static AcDream.App.UI.Hud.DeckChrome;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiSpellDeck
{
    /// <summary>A length in base points at the deck's scale.</summary>
    private float P(float points) => points * _geometry.Scale;

    /// <summary>A line weight at the deck's scale, in whole points, never under one.</summary>
    private float Line(float points) => DeckChrome.Line(points, _geometry.Scale);

    protected override void OnDraw(UiRenderContext ctx)
    {
        SpellDeckGeometry g = _geometry;
        DeckChrome.DrawFrame(ctx, g.Frame, g.Scale);
        DeckRect d = g.Divider;
        ctx.DrawFill(d.X, d.Y, d.W, d.H, Bronze);

        DrawItemSocket(ctx);
        uint? pending = _pendingSpell();
        SpellDeckOutline outline = _controller.Outline;
        for (int row = 0; row < g.Rows && row < _controller.Rows.Count; row++)
        {
            SpellDeckRow r = _controller.Rows[row];
            DeckRect chip = g.TabChip(row);
            Vector4 ink = DeckChrome.DrawStud(ctx, chip, g.Scale, on: false);
            DrawTextCentered(ctx, (r.Tab + 1).ToString(), chip.X + chip.W / 2, chip, ink);

            IReadOnlyList<uint> spells = _controller.Visible(r.Tab);
            for (int slot = 0; slot < g.SlotsPerRow; slot++)
            {
                DeckRect s = g.Slot(row, slot);
                if (slot < spells.Count)
                    ctx.DrawSprite(_spellIcon(spells[slot]), s.X, s.Y, s.W, s.H, 0, 0, 1, 1, Vector4.One);
                else
                    ctx.FillRoundedRect(s.X, s.Y, s.W, s.H, P(4), Track);

                string key = KeyLabel(row, slot);
                if (key.Length > 0)
                {
                    DeckFace face = Face(small: true);
                    face.Draw(ctx, key, s.X + P(2), s.Y + P(2), LabelInk);
                }

                if (slot >= spells.Count) continue;
                uint id = spells[slot];
                if (outline.Kind == SpellDeckOutlineKind.Slot && outline.RowId == r.Id && outline.SpellId == id)
                    StrokeAround(ctx, s, gap: 1f, width: 2f, Handle);
                if (pending == id)
                    StrokeAround(ctx, s, gap: 3f, width: 2f, CastLit with { W = 0.8f });
            }
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
        if (_controller.Outline.Kind == SpellDeckOutlineKind.Item)
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
