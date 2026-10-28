using System.Numerics;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using static AcDream.App.UI.Hud.DeckChrome;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiGearDeck
{
    /// <summary>An equipment slot, worn cell or side-pack cell (the mockup's <c>.slot</c> / <c>.pk</c>).</summary>
    internal static readonly Vector4 SlotFill = Rgb(0x0E0D12);
    private static readonly Vector4 SlotEdge = Rgb(0x3B3542);
    /// <summary>A pack item cell (the mockup's <c>.it</c>).</summary>
    internal static readonly Vector4 CellFill = Rgb(0x16151B);
    private static readonly Vector4 CellEdge = Rgb(0x2C2933);
    /// <summary>The rule under each list row.</summary>
    internal static readonly Vector4 RowRule = Rgb(0x24222A);
    /// <summary>The Equipment and Packs section headers' backing.</summary>
    private static readonly Vector4 HeaderFill = Rgb(0x24222B);
    private static readonly Vector4 DropAccept = Rgb(0x6FD38A);
    private static readonly Vector4 DropReject = Rgb(0xE0505E);

    private const float ListIcon = 18f;
    /// <summary>How far the frame is drawn inside the element, so its dark edge stays inside too.</summary>
    private const float FrameInset = 2f;

    private DeckFace Small => DeckFace.For(_fonts, 1f, small: true);
    private DeckFace Bold => DeckFace.SmallBoldFor(_fonts, 1f);

    protected override void OnDraw(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        DeckRect f = g.Frame;
        DeckChrome.DrawFrame(ctx, new DeckRect(f.X + FrameInset, f.Y + FrameInset, f.W - 2f * FrameInset, f.H - 2f * FrameInset), 1f);
        DrawText(ctx, Bold, "Gear", GearDeckGeometry.Pad + 2f, g.Title, LabelInk);
        DrawClose(ctx);
        if (_model.Packs.Count == 0) return;   // no player yet: the frame and title only

        ClientObjectTable objects = _b.Objects();
        DrawEquipHeader(ctx);
        if (g.Input.Folded) DrawWornRow(ctx);
        else DrawSlots(ctx, objects);
        DrawPacksHeader(ctx);
        DeckChrome.DrawGauge(ctx, g.BurdenBar, _model.BurdenFill, StaminaInk, 1f);

        DeckRect c = g.Contents;
        ctx.PushClip(c.X, c.Y, c.W, c.H);
        try
        {
            if (g.Input.List) DrawList(ctx);
            else DrawGrid(ctx);
        }
        finally { ctx.PopClip(); }

        DrawSideColumn(ctx, objects);
        string footer = FooterText;
        DrawText(ctx, Small, footer, g.Footer.X + g.Footer.W - Small.Measure(ctx, footer), g.Footer, LabelInk);

        if (DropHighlight is { } drop && RectFor(drop.Hit) is { } target)
            StrokeAround(ctx, target, drop.Accept ? DropAccept : DropReject);
    }

    // ---- text and cells ----

    /// <summary>Text from <paramref name="x"/>, centred on <paramref name="band"/>'s height.</summary>
    private static void DrawText(UiRenderContext ctx, DeckFace face, string text, float x, DeckRect band, Vector4 ink)
    {
        if (text.Length == 0) return;
        face.Draw(ctx, text, x, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);
    }

    private static void DrawCentred(UiRenderContext ctx, DeckFace face, string text, DeckRect band, Vector4 ink) =>
        DrawText(ctx, face, text, band.X + (band.W - face.Measure(ctx, text)) / 2f, band, ink);

    private static string Fit(UiRenderContext ctx, DeckFace face, string text, float width) =>
        UiCommandDeck.FitCastingText(text, t => face.Measure(ctx, t), width);

    private static void DrawCell(UiRenderContext ctx, DeckRect r, Vector4 fill, Vector4 edge, float radius)
    {
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, radius, fill);
        ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 1f, radius, edge, 1f);
    }

    private uint IconFor(ClientObject o) => _b.ResolveIcon(o.Type, o.IconId, o.IconUnderlayId, o.IconOverlayId, o.Effects);

    private static void DrawIcon(UiRenderContext ctx, uint texture, DeckRect r)
    {
        if (texture != 0u) ctx.DrawSprite(texture, r.X + 1f, r.Y + 1f, r.W - 2f, r.H - 2f, 0, 0, 1, 1, Vector4.One);
    }

    /// <summary>A 2-point stroke whose inner edge is a point outside <paramref name="r"/>.</summary>
    private static void StrokeAround(UiRenderContext ctx, DeckRect r, Vector4 ink)
    {
        const float width = 2f, grow = 1f + width / 2f;
        ctx.StrokeRoundedRect(r.X - grow, r.Y - grow, r.W + 2f * grow, r.H + 2f * grow, 5f + grow, ink, width);
    }

    // ---- title and headers ----

    private void DrawClose(UiRenderContext ctx)
    {
        DeckRect c = _g.Close;
        bool hot = Hover.Zone == GearZone.Close;
        ctx.FillRoundedRect(c.X, c.Y, c.W, c.H, 4f, hot ? MenuHighlight : StudOff with { W = 0.6f });
        Vector4 ink = hot ? TextInk : StudOffInk;
        const float inset = 5f;
        ctx.DrawLine(c.X + inset, c.Y + inset, c.X + c.W - inset, c.Y + c.H - inset, ink, 1.5f);
        ctx.DrawLine(c.X + c.W - inset, c.Y + inset, c.X + inset, c.Y + c.H - inset, ink, 1.5f);
    }

    /// <summary>The ▾ / ▸ fold stud, "Equipment", and the Figure (dimmed in plan 1) and Slots studs.</summary>
    private void DrawEquipHeader(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        DeckRect h = g.EquipHeader;
        ctx.FillRoundedRect(h.X, h.Y, h.W, h.H, 5f, HeaderFill);

        Vector4 ink = DeckChrome.DrawStud(ctx, g.FoldStud, 1f, on: false);
        DrawChevron(ctx, g.FoldStud, down: !g.Input.Folded, ink);
        DrawText(ctx, Bold, "Equipment", g.FoldStud.X + g.FoldStud.W + 6f, h, LabelInk);

        DrawLabelledStud(ctx, g.FigureStud, "Figure", on: false, dim: true);
        DrawLabelledStud(ctx, g.SlotsStud, "Slots", on: true);
    }

    private void DrawPacksHeader(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        DeckRect h = g.PacksHeader;
        ctx.FillRoundedRect(h.X, h.Y, h.W, h.H, 5f, HeaderFill);
        DrawText(ctx, Bold, "Packs", h.X + 6f, h, LabelInk);

        string burden = _burdenText;
        DrawText(ctx, Small, burden, g.GridStud.X - 6f - Small.Measure(ctx, burden), h, LabelInk);

        bool list = g.Input.List;
        DrawLabelledStud(ctx, g.GridStud, "Grid", on: !list);
        DrawLabelledStud(ctx, g.ListStud, "List", on: list);
    }

    private void DrawLabelledStud(UiRenderContext ctx, DeckRect r, string label, bool on, bool dim = false)
    {
        Vector4 ink = DeckChrome.DrawStud(ctx, r, 1f, on, dim);
        DrawCentred(ctx, Bold, label, r, ink);   // fixed labels that fit their studs
    }

    /// <summary>A ▾ (<paramref name="down"/>) or ▸ drawn as two strokes on the stud's centre.</summary>
    private static void DrawChevron(UiRenderContext ctx, DeckRect r, bool down, Vector4 ink)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        const float arm = 4f, depth = 2f;
        if (down)
        {
            ctx.DrawLine(cx - arm, cy - depth, cx, cy + depth, ink, 1.5f);
            ctx.DrawLine(cx, cy + depth, cx + arm, cy - depth, ink, 1.5f);
        }
        else
        {
            ctx.DrawLine(cx - depth, cy - arm, cx + depth, cy, ink, 1.5f);
            ctx.DrawLine(cx + depth, cy, cx - depth, cy + arm, ink, 1.5f);
        }
    }

    // ---- equipment ----

    /// <summary>The Slots view: every slot as a dark cell with its worn icon, or its label when
    /// empty. Aetheria slots the character has not unlocked are dimmed, as the paper doll hides them.</summary>
    private void DrawSlots(UiRenderContext ctx, ClientObjectTable objects)
    {
        AetheriaUnlockState unlocked = _aetheria;
        IReadOnlyList<GearSlot> slots = GearSlotCatalog.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            GearSlot slot = slots[i];
            DeckRect r = _g.EquipSlot(i);
            bool locked = slot.IsAetheria && (unlocked & AetheriaBit(slot.Mask)) == AetheriaUnlockState.None;
            uint occupant = _slotOccupants[i];
            ctx.PushAlpha(locked || (occupant != 0 && _b.IsPending(occupant)) ? DimAlpha : 1f);
            try
            {
                DrawCell(ctx, r, SlotFill, SlotEdge, 6f);
                if (occupant != 0 && objects.Get(occupant) is { } worn) DrawIcon(ctx, IconFor(worn), r);
                else DrawSlotLabel(ctx, i, slot.Label, r);
                if (Hover.Zone == GearZone.EquipSlot && Hover.Index == i)
                    ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 1f, 6f, Handle with { W = 0.6f }, 1f);
            }
            finally { ctx.PopAlpha(); }
        }
    }

    private static AetheriaUnlockState AetheriaBit(EquipMask mask) => mask switch
    {
        EquipMask.SigilOne => AetheriaUnlockState.Blue,
        EquipMask.SigilTwo => AetheriaUnlockState.Yellow,
        EquipMask.SigilThree => AetheriaUnlockState.Red,
        _ => AetheriaUnlockState.None,
    };

    /// <summary>An empty slot's label: one line when it fits, else split at its first space over two.</summary>
    private void DrawSlotLabel(UiRenderContext ctx, int index, string label, DeckRect r)
    {
        DeckFace face = Small;
        if (_slotLabels[index] is not { } lines)
        {
            float room = r.W - 4f;
            int space = label.IndexOf(' ');
            lines = face.Measure(ctx, label) <= room || space < 0
                ? (Fit(ctx, face, label, room), null)
                : (Fit(ctx, face, label[..space], room), Fit(ctx, face, label[(space + 1)..], room));
            _slotLabels[index] = lines;
        }
        if (lines.Second is null)
        {
            DrawCentred(ctx, face, lines.First, r, LabelInk);
            return;
        }
        float lh = face.LineHeight(ctx), top = r.Y + (r.H - 2f * lh) / 2f;
        DrawCentred(ctx, face, lines.First, new DeckRect(r.X, top, r.W, lh), LabelInk);
        DrawCentred(ctx, face, lines.Second, new DeckRect(r.X, top + lh, r.W, lh), LabelInk);
    }

    /// <summary>The folded Equipment section: a row (or up to three) of worn icons.</summary>
    private void DrawWornRow(UiRenderContext ctx)
    {
        int count = Math.Min(_model.Worn.Count, GearDeckGeometry.MaxWornRows * GearDeckGeometry.WornPerRow);
        for (int i = 0; i < count; i++)
        {
            ClientObject worn = _model.Worn[i];
            DeckRect r = _g.WornCell(i);
            ctx.PushAlpha(_b.IsPending(worn.ObjectId) ? DimAlpha : 1f);
            try
            {
                DrawCell(ctx, r, SlotFill, SlotEdge, 4f);
                DrawIcon(ctx, IconFor(worn), r);
            }
            finally { ctx.PopAlpha(); }
        }
    }

    // ---- pack contents ----

    private void DrawGrid(UiRenderContext ctx)
    {
        IReadOnlyList<ClientObject> items = Items;
        DeckFace face = Small;
        for (int i = _scrollRow * GearDeckGeometry.Columns; i < items.Count; i++)
        {
            if (_g.GridCell(i, _scrollRow) is not { } r) break;
            ClientObject o = items[i];
            ctx.PushAlpha(_b.IsPending(o.ObjectId) ? DimAlpha : 1f);
            try
            {
                DrawCell(ctx, r, CellFill, CellEdge, 5f);
                DrawIcon(ctx, IconFor(o), r);
                string stack = CachedStackText(o.StackSize);
                if (stack.Length > 0)
                {
                    float lh = face.LineHeight(ctx);
                    face.Draw(ctx, stack, r.X + r.W - 2f - face.Measure(ctx, stack), r.Y + r.H - lh, TextInk);
                }
                if (Hover.Zone == GearZone.Item && Hover.Index == i)
                    ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 1f, 5f, Handle with { W = 0.6f }, 1f);
            }
            finally { ctx.PopAlpha(); }
        }
    }

    /// <summary>The list view: icon, name shortened to fit, and the stack count right-aligned.</summary>
    private void DrawList(UiRenderContext ctx)
    {
        IReadOnlyList<ClientObject> items = Items;
        DeckFace face = Small;
        for (int i = _scrollRow; i < items.Count; i++)
        {
            if (_g.ListRow(i, _scrollRow) is not { } r) break;
            ClientObject o = items[i];
            ctx.PushAlpha(_b.IsPending(o.ObjectId) ? DimAlpha : 1f);
            try
            {
                if (Hover.Zone == GearZone.Item && Hover.Index == i)
                    ctx.FillRoundedRect(r.X, r.Y, r.W, r.H - 1f, 3f, MenuHighlight);
                ctx.DrawFill(r.X, r.Y + r.H - 1f, r.W, 1f, RowRule);

                var icon = new DeckRect(r.X + 2f, r.Y + (r.H - ListIcon) / 2f, ListIcon, ListIcon);
                DrawIcon(ctx, IconFor(o), icon);

                string stack = CachedStackText(o.StackSize);
                float stackW = stack.Length > 0 ? face.Measure(ctx, stack) : 0f;
                float right = r.X + r.W - 4f;
                if (stack.Length > 0) DrawText(ctx, face, stack, right - stackW, r, LabelInk);

                float nameX = icon.X + icon.W + 6f;
                float nameW = right - (stack.Length > 0 ? stackW + 6f : 0f) - nameX;
                if (!_fittedNames.TryGetValue(o.ObjectId, out string? name))
                    _fittedNames[o.ObjectId] = name = Fit(ctx, face, _b.DisplayName(o), nameW);
                DrawText(ctx, face, name, nameX, r, TextInk);
            }
            finally { ctx.PopAlpha(); }
        }
    }

    // ---- side column ----

    /// <summary>The main pack, then each side pack, then empty side-pack slots up to the
    /// character's container slots; the selected pack outlined in <see cref="DeckChrome.Handle"/>.</summary>
    private void DrawSideColumn(UiRenderContext ctx, ClientObjectTable objects)
    {
        uint selected = SelectedPackId;
        DeckFace face = Small;
        int shown = _g.VisibleSidePacks;
        for (int i = 0; i < shown; i++)
        {
            DeckRect r = _g.SidePack(i);
            if (i >= _model.Packs.Count)
            {
                ctx.PushAlpha(0.5f);
                try { DrawCell(ctx, r, SlotFill, SlotEdge, 6f); }
                finally { ctx.PopAlpha(); }
                continue;
            }

            GearPack pack = _model.Packs[i];
            DrawCell(ctx, r, SlotFill, SlotEdge, 6f);
            uint texture = pack.IsMain
                ? _b.ResolveIcon(ItemType.Container, InventoryController.PlayerPackBaseIcon, 0u, 0u, 0u)
                : objects.Get(pack.Id) is { } bag ? IconFor(bag) : 0u;
            DrawIcon(ctx, texture, r);
            string badge = _badges[i];
            face.Draw(ctx, badge, r.X + r.W - 2f - face.Measure(ctx, badge), r.Y + r.H - face.LineHeight(ctx), LabelInk);
            if (pack.Id == selected) StrokeAround(ctx, r, Handle);
        }
    }

    /// <summary>The rectangle a hit names (for the drop highlight), or null.</summary>
    private DeckRect? RectFor(GearDeckHit hit) => hit.Zone switch
    {
        GearZone.EquipSlot when hit.Index >= 0 => _g.EquipSlot(hit.Index),
        GearZone.WornCell when hit.Index >= 0 => _g.WornCell(hit.Index),
        GearZone.SidePack when hit.Index >= 0 => _g.SidePack(hit.Index),
        GearZone.Item => _g.Input.List ? _g.ListRow(hit.Index, _scrollRow) : _g.GridCell(hit.Index, _scrollRow),
        _ => null,
    };
}
