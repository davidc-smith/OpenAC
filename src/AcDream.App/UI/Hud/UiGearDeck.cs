using System.Globalization;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>What the Gear Deck reads from the game and the session it saves its view state through.</summary>
internal sealed record GearDeckBindings(
    Func<ClientObjectTable> Objects,
    Func<uint> PlayerGuid,
    Func<int?> Strength,
    Func<ItemType, uint, uint, uint, uint, uint> ResolveIcon,
    Func<ItemType, uint, uint, uint, uint, uint> ResolveDragIcon,
    Func<ClientObject, string> DisplayName,
    GearDeckSession Session,
    IGearItemActions Actions,
    Func<uint, bool> IsPending,
    Action Close,
    Action<string>? Log = null,
    Silk.NET.Input.IKeyboard? Keyboard = null,
    Func<(bool Toggle, bool Range)>? Modifiers = null,
    IGearSalvage? Salvage = null,
    HudText? Text = null,
    PaperdollClickMap? ClickMap = null,
    GearMoveQueue? Queue = null,
    ItemLocks? Locks = null);

/// <summary>
/// The Gear Deck: the Modern HUD inventory and paper doll, one column 300
/// points × its own scale wide. Dragging the left or right edge scales the
/// whole deck in the decks' steps (the width is always 300 × the scale, so the
/// scale is read back from the width, and a saved layout restores it); the
/// top and bottom edges change the height only. Each tick it rebuilds its
/// <see cref="GearInventoryModel"/> when the object table, the player or the
/// strength changed, and its <see cref="GearDeckGeometry"/> when the height or
/// the layout input changed. Drawing is in UiGearDeck.Draw.cs, input in
/// UiGearDeck.Input.cs.
/// </summary>
internal sealed partial class UiGearDeck : UiElement
{
    /// <summary>The alpha of pending items and locked Aetheria slots.</summary>
    internal const float DimAlpha = 0.45f;

    private readonly GearDeckBindings _b;
    private readonly DeckFonts? _fonts;
    /// <summary>Held for later plans (Figure view, tool studs); plan 1 draws its glyphs as lines.</summary>
    private readonly EmbeddedSvgSpriteCache? _icons;
    private GearInventoryModel _model = GearInventoryModel.Empty;
    private GearDeckGeometry _g;
    /// <summary>The deck scale everything is laid out and drawn at: the width's (<see cref="GearDeckGeometry.ScaleForWidth"/>).</summary>
    private float _scale;
    /// <summary>The deck's text factor: its HUD text size (<see cref="HudText.FactorFor"/>), or 1 without one.</summary>
    private float _textFactor;
    private int _scrollRow;
    private ulong _lastRevision;
    private ClientObjectTable? _lastTable;
    private uint _lastPlayer;
    private int? _lastStrength;
    private int _lastLocks;
    private bool _built;
    /// <summary>Counts model rebuilds, so <see cref="Shown"/> knows when its rows are stale.</summary>
    private ulong _modelBuild;

    /// <summary>The model as flat rows: what a query searches and what <see cref="Shown"/> indexes.</summary>
    private GearProjection _projection = GearProjection.Empty;
    private GearQuery _query = GearQuery.Empty;
    /// <summary>Bumped by each query change; the caches that follow the query key on it.</summary>
    private int _queryVersion;
    internal int QueryVersion => _queryVersion;
    private IReadOnlyList<GearItemView> _shown = [];
    private (ulong Build, int QueryVersion, uint Pack)? _shownFor;

