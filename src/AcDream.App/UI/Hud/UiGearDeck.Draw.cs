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
    /// <summary>A results-list value column and the pack badge slot after it.</summary>
    private const float ColumnW = 44f, BadgeW = 16f;
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
        DrawQueryBar(ctx);

        DeckRect c = g.Contents;
        ctx.PushClip(c.X, c.Y, c.W, c.H);
        try
        {
            if (g.Input.List) DrawList(ctx, objects);
            else DrawGrid(ctx, objects);
        }
        finally { ctx.PopClip(); }

        DrawSideColumn(ctx, objects);
        DrawText(ctx, Small, FooterLeftText, g.Footer.X, g.Footer, LabelInk);
        if (!ShowsResults)
        {
            string footer = FooterText;
            DrawText(ctx, Small, footer, g.Footer.X + g.Footer.W - Small.Measure(ctx, footer), g.Footer, LabelInk);
        }

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
        DrawChevron(ctx, g.FoldStud, g.Input.Folded ? Chevron.Right : Chevron.Down, ink);
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

    private enum Chevron { Down, Right, Up }

    /// <summary>A ▾, ▸ or ▴ drawn as two strokes on the stud's centre.</summary>
    private static void DrawChevron(UiRenderContext ctx, DeckRect r, Chevron direction, Vector4 ink)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        const float arm = 4f, depth = 2f;
        if (direction == Chevron.Down)
        {
            ctx.DrawLine(cx - arm, cy - depth, cx, cy + depth, ink, 1.5f);
            ctx.DrawLine(cx, cy + depth, cx + arm, cy - depth, ink, 1.5f);
        }
        else if (direction == Chevron.Up)
        {
            ctx.DrawLine(cx - arm, cy + depth, cx, cy - depth, ink, 1.5f);
            ctx.DrawLine(cx, cy - depth, cx + arm, cy + depth, ink, 1.5f);
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

    /// <summary>The grid: each row's icon and stack, and in results its pack badge top-left.</summary>
    private void DrawGrid(UiRenderContext ctx, ClientObjectTable objects)
    {
        IReadOnlyList<GearItemView> rows = Shown;
        bool results = ShowsResults;
        DeckFace face = Small;
        for (int i = _scrollRow * GearDeckGeometry.Columns; i < rows.Count; i++)
        {
            if (_g.GridCell(i, _scrollRow) is not { } r) break;
            GearItemView row = rows[i];
            if (objects.Get(row.Id) is not { } o) continue;   // gone since the model was built
            ctx.PushAlpha(_b.IsPending(o.ObjectId) ? DimAlpha : 1f);
            try
            {
                bool picked = _selection.Contains(row.Id);
                DrawCell(ctx, r, CellFill, CellEdge, 5f);
                if (picked) ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, MenuHighlight);
                DrawIcon(ctx, IconFor(o), r);
                string stack = CachedStackText(o.StackSize);
                if (stack.Length > 0)
                {
                    float lh = face.LineHeight(ctx);
                    face.Draw(ctx, stack, r.X + r.W - 2f - face.Measure(ctx, stack), r.Y + r.H - lh, TextInk);
                }
                if (results) face.Draw(ctx, PackBadge(row), r.X + 2f, r.Y + 1f, LabelInk);
                if (picked)
                    ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 1f, 5f, Handle, 1f);
                else if (Hover.Zone == GearZone.Item && Hover.Index == i)
                    ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 1f, 5f, Handle with { W = 0.6f }, 1f);
            }
            finally { ctx.PopAlpha(); }
        }
    }

    /// <summary>The list view: icon, name shortened to fit, and the stack count right-aligned;
    /// in results, up to two value columns and the pack badge in place of the stack.</summary>
    private void DrawList(UiRenderContext ctx, ClientObjectTable objects)
    {
        IReadOnlyList<GearItemView> rows = Shown;
        bool results = ShowsResults;
        DeckFace face = Small;
        for (int i = _scrollRow; i < rows.Count; i++)
        {
            if (_g.ListRow(i, _scrollRow) is not { } r) break;
            GearItemView row = rows[i];
            if (objects.Get(row.Id) is not { } o) continue;   // gone since the model was built
            ctx.PushAlpha(_b.IsPending(o.ObjectId) ? DimAlpha : 1f);
            try
            {
                if (_selection.Contains(row.Id))
                    ctx.FillRoundedRect(r.X, r.Y, r.W, r.H - 1f, 3f, MenuHighlight);
                if (Hover.Zone == GearZone.Item && Hover.Index == i)
                    ctx.StrokeRoundedRect(r.X + 0.5f, r.Y + 0.5f, r.W - 1f, r.H - 2f, 3f, Handle with { W = 0.6f }, 1f);
                ctx.DrawFill(r.X, r.Y + r.H - 1f, r.W, 1f, RowRule);

                var icon = new DeckRect(r.X + 2f, r.Y + (r.H - ListIcon) / 2f, ListIcon, ListIcon);
                DrawIcon(ctx, IconFor(o), icon);
                if (results)
                {
                    DrawResultColumns(ctx, face, row, r, icon.X + icon.W + 6f);
                    continue;
                }

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

    /// <summary>A results row after its icon: the name, the value columns right-aligned in
    /// <see cref="ColumnW"/> slots, then the pack badge centred in its <see cref="BadgeW"/> slot.</summary>
    private void DrawResultColumns(UiRenderContext ctx, DeckFace face, GearItemView row, DeckRect r, float nameX)
    {
        var badge = new DeckRect(r.X + r.W - 2f - BadgeW, r.Y, BadgeW, r.H);
        DrawCentred(ctx, face, PackBadge(row), badge, LabelInk);

        (GearSortField? a, GearSortField? b) = ResultColumns;
        float right = badge.X;
        if (b is GearSortField second) right = DrawColumn(ctx, face, row, second, r, right);
        if (a is GearSortField first) right = DrawColumn(ctx, face, row, first, r, right);

        float nameW = right - 6f - nameX;
        if (!_fittedNames.TryGetValue(row.Id, out string? name))
            _fittedNames[row.Id] = name = Fit(ctx, face, row.Name, nameW);
        DrawText(ctx, face, name, nameX, r, TextInk);
    }

    /// <summary>One value column ending at <paramref name="right"/>; returns the column's left edge.</summary>
    private float DrawColumn(UiRenderContext ctx, DeckFace face, GearItemView row, GearSortField field, DeckRect r, float right)
    {
        float room = ColumnW - 4f;
        if (!_columnTexts.TryGetValue((row.Id, field), out string? text))
            _columnTexts[(row.Id, field)] = text = Fit(ctx, face, GearChipText.Value(field, row), room);
        if (text.Length > 0) DrawText(ctx, face, text, right - 4f - face.Measure(ctx, text), r, LabelInk);
        return right - ColumnW;
    }

    // ---- query bar ----

    /// <summary>The bar's backing, its chips and "+N" pill, and the field's dark well with the
    /// placeholder while it is empty and unfocused (the field child draws the typed text).</summary>
    private void DrawQueryBar(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        DeckRect bar = g.QueryBar;
        ctx.FillRoundedRect(bar.X, bar.Y, bar.W, bar.H, 5f, HeaderFill);

        DeckRect well = g.QueryField;
        if (well.W > 0f)
        {
            ctx.FillRoundedRect(well.X, well.Y + 2f, well.W, well.H - 4f, 4f, ChatDeckPalette.FieldFill);
            if (QueryInput.Text.Length == 0 && !QueryInput.IsFocused)
                DrawText(ctx, Small, QueryPlaceholder, well.X + 4f, well, LabelInk);
        }

        IReadOnlyList<string> labels = Pills().Chips;
        for (int i = 0; i < g.Chips.Count && i < labels.Count; i++)
            DrawChip(ctx, i, labels[i], g.Chips[i]);
        if (g.ChipMore is { } more)
        {
            DrawCell(ctx, more, HeaderFill, CellEdge, 5f);
            DrawCentred(ctx, Bold, MoreText, more, Hover.Zone == GearZone.ChipMore ? TextInk : LabelInk);
        }
        DrawQueryStuds(ctx);
        DrawSavedRow(ctx);
    }

    /// <summary>The ★, sort and direction studs. Sort applies to results only, so the sort pair is dim
    /// (but live) while none are shown; ★ is dim while the query is empty.</summary>
    private void DrawQueryStuds(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        Vector4 star = DeckChrome.DrawStud(ctx, g.SaveStud, 1f, on: _naming, dim: _query.IsEmpty || SavedFull);
        DrawStar(ctx, g.SaveStud, star);

        bool dim = !ShowsResults;
        GearSortField field = _query.Sort.Field;
        Vector4 ink = DeckChrome.DrawStud(ctx, g.SortStud, 1f, on: false, dim);
        if (_sortText is not { } cached || cached.Field != field)
            _sortText = cached = (field, Fit(ctx, Bold, GearChipText.SortLabel(field), g.SortStud.W - 6f));
        DrawCentred(ctx, Bold, cached.Text, g.SortStud, ink);

        ink = DeckChrome.DrawStud(ctx, g.DirStud, 1f, on: false, dim);
        DrawChevron(ctx, g.DirStud, _query.Sort.Descending ? Chevron.Down : Chevron.Up, ink);
    }

    /// <summary>A five-point star: five strokes, each joining every second point of a pentagon of radius 5.</summary>
    private static void DrawStar(UiRenderContext ctx, DeckRect r, Vector4 ink)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        const float radius = 5f;
        Span<Vector2> p = stackalloc Vector2[5];
        for (int i = 0; i < 5; i++)
        {
            float a = -MathF.PI / 2f + i * 2f * MathF.PI / 5f;
            p[i] = new Vector2(cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a));
        }
        for (int i = 0; i < 5; i++)
        {
            Vector2 from = p[i], to = p[(i + 2) % 5];
            ctx.DrawLine(from.X, from.Y, to.X, to.Y, ink, 1.2f);
        }
    }

    /// <summary>One stud per saved filter (lit while it is the live query) and a "+N" pill for the rest.</summary>
    private void DrawSavedRow(UiRenderContext ctx)
    {
        GearDeckGeometry g = _g;
        IReadOnlyList<string> names = Pills().Saved;
        IReadOnlyList<SavedFilter> saved = _b.Session.Settings.SavedFilters;
        for (int i = 0; i < g.SavedStuds.Count && i < names.Count && i < saved.Count; i++)
        {
            DeckRect r = g.SavedStuds[i];
            bool hot = Hover.Zone == GearZone.SavedFilter && Hover.Index == i;
            Vector4 ink = DeckChrome.DrawStud(ctx, r, 1f, on: _query.Equals(saved[i].Query));
            string text = i < _fittedSaved.Length && _fittedSaved[i] is { } fitted
                ? fitted
                : Fit(ctx, Bold, names[i], MathF.Max(0f, r.W - 2f * GearDeckGeometry.PillPadX));
            if (i < _fittedSaved.Length) _fittedSaved[i] = text;
            DrawCentred(ctx, Bold, text, r, hot ? TextInk : ink);
        }
        if (g.SavedMore is { } more)
        {
            if (_savedMoreFor != g.HiddenSaved)
            {
                _savedMoreText = "+" + g.HiddenSaved.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _savedMoreFor = g.HiddenSaved;
            }
            DrawCell(ctx, more, HeaderFill, CellEdge, 5f);
            DrawCentred(ctx, Bold, _savedMoreText, more, Hover.Zone == GearZone.SavedMore ? TextInk : LabelInk);
        }
    }

    /// <summary>A chip pill: its label fitted before a small × in the right <see cref="GearDeckGeometry.ChipCloseW"/>.</summary>
    private void DrawChip(UiRenderContext ctx, int index, string label, DeckRect r)
    {
        bool hot = Hover.Zone == GearZone.Chip && Hover.Index == index;
        DrawCell(ctx, r, HeaderFill, hot ? Handle with { W = 0.6f } : CellEdge, 5f);
        const float pad = GearDeckGeometry.PillPadX, closeW = GearDeckGeometry.ChipCloseW;
        string text = index < _fittedChips.Length && _fittedChips[index] is { } fitted
            ? fitted
            : Fit(ctx, Bold, label, MathF.Max(0f, r.W - 2f * pad - closeW));
        if (index < _fittedChips.Length) _fittedChips[index] = text;
        Vector4 ink = hot ? TextInk : LabelInk;
        DrawText(ctx, Bold, text, r.X + pad, r, ink);

        const float arm = 2.5f;
        float cx = r.X + r.W - pad - closeW / 2f, cy = r.Y + r.H / 2f;
        ctx.DrawLine(cx - arm, cy - arm, cx + arm, cy + arm, ink, 1.2f);
        ctx.DrawLine(cx + arm, cy - arm, cx - arm, cy + arm, ink, 1.2f);
    }

    /// <summary>The suggestion rows under the bar, over the grid: a panel with each chip's
    /// suggestion text and the highlighted row (the one Enter takes) backed.</summary>
    private void DrawSuggestions(UiRenderContext ctx)
    {
        int count = Math.Min(_g.Input.Suggestions, _suggestions.Count);
        if (count == 0 || _model.Packs.Count == 0) return;
        DeckRect first = _g.Suggestion(0), last = _g.Suggestion(count - 1);
        var panel = new DeckRect(first.X, first.Y, first.W, last.Y + last.H - first.Y);
        DrawCell(ctx, panel, CellFill, CellEdge, 4f);
        DeckFace face = Small;
        for (int i = 0; i < count; i++)
        {
            DeckRect row = _g.Suggestion(i);
            bool picked = i == _suggestIndex;
            if (picked) ctx.FillRoundedRect(row.X + 1f, row.Y + 1f, row.W - 2f, row.H - 2f, 3f, MenuHighlight);
            if (i >= _suggestTexts.Length) break;
            string text = _suggestTexts[i] ??= Fit(ctx, face, GearChipText.Suggestion(_suggestions[i]), row.W - 12f);
            bool hot = picked || (Hover.Zone == GearZone.Suggestion && Hover.Index == i);
            DrawText(ctx, face, text, row.X + 6f, row, hot ? TextInk : LabelInk);
        }
    }

    // ---- side column ----

    /// <summary>The main pack, then each side pack, then empty side-pack slots up to the
    /// character's container slots; the selected pack outlined in <see cref="DeckChrome.Handle"/>.
    /// While results are shown the packs with a Pack chip are outlined instead (none: every pack).</summary>
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
            if (ShowsResults ? HasPackChip(pack.Id) : pack.Id == selected) StrokeAround(ctx, r, Handle);
        }
    }

    /// <summary>The rectangle a hit names (for the drop highlight), or null.</summary>
    private DeckRect? RectFor(GearDeckHit hit) => hit.Zone switch
    {
        GearZone.EquipSlot when hit.Index >= 0 => _g.EquipSlot(hit.Index),
        GearZone.WornCell when hit.Index >= 0 => _g.WornCell(hit.Index),
        GearZone.SidePack when hit.Index >= 0 => _g.SidePack(hit.Index),
        GearZone.Chip when hit.Index >= 0 && hit.Index < _g.Chips.Count => _g.Chips[hit.Index],
        GearZone.ChipMore => _g.ChipMore,
        GearZone.Item => _g.Input.List ? _g.ListRow(hit.Index, _scrollRow) : _g.GridCell(hit.Index, _scrollRow),
        _ => null,
    };
}