    // Built with the model (Refresh's gated rebuild) so a frame does no table scans or string work.
    /// <summary>Each <see cref="GearSlotCatalog.Slots"/> entry's occupant id, or 0.</summary>
    private uint[] _slotOccupants = new uint[GearSlotCatalog.Slots.Count];
    private AetheriaUnlockState _aetheria;
    private string _burdenText = "";
    private string[] _badges = [];
    private string _footer = "";
    private uint _footerFor;
    private string _matchText = "";
    private (ulong Build, int QueryVersion)? _matchFor;
    private string _footerLeft = "";
    private (ulong Build, int QueryVersion, int Selection, bool Results)? _footerLeftFor;
    /// <summary>The results list's value columns for <see cref="_columnsFor"/>'s query version.</summary>
    private (GearSortField? A, GearSortField? B) _columns;
    private int _columnsFor = -1;
    /// <summary>Results-list column texts fitted to their slot, by item and field; cleared with <see cref="_fittedNames"/>.</summary>
    private readonly Dictionary<(uint Id, GearSortField Field), string> _columnTexts = [];
    /// <summary>Stack corner texts by stack size; cleared with the model.</summary>
    private readonly Dictionary<int, string> _stackTexts = [];
    /// <summary>List names fitted to their row, by item id; cleared with the model and the geometry.</summary>
    private readonly Dictionary<uint, string> _fittedNames = [];
    /// <summary>Empty-slot labels fitted to one or two lines, by slot; cleared with the geometry.</summary>
    private readonly (string First, string? Second)?[] _slotLabels = new (string, string?)?[GearSlotCatalog.Slots.Count];

    public UiGearDeck(GearDeckBindings bindings, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons,
        Func<double>? clock = null)
    {
        _b = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _fonts = fonts;
        _icons = icons;
        _clock = clock ?? (() => Environment.TickCount64 / 1000.0);
        _actions = new LoggedGearItemActions(_b.Actions);
        _dispatcherTable = _b.Objects();
        _dispatcher = new GearDropDispatcher(_dispatcherTable, _b.PlayerGuid, _actions);
        _scale = 1f;
        _textFactor = _b.Text?.FactorFor(HudDeck.Gear) ?? 1f;
        Width = GearDeckGeometry.WidthAt(_scale);
        MinWidth = MinWindowWidth;
        MaxWidth = MaxWindowWidth;
        Height = GearDeckGeometry.DefaultHeightAt(_scale);
        MinHeight = GearDeckGeometry.MinHeightAt(_scale);
        Resizable = true;
        ResizeX = true;
        ResizeY = true;
        ResizableEdges = AllEdges;
        _g = GearDeckGeometry.Create(Height, LayoutInput(), null, _scale, TextGrow);
        CreateQueryInput();   // UiGearDeck.Query.cs
        CreateMenu();         // UiGearDeck.Menu.cs
        _b.Session.Ended += OnSessionEnded;
        if (_b.Text is { } text) text.Changed += OnTextSizeChanged;
    }

    /// <summary>The text factor the deck draws at: its HUD text size, or 1 without one.</summary>
    internal float TextFactor => _textFactor;

    /// <summary>How much the list and suggestion rows grow at the deck's text size.</summary>
    private float TextGrow => DeckFonts.RowGrow(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _scale, _textFactor);

    /// <summary>A text size change does what a scale change does: drop fitted text, rebuild geometry.</summary>
    private void OnTextSizeChanged() => SyncGeometry();

    /// <summary>The live query is not kept between sessions, and the figure stands up (the default settings are unlocked).</summary>
    private void OnSessionEnded()
    {
        FinishSave(null, "session ended");
        SetQuery(GearQuery.Empty);
        QueryInput.SetText("");
        ResetFigurePose();
    }

    /// <summary>Stops following the session; the runtime calls it as it disposes the deck.</summary>
    internal void Detach()
    {
        _b.Session.Ended -= OnSessionEnded;
        if (_b.Text is { } text) text.Changed -= OnTextSizeChanged;
    }

    // The window's full width range, one width per scale step; ConstrainResize
    // and SyncScale keep the width on a step.
    private const float MinWindowWidth = GearDeckGeometry.Width * DeckGeometry.MinScale;
    private const float MaxWindowWidth = GearDeckGeometry.Width * DeckGeometry.MaxScale;
    private const ResizeEdges AllEdges = ResizeEdges.Left | ResizeEdges.Right | ResizeEdges.Top | ResizeEdges.Bottom;

    /// <summary>How the runtime mounts the deck: imported chrome, any width in the decks' scale range, the
    /// minimum height at <paramref name="scale"/> (its <see cref="Scale"/>), resized from every edge, hidden.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top, float scale = 1f) => new()
    {
        WindowName = WindowNames.GearDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = true,
        ResizeY = true,
        ResizableEdges = AllEdges,
        ConstrainResizeToParent = true,
        MinWidth = MinWindowWidth,
        MaxWidth = MaxWindowWidth,
        MinHeight = GearDeckGeometry.MinHeightAt(scale),
        Visible = false,
        ContentClickThrough = false,
    };

    internal GearDeckGeometry Geometry => _g;

    /// <summary>The deck scale the deck is laid out and drawn at.</summary>
    internal float Scale => _scale;
    internal GearInventoryModel Model => _model;
    internal GearDeckBindings Bindings => _b;

    /// <summary>The first shown grid or list row.</summary>
    internal int ScrollRow => _scrollRow;

    /// <summary>The zone under the pointer, for hover backings and the tooltip.</summary>
    internal GearDeckHit Hover { get; private set; } = new(GearZone.None);

    /// <summary>The drop target being dragged over and whether it accepts, or null.</summary>
    internal (GearDeckHit Hit, bool Accept)? DropHighlight { get; private set; }

    /// <summary>The selected pack when it still exists, else the main pack (the player).</summary>
    internal uint SelectedPackId
    {
        get
        {
            uint selected = _b.Session.Settings.SelectedPack;
            foreach (GearPack p in _model.Packs)
                if (p.Id == selected) return selected;
            return _model.Packs.Count > 0 ? _model.Packs[0].Id : 0u;
        }
    }

    /// <summary>The loose items of the selected pack, in slot order.</summary>
    internal IReadOnlyList<ClientObject> Items => _model.Contents(SelectedPackId);

    /// <summary>The live query; an empty one shows the selected pack.</summary>
    internal GearQuery Query => _query;

    /// <summary>A non-empty query shows its matches from every pack instead of the selected pack. A sort alone
    /// is no query: it orders the selected pack's view.</summary>
    internal bool ShowsResults => !_query.IsEmpty;

    /// <summary>The sort applies to the pack view and to results alike, so its stud is never dim.</summary>
    internal bool SortStudDim => false;

    /// <summary>Pack order has no direction: the ↑/↓ stud is dim and inert under it.</summary>
    internal bool DirStudDim => _query.Sort.Field == GearSortField.Pack;

    /// <summary>Sets the live query and shows its first row; the same query changes nothing.</summary>
    internal void SetQuery(GearQuery q)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (q.Equals(_query)) return;
        _query = q;
        _queryVersion++;
        ClearFitted();   // the results list fits its names around the query's columns
        ScrollTo(0);
    }

    /// <summary>
    /// The rows the contents area shows and every item hit indexes: the query's
    /// matches across all packs while <see cref="ShowsResults"/>, else the selected
    /// pack's rows in the query's sort (slot order under Pack order). Each row keeps
    /// its own <see cref="GearItemView.Slot"/>, which a drop on it uses. Rebuilt only
    /// when the model, query or pack changed.
    /// </summary>
    internal IReadOnlyList<GearItemView> Shown
    {
        get
        {
            uint pack = SelectedPackId;
            if (_shownFor != (_modelBuild, _queryVersion, pack))
            {
                _shown = ShowsResults
                    ? GearQueryEngine.Run(_projection.All, _query)
                    : GearQueryEngine.Sort(_projection.ForPack(pack), _query.Sort);
                _shownFor = (_modelBuild, _queryVersion, pack);
                PruneSelection(_shown);
            }
            return _shown;
        }
    }

    /// <summary>The rows the selected view holds for <see cref="Shown"/>.</summary>
    private int ContentRows => _b.Session.Settings.View == GearPackView.List
        ? Shown.Count
        : (Shown.Count + GearDeckGeometry.Columns - 1) / GearDeckGeometry.Columns;

    private int VisibleRows => _b.Session.Settings.View == GearPackView.List ? _g.VisibleListRows : _g.VisibleGridRows;

    /// <summary>The last row that can be first: the rows past what is visible.</summary>
    internal int MaxScrollRow => Math.Max(0, ContentRows - VisibleRows);

    /// <summary>Shows <paramref name="row"/> first, clamped to <c>[0, MaxScrollRow]</c>.</summary>
    internal void ScrollTo(int row)
    {
        _scrollRow = Math.Clamp(row, 0, MaxScrollRow);
        SyncGeometry();
    }

    protected override void OnTick(double deltaSeconds)
    {
        _b.Session.Tick();
        Refresh();
        TrackDropHighlight();
    }

    /// <summary>Rebuilds the model when the table, player or strength changed, then the geometry when needed.</summary>
    internal void Refresh()
    {
        ClientObjectTable table = _b.Objects();
        uint player = _b.PlayerGuid();
        int? strength = _b.Strength();
        int locksRev = _b.Locks?.Revision ?? 0;
        if (!_built || !ReferenceEquals(table, _lastTable) || table.MutationRevision != _lastRevision
            || player != _lastPlayer || strength != _lastStrength || locksRev != _lastLocks)
        {
            _model = GearInventoryModel.Build(table, player, strength);
            _projection = GearProjection.Build(_model, table, _b.DisplayName, _b.Locks is { } locks ? locks.IsLocked : null);
            _modelBuild++;
            RebuildCaches(table, player);
            _lastTable = table;
            _lastRevision = table.MutationRevision;
            _lastPlayer = player;
            _lastStrength = strength;
            _lastLocks = locksRev;
            _built = true;
        }
        SyncGeometry();
        // Items can go away under the scroll; keep it in range.
        int clamped = Math.Clamp(_scrollRow, 0, MaxScrollRow);
        if (clamped != _scrollRow)
        {
            _scrollRow = clamped;
            SyncGeometry();
        }
    }

    private GearDeckLayoutInput LayoutInput() => new(
        _b.Session.Settings.Folded,
        _b.Session.Settings.View == GearPackView.List,
        _model.Worn.Count,
        Shown.Count,
        _model.SidePackSlots,
        _scrollRow,
        SavedCount: _b.Session.Settings.SavedFilters.Count,
        Suggestions: VisibleSuggestions,
        SalvageButton: _b.Salvage?.IsOpen == true && (ShowsResults || _selection.Count > 0) && SalvageCount > 0
            && _b.Queue?.IsRunning != true,
        Figure: ShowsFigureView,
        CombineButton: _b.Queue is { IsRunning: false } && CombineMoves.Count > 0,
        Queue: _b.Queue?.IsRunning == true);

    private void SyncGeometry()
    {
        SyncScale();
        float grow = TextGrow;
        GearDeckLayoutInput input = LayoutInput();
        FitMinHeight(input);
        GearPills pills = Pills();
        if (input != _g.Input || MathF.Max(Height, GearDeckGeometry.MinHeightAt(_scale)) != _g.Frame.H
            || _pillsVersion != _geometryPills || _g.Scale != _scale || _g.TextGrow != grow)
        {
            _g = GearDeckGeometry.Create(Height, input, pills, _scale, grow);
            _geometryPills = _pillsVersion;
            ClearFitted();
            Array.Clear(_slotLabels);
            Array.Clear(_fittedChips);
        }
        PlaceQueryInput();
    }

    /// <summary>
    /// Sets the deck to <paramref name="scale"/> (snapped to the decks' steps and
    /// range) as a side-edge drag would, keeping the right edge when the new
    /// width would cross the parent's right side.
    /// </summary>
    internal void SetScale(float scale)
    {
        float width = GearDeckGeometry.WidthAt(DeckGeometry.SnapScale(scale));
        if (width == Width) return;
        float right = Left + Width;
        Width = width;
        if (Parent is { } parent && Left + Width > parent.Width)
            Left = MathF.Max(0f, MathF.Min(right, parent.Width) - Width);
        ResetAnchorCapture();
        SyncGeometry();
        FindRoot()?.NotifyWindowResized(this);   // the layout records the new size
    }

    /// <summary>
    /// Follows the width's scale and the HUD text size: a change re-bakes the fields' fonts and drops
    /// the text fitted at the old size, and every check puts the width on the
    /// scale's step (a saved or hand-edited layout may restore another), kept
    /// inside the parent. A width that would cross the parent's right side
    /// keeps the old right edge instead. The height is left to
    /// <see cref="FitMinHeight"/>, which grows it to the new minimum.
    /// </summary>
    private void SyncScale()
    {
        float scale = GearDeckGeometry.ScaleForWidth(Width);
        float factor = _b.Text?.FactorFor(HudDeck.Gear) ?? 1f;
        if (scale != _scale || factor != _textFactor)
        {
            _scale = scale;
            _textFactor = factor;
            SetFieldFonts();
            SetMenuScale();   // UiGearDeck.Menu.cs
            _pillsFor = null;   // pills are measured with the face at the scale
            _sortText = null;
            _fittedFooter = null;
            _fittedSalvage = null;
            Array.Clear(_suggestTexts);
            Array.Clear(_slotLabels);
            Array.Clear(_fittedChips);
            Array.Clear(_fittedSaved);
            ClearFitted();
        }
        float width = GearDeckGeometry.WidthAt(_scale);
        if (Width == width) return;
        float right = Left + Width;
        Width = width;
        if (Parent is { } parent && Left + Width > parent.Width)
            Left = MathF.Max(0f, MathF.Min(right, parent.Width) - Width);
        ResetAnchorCapture();
        FindRoot()?.NotifyWindowResized(this);   // the layout records the new size
    }

    /// <summary>
    /// The window's minimum is the height that shows every side-pack cell for
    /// the fold state. A deck below it (unfolded, a pack slot gained, an older
    /// saved size) grows to it, no taller than its parent and kept inside it.
    /// </summary>
    private void FitMinHeight(GearDeckLayoutInput input)
    {
        float min = GearDeckGeometry.MinHeightFor(input, _scale);
        MinHeight = min;
        if (Height >= min) return;
        UiElement? parent = Parent;
        float target = parent is null ? min : MathF.Min(min, MathF.Max(Height, parent.Height));
        if (target <= Height) return;
        Height = target;
        if (parent is not null && Top + Height > parent.Height) Top = MathF.Max(0f, parent.Height - Height);
        ResetAnchorCapture();
        FindRoot()?.NotifyWindowResized(this);   // the layout records the new size
    }

    /// <summary>What the draw reads per model: slot occupants (a whole-table scan each),
    /// the Aetheria unlocks, the burden label and the side-pack badges.</summary>
    private void RebuildCaches(ClientObjectTable table, uint player)
    {
        IReadOnlyList<GearSlot> slots = GearSlotCatalog.Slots;
        for (int i = 0; i < slots.Count; i++)
            _slotOccupants[i] = _model.Packs.Count == 0 ? 0u : GearSlotCatalog.Occupant(table, player, slots[i]);
        _aetheria = AetheriaUnlocks.Read(table.Get(player));
        _burdenText = _model.BurdenPercent.ToString(CultureInfo.InvariantCulture) + "% burden";
        _badges = new string[_model.Packs.Count];
        for (int i = 0; i < _badges.Length; i++)
            _badges[i] = _model.Packs[i].IsMain ? "M" : (i + 1).ToString(CultureInfo.InvariantCulture);
        _footer = "";
        _footerFor = 0;
        _stackTexts.Clear();
        ClearFitted();
    }

    /// <summary>Forgets the list names and column texts fitted to their rows.</summary>
    private void ClearFitted()
    {
        _fittedNames.Clear();
        _columnTexts.Clear();
    }

    /// <summary>A results row's pack badge: "M", "2"…"8", or "W" for a worn item.</summary>
    private string PackBadge(GearItemView row) =>
        row.Worn ? "W" : row.PackIndex >= 0 && row.PackIndex < _badges.Length ? _badges[row.PackIndex] : "";

    /// <summary>"3 of 102 match": the results against every pack item.</summary>
    internal string MatchText
    {
        get
        {
            if (_matchFor != (_modelBuild, _queryVersion))
            {
                _matchText = $"{Shown.Count.ToString(CultureInfo.InvariantCulture)} of {_projection.PackItemCount.ToString(CultureInfo.InvariantCulture)} match";
                _matchFor = (_modelBuild, _queryVersion);
            }
            return _matchText;
        }
    }

    /// <summary>The footer's left text: a running queue's progress; else the match text and " · k selected" in
    /// results, "k selected" alone in the pack view, and empty when nothing is selected there. Cached per model,
    /// query and selection, or per queue version.</summary>
    internal string FooterLeftText
    {
        get
        {
            if (_b.Queue is { IsRunning: true } q) return QueueText(q);
            _ = Shown;   // prune the selection first, so the count is of shown items
            if (_footerLeftFor != (_modelBuild, _queryVersion, _selectionVersion, ShowsResults))
            {
                string picked = _selection.Count > 0
                    ? _selection.Count.ToString(CultureInfo.InvariantCulture) + " selected" : "";
                _footerLeft = !ShowsResults ? picked : picked.Length == 0 ? MatchText : MatchText + " · " + picked;
                _footerLeftFor = (_modelBuild, _queryVersion, _selectionVersion, ShowsResults);
            }
            return _footerLeft;
        }
    }

    /// <summary>The results list's value columns: the sort field when it is not Name or Pack, then
    /// the first range chip's field when that differs from the sort.</summary>
    private (GearSortField? A, GearSortField? B) ResultColumns
    {
        get
        {
            if (_columnsFor == _queryVersion) return _columns;
            GearSortField? sort = _query.Sort.Field is GearSortField.Name or GearSortField.Pack ? null : _query.Sort.Field;
            GearSortField? range = null;
            IReadOnlyList<GearChip> chips = _query.Chips;
            for (int i = 0; i < chips.Count; i++)
                if (chips[i].IsRange) { range = GearChipText.SortFieldOf(chips[i].Field); break; }
            if (range == sort) range = null;
            _columns = sort is null ? (range, null) : (sort, range);
            _columnsFor = _queryVersion;
            return _columns;
        }
    }

    /// <summary>The occupant drawn in slot <paramref name="index"/> of <see cref="GearSlotCatalog.Slots"/>, or 0.</summary>
    internal uint SlotOccupant(int index) => _slotOccupants[index];

    private string CachedStackText(int stack)
    {
        if (stack <= 1) return "";
        if (!_stackTexts.TryGetValue(stack, out string? text)) _stackTexts[stack] = text = StackText(stack);
        return text;
    }

    // ---- salvage hand-off ----

    private int _salvageCount;
    private (ulong Build, int QueryVersion, int Selection, int Revision, bool Open, bool OneMaterial)? _salvageFor;
    private readonly Dictionary<int, string> _salvageTexts = [];

    /// <summary>The rows the footer's Add to salvage acts on: the selection in <see cref="Shown"/> order, else all
    /// of Shown while results show; none in the plain pack view with nothing selected.</summary>
    private IReadOnlyList<GearItemView> SalvageTargets()
    {
        IReadOnlyList<GearItemView> shown = Shown;   // prunes the selection
        if (_selection.Count == 0) return ShowsResults ? shown : [];
        var picked = new List<GearItemView>(_selection.Count);
        foreach (GearItemView row in shown)
            if (_selection.Contains(row.Id)) picked.Add(row);
        return picked;
    }

    /// <summary>How many of the targets the salvage panel would take; cached per model, query, selection and panel
    /// state (its contents, whether it is open and whether it takes one material only).</summary>
    internal int SalvageCount
    {
        get
        {
            if (_b.Salvage is not { } salvage) return 0;
            _ = Shown;
            bool open = salvage.IsOpen;
            (ulong, int, int, int, bool, bool) key =
                (_modelBuild, _queryVersion, _selectionVersion, salvage.Revision, open, salvage.OneMaterial);
            if (_salvageFor != key)
            {
                _salvageCount = open ? GearSalvageHandOff.Candidates(SalvageTargets(), salvage).Count : 0;
                _salvageFor = key;
            }
            return _salvageCount;
        }
    }

    /// <summary>The footer button's label for the current <see cref="SalvageCount"/>.</summary>
    internal string SalvageButtonText => SalvageText(SalvageCount);

    /// <summary>"Add N to salvage", built once per N.</summary>
    private string SalvageText(int n)
    {
        if (!_salvageTexts.TryGetValue(n, out string? text))
            _salvageTexts[n] = text = $"Add {n.ToString(CultureInfo.InvariantCulture)} to salvage";
        return text;
    }

    private void AddTargetsToSalvage()
    {
        if (_b.Salvage is not { IsOpen: true } salvage) return;
        GearSalvageHandOff.AddAll(GearSalvageHandOff.Candidates(SalvageTargets(), salvage), salvage);
    }

    /// <summary>The window is on screen: nothing to restore, the scroll is kept.</summary>
    public void OnShown()
    {
        ClearHover();
        NoteFigureUnavailable();   // UiGearDeck.Figure.cs
    }

    /// <summary>The window left the screen: forget the hover and any drop highlight, keep the scroll,
    /// and stand the figure back up so the next open starts at rest.</summary>
    public void OnHidden()
    {
        ClearHover();
        ResetFigurePose();
    }

    /// <summary>
    /// A side-edge drag scales the deck: the proposed width snaps live to the
    /// nearest scale step (as the Command Deck's drags do), lowered a step at a
    /// time while the deck at that scale would not fit the room the drag has
    /// (the side being dragged towards and the height's minimum). The top and
    /// bottom edges change only the height, never under the minimum at the
    /// scale. A corner does both: its side sets the scale, its top or bottom
    /// the height. The edges not dragged stay where they are.
    /// </summary>
    internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
    {
        float scale = _scale;
        if ((edges & (ResizeEdges.Left | ResizeEdges.Right)) != 0)
        {
            scale = DeckGeometry.SnapScale(width / GearDeckGeometry.Width);
            if (ConstrainResizeToParent && Parent is { } parent)
            {
                float roomWidth = (edges & ResizeEdges.Left) != 0 ? Left + Width : parent.Width - Left;
                float roomHeight = (edges & ResizeEdges.Top) != 0 ? Top + Height : parent.Height - Top;
                GearDeckLayoutInput input = LayoutInput();
                while (scale > DeckGeometry.MinScale + 1e-4f
                       && (GearDeckGeometry.WidthAt(scale) > roomWidth + 0.01f
                           || GearDeckGeometry.MinHeightFor(input, scale) > roomHeight + 0.01f))
                    scale = DeckGeometry.SnapScale(scale - DeckGeometry.ScaleStep);
            }
        }
        width = GearDeckGeometry.WidthAt(scale);
        height = MathF.Max(height, scale == _scale ? MinHeight : GearDeckGeometry.MinHeightFor(LayoutInput(), scale));
    }

    /// <summary>"Main · 96 / 102" or "Pack 3 · 10 / 24": the selected pack, its item count and capacity.</summary>
    internal string FooterText
    {
        get
        {
            uint selected = SelectedPackId;
            if (selected != _footerFor || _footer.Length == 0)
            {
                _footer = BuildFooter(selected);
                _footerFor = selected;
            }
            return _footer;
        }
    }

    private string BuildFooter(uint selected)
    {
        for (int i = 0; i < _model.Packs.Count; i++)
        {
            GearPack p = _model.Packs[i];
            if (p.Id != selected) continue;
            string name = p.IsMain ? "Main" : $"Pack {i + 1}";
            return $"{name} · {_model.Contents(p.Id).Count} / {p.Capacity}";
        }
        return "";
    }

    /// <summary>A stack count for a cell corner: empty for one, thousands as "9k".</summary>
    internal static string StackText(int stack) => stack switch
    {
        <= 1 => "",
        < 1000 => stack.ToString(CultureInfo.InvariantCulture),
        _ => (stack / 1000).ToString(CultureInfo.InvariantCulture) + "k",
    };
}
