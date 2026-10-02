using System.Numerics;
using AcDream.Plugin.Abstractions;
using static AcDream.App.UI.PluginUiStyle;

namespace AcDream.App.UI;

/// <summary>
/// The plugin dock: one slot per plugin window, a move handle, a collapse
/// button and a gear that opens plugin appearance. It draws the same shape in
/// every theme, in the theme's palette or the dock's own Classic colours
/// (<see cref="PluginUiThemeSettings.DockPalette"/>). Where things go is
/// worked out by <see cref="PluginDockLayout"/>.
/// </summary>
public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowStateController, IRetainedPanelController
{
    private const float DefaultTop = 116f;
    private const float DefaultLeft = 10f;
    private const float BottomMargin = 8f;
    private const float SnapIn = 8f;
    private const float SnapOut = 32f;

    private readonly RetailWindowManager _windows;
    private readonly Func<uint, (uint tex, int width, int height)> _resolve;
    private readonly UiDatFont? _font;
    private readonly Dictionary<RetailWindowHandle, ShelfEntry> _entries = [];
    private readonly List<RetailWindowHandle> _order = [];
    private readonly DockGrip _grip;
    private readonly DockToggleButton _toggle;
    private readonly DockGearButton _gear;
    private readonly PluginUiThemeSettings _themes;
    private readonly Action? _appearanceRequested;
    private PluginUiTheme _lastTheme;
    private PluginDockMode _appliedMode;
    private PluginDockMode? _dragMode;
    private PluginDockLayout _layout;
    private int _firstRow;
    private bool _disposed;
    private float _lastLayoutHeight = -1f;

    private bool _collapsed;

    private bool _requestedVisible = true;

    private bool _userPositioned;

    private bool _homeApplied;

    private float _homeLeft;
    private float _homeTop;

    private RetailWindowHandle? _ownHandle;

    public PluginSidePanel(
        RetailWindowManager windows,
        Func<uint, (uint tex, int width, int height)> resolve,
        UiDatFont? font,
        PluginUiThemeSettings? themes = null,
        Action? appearanceRequested = null)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _font = font;
        _themes = themes ?? new PluginUiThemeSettings();
        _appearanceRequested = appearanceRequested;
        _lastTheme = _themes.Theme;
        _appliedMode = _themes.Dock;

        Top = DefaultTop;
        Anchors = AnchorEdges.None;
        Draggable = false;
        Resizable = false;
        // The dock's Width/Height are entirely derived (Reflow), so a restored
        // layout's saved dimensions must never stomp them via ResizeTo.
        ResizeX = false;
        ResizeY = false;
        BackgroundColor = Vector4.Zero;
        BorderColor = Vector4.Zero;
        Visible = false;

        _grip = new DockGrip(this) { WindowMoveHandle = true, Anchors = AnchorEdges.None };
        _toggle = new DockToggleButton(this) { Anchors = AnchorEdges.None };
        _toggle.Click += ToggleCollapsed;
        _gear = new DockGearButton(this) { Anchors = AnchorEdges.None };
        _gear.Click += () => _appearanceRequested?.Invoke();
        AddChild(_grip);
        AddChild(_toggle);
        AddChild(_gear);
        _layout = PluginDockLayout.Compute(PluginDockMode.Floating, false, [], float.PositiveInfinity, 0);
        Reflow();

        _windows.WindowUnregistered += OnWindowUnregistered;
        _windows.WindowRegistered += OnWindowRegistered;
    }

    /// <summary>Number of live plugin-window entries, exposed for gates.</summary>
    public int EntryCount => _entries.Count;

    /// <summary>Where the dock's parts are now, for tests and for drawing.</summary>
    internal PluginDockLayout Layout => _layout;

    internal PluginUiPalette Palette => _themes.DockPalette;

    /// <summary>The font for monograms and labels: the bundled title face in Moss/Brass, the DAT font in Classic.</summary>
    internal UiDatFont? TitleFont => _themes.Palette is null ? _font : _themes.ModernTitleFont ?? _themes.ModernFont ?? _font;

    internal UiDatFont? BodyFont => _themes.Palette is null ? _font : _themes.ModernFont ?? _font;

    /// <summary>Whether the pointer is over the dock, which shows the handle dots and the collapse button.</summary>
    internal bool PointerOver { get; private set; }

    internal bool Collapsed => _collapsed;

    /// <summary>
    /// The mode the dock is drawn in: the saved one, or the one a drag has
    /// snapped it into, which is saved when the drag ends.
    /// </summary>
    internal PluginDockMode Mode => _dragMode ?? _themes.Dock;

    /// <summary>
    /// Adds one manifest-scoped plugin window and its minimize affordance.
    /// Duplicate handles are idempotent.
    /// </summary>
    public void Add(
        PluginUiOwner owner,
        PluginPanelDescriptor descriptor,
        RetailWindowHandle handle,
        (uint Texture, int Width, int Height)? fileIcon = null) =>
        Add(owner, descriptor, handle, fileIcon, svgIcon: null);

    /// <summary>
    /// The same, with an SVG icon. The caller's hold on <paramref name="svgIcon"/>
    /// passes to the dock, which gives it back when the window goes.
    /// </summary>
    internal void Add(
        PluginUiOwner owner,
        PluginPanelDescriptor descriptor,
        RetailWindowHandle handle,
        (uint Texture, int Width, int Height)? fileIcon,
        PluginSvgIconCache.PluginSvgIconEntry? svgIcon)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handle);
        if (_entries.ContainsKey(handle))
        {
            svgIcon?.Release();
            return;
        }

        handle.OuterFrame.ConstrainResizeToParent = true;
        KeepWindowReachable(handle);

        var button = new PluginShelfButton(this, descriptor, owner, handle, _resolve, fileIcon, svgIcon)
        {
            Width = DockSlot,
            Height = DockSlot,
        };
        button.Click += () =>
        {
            if (handle.IsVisible)
                handle.Hide();
            else
                handle.Show();
        };

        var minimize = new PluginMinimizeButton(handle, _font, _themes)
        {
            Left = MathF.Max(8f, handle.OuterFrame.Width - 23f),
            Top = 3f,
            Width = 18f,
            Height = 17f,
            Anchors = AnchorEdges.Top | AnchorEdges.Right,
        };
        handle.OuterFrame.AddChild(minimize);

        _entries.Add(handle, new ShelfEntry(button, minimize));
        _order.Add(handle);
        AddChild(button);
        Reflow();
    }

    public void Show()
    {
        _requestedVisible = true;
        if (_collapsed)
            _collapsed = false;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    /// <summary>Hide the dock. Preserves entries/positions; never disables a
    /// plugin or touches any plugin window's own visibility.</summary>
    public void Hide()
    {
        _requestedVisible = false;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette p = Palette;
        PluginDockMode mode = Mode;
        DockBody(ctx, p, mode, Width, Height);
        foreach (float y in _layout.Dividers)
            DockDivider(ctx, p, Width, y);
        if (_collapsed) return;
        DockSide side = ScreenSide;
        foreach (ShelfEntry entry in _entries.Values)
        {
            PluginShelfButton button = entry.Button;
            if (!button.Visible) continue;
            if (mode == PluginDockMode.Floating)
            {
                if (button.IsOpen)
                    OpenDot(ctx, p, side, Width, button.Top, button.Height);
            }
            else
            {
                EdgePill(ctx, button.IsOpen ? p.Accent : p.Muted, side, Width, button.Top, button.Height, button.PillHeight);
            }
        }
    }

    /// <summary>The hover label is drawn in the overlay pass, outside the dock's own clip.</summary>
    protected override bool ExpandsClipForPopup => true;

    /// <summary>The slot (or gear) the pointer is over, whose label shows; null when none is.</summary>
    internal UiSimpleButton? HoveredSlot
    {
        get
        {
            if (_collapsed || !Visible) return null;
            if (_gear.Visible && _gear.State is UiControlState.Hovered or UiControlState.Pressed)
                return _gear;
            foreach (RetailWindowHandle handle in _order)
            {
                PluginShelfButton button = _entries[handle].Button;
                if (button.Visible && button.State is UiControlState.Hovered or UiControlState.Pressed)
                    return button;
            }
            return null;
        }
    }

    /// <summary>A slot's label: the window title, and the plugin's name under it unless they are the same.</summary>
    internal static (string Title, string? Subtitle) LabelText(UiSimpleButton slot) => slot switch
    {
        PluginShelfButton button => (button.WindowTitle,
            string.Equals(button.WindowTitle, button.OwnerName, StringComparison.Ordinal) ? null : button.OwnerName),
        _ => ("Plugin appearance", null),
    };

    /// <summary>Where a slot's label goes, in the dock's space: beside the slot, on the side away from the screen edge.</summary>
    internal DockRect LabelRect(UiSimpleButton slot)
    {
        (string title, string? subtitle) = LabelText(slot);
        UiDatFont? titleFont = TitleFont, bodyFont = BodyFont;
        float titleHeight = titleFont?.LineHeight ?? 12f;
        float bodyHeight = bodyFont?.LineHeight ?? 12f;
        float textWidth = MathF.Max(titleFont?.MeasureWidth(title) ?? 0f,
            subtitle is null ? 0f : bodyFont?.MeasureWidth(subtitle) ?? 0f);
        float w = MathF.Ceiling(textWidth + 2f * LabelPadX);
        float h = MathF.Ceiling(titleHeight + (subtitle is null ? 0f : bodyHeight + 1f) + 2f * LabelPadY);
        float x = ScreenSide == DockSide.Left ? Width + LabelGap : -LabelGap - w;
        float y = MathF.Round(slot.Top + (slot.Height - h) / 2f);
        return new DockRect(x, y, w, h);
    }

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (HoveredSlot is not { } slot) return;
        PluginUiPalette p = Palette;
        DockRect r = LabelRect(slot);
        HoverLabel(ctx, p, r.X, r.Y, r.W, r.H);
        (string title, string? subtitle) = LabelText(slot);
        float y = r.Y + LabelPadY;
        if (TitleFont is { } titleFont)
        {
            ctx.DrawStringDat(titleFont, title, r.X + LabelPadX, y, p.Text);
            y += titleFont.LineHeight + 1f;
        }
        if (subtitle is not null && BodyFont is { } bodyFont)
            ctx.DrawStringDat(bodyFont, subtitle, r.X + LabelPadX, y, p.Muted);
    }

    protected override void OnDrawAfterChildren(UiRenderContext ctx)
    {
        if (_collapsed) return;
        if (_layout.FadeTop) DockFade(ctx, Palette, _layout.ListTop, Width, darkAtBottom: false);
        if (_layout.FadeBottom) DockFade(ctx, Palette, _layout.ListBottom - DockFadeHeight, Width, darkAtBottom: true);
    }

    /// <summary>The screen edge the dock faces: a rail's own edge, or the nearer one for a floating dock.</summary>
    internal DockSide ScreenSide => Mode switch
    {
        PluginDockMode.Left => DockSide.Left,
        PluginDockMode.Right => DockSide.Right,
        _ => Parent is { } parent && Left + Width / 2f > parent.Width / 2f ? DockSide.Right : DockSide.Left,
    };

    /// <summary>
    /// While the dock is dragged: a floating dock that comes within 8pt of a
    /// screen edge becomes a rail on that edge, and a rail follows the pointer
    /// up and down its edge until the dock's would-be edge is more than 32pt away, when it
    /// floats again. The new mode is saved when the drag ends.
    /// </summary>
    internal override void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY)
    {
        if (Parent is not { } parent) return;
        PluginDockMode mode = Mode;
        PluginDockMode next = mode switch
        {
            PluginDockMode.Floating when left <= SnapIn => PluginDockMode.Left,
            PluginDockMode.Floating when left + Width >= parent.Width - SnapIn => PluginDockMode.Right,
            PluginDockMode.Left when left > SnapOut => PluginDockMode.Floating,
            PluginDockMode.Right when left + PluginUiStyle.RailWidth < parent.Width - SnapOut => PluginDockMode.Floating,
            _ => mode,
        };
        if (next != mode)
        {
            _dragMode = next;
            _appliedMode = next;
            Reflow();
            if (next == PluginDockMode.Floating)
                left = Math.Clamp(left, 0f, MathF.Max(0f, parent.Width - Width));
        }
        if (next == PluginDockMode.Left)
            left = 0f;
        else if (next == PluginDockMode.Right)
            left = parent.Width - Width;
        top = Math.Clamp(top, 0f, MathF.Max(0f, parent.Height - Height));
    }

    protected override void OnTick(double deltaSeconds)
    {
        base.OnTick(deltaSeconds);

        if (_lastTheme != _themes.Theme)
        {
            _lastTheme = _themes.Theme;
            Reflow();
        }
        if (_dragMode is not null && Parent is UiRoot moveRoot && !moveRoot.IsWindowMoveActive)
            _dragMode = null;
        if (_appliedMode != Mode)
            ApplyModeFromSetting(_appliedMode, Mode);
        _grip.Opacity = _windows.IsLocked ? 0.5f : 1f;
        PointerOver = Parent is UiRoot root
            && root.MouseX >= Left && root.MouseX < Left + Width
            && root.MouseY >= Top && root.MouseY < Top + Height;

        if (Parent is { } parent)
        {
            float availableHeight = MathF.Max(0f, parent.Height - Top - BottomMargin);
            if (MathF.Abs(availableHeight - _lastLayoutHeight) > 0.5f)
            {
                _lastLayoutHeight = availableHeight;
                Reflow(availableHeight);
            }

            if (!_homeApplied && !_userPositioned && parent.Width > 0f)
            {
                float homeLeft = MathF.Min(DefaultLeft, MathF.Max(0f, parent.Width - Width));
                float homeTop = Top;
                _homeLeft = homeLeft;
                _homeTop = homeTop;
                _homeApplied = true;
                if (_ownHandle is { } handle)
                    handle.MoveTo(homeLeft, homeTop);
                else
                    Left = homeLeft;
            }
        }

        if (Parent is { } edgeParent && Mode != PluginDockMode.Floating)
            Left = Mode == PluginDockMode.Left ? 0f : edgeParent.Width - Width;

        UpdateEdgePills((float)deltaSeconds);

        foreach (RetailWindowHandle handle in _entries.Keys)
            KeepWindowReachable(handle);
    }

    /// <summary>
    /// The player picked a mode in plugin appearance: a rail moves to its
    /// edge, and a floating dock moves 10pt in from the edge it was on. Both
    /// keep their top.
    /// </summary>
    private void ApplyModeFromSetting(PluginDockMode from, PluginDockMode to)
    {
        _appliedMode = to;
        Reflow();
        if (Parent is not { } parent) return;
        float left = to switch
        {
            PluginDockMode.Left => 0f,
            PluginDockMode.Right => parent.Width - Width,
            _ when from == PluginDockMode.Right => MathF.Max(0f, parent.Width - Width - DefaultLeft),
            _ => DefaultLeft,
        };
        if (_ownHandle is { } handle)
            handle.MoveTo(left, Top);
        else
            Left = left;
    }

    /// <summary>The plugin window drawn over the dock's other open windows, which gets the tall edge pill.</summary>
    internal RetailWindowHandle? FrontWindow
    {
        get
        {
            RetailWindowHandle? front = null;
            foreach (RetailWindowHandle handle in _order)
            {
                if (handle.IsVisible && (front is null || handle.OuterFrame.ZOrder >= front.OuterFrame.ZOrder))
                    front = handle;
            }
            return front;
        }
    }

    private void UpdateEdgePills(float dt)
    {
        RetailWindowHandle? front = FrontWindow;
        float step = RailPillFront / RailPillSeconds * MathF.Max(0f, dt);
        foreach (RetailWindowHandle handle in _order)
        {
            PluginShelfButton button = _entries[handle].Button;
            float target = ReferenceEquals(handle, front) ? RailPillFront
                : button.IsOpen ? RailPillOpen
                : button.State is UiControlState.Hovered or UiControlState.Pressed ? RailPillHover
                : 0f;
            float delta = target - button.PillHeight;
            button.PillHeight += Math.Clamp(delta, -step, step);
        }
    }

    /// <inheritdoc />
    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.RightClick && _appearanceRequested is not null)
        {
            _appearanceRequested();
            return true;
        }
        if (e.Type == UiEventType.Scroll && !_collapsed && _layout.MaxFirstRow > 0)
        {
            _firstRow = Math.Clamp(_firstRow + (e.Data0 > 0 ? -1 : 1), 0, _layout.MaxFirstRow);
            Reflow();
            return true;
        }
        return base.OnEvent(in e);
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    private void OnWindowRegistered(RetailWindowHandle handle)
    {
        if (!ReferenceEquals(handle.OuterFrame, this)) return;
        _ownHandle = handle;
        handle.Moved += OnHandleMoved;
        _windows.WindowRegistered -= OnWindowRegistered;
    }

    private void OnHandleMoved(RetailWindowHandle _)
    {
        if (_dragMode is { } snapped)
        {
            _dragMode = null;
            _themes.Dock = snapped;
        }
        if (Mode != PluginDockMode.Floating)
            return;
        if (!_homeApplied)
        {
            _userPositioned = true;
            return;
        }
        if (Parent is { } parent)
        {
            float currentHomeLeft = MathF.Min(DefaultLeft, MathF.Max(0f, parent.Width - Width));
            float reachabilityClampOfPriorHome = Math.Clamp(
                _homeLeft, 0f, MathF.Max(0f, parent.Width - Width));
            bool stillHome = Top == _homeTop
                && (Left == currentHomeLeft || Left == reachabilityClampOfPriorHome);
            if (stillHome)
            {
                _homeLeft = Left;
                _homeTop = Top;
                return;
            }
        }

        if (Left != _homeLeft || Top != _homeTop)
            _userPositioned = true;
    }

    // ── IRetainedPanelController: separates "has entries" (availability) from
    // the user's own show/hide request — see ApplyVisibility. ────────────────

    void IRetainedPanelController.OnShown() => _requestedVisible = true;

    void IRetainedPanelController.OnHidden()
    {
        // Hidden because EntryCount hit zero is temporary (availability gate);
        // hidden while entries remain is a real user hide.
        if (_entries.Count > 0)
            _requestedVisible = false;
    }

    public RetainedWindowState CaptureWindowState() =>
        new(Collapsed: _collapsed, RequestedVisible: _requestedVisible);

    public void RestoreWindowState(RetainedWindowState state)
    {
        _collapsed = state.Collapsed;
        if (state.RequestedVisible is { } requestedVisible)
            _requestedVisible = requestedVisible;
        Reflow();
    }

    private void OnWindowUnregistered(RetailWindowHandle handle)
    {
        if (!_entries.Remove(handle, out ShelfEntry entry))
            return;

        _order.Remove(handle);
        entry.Button.ReleaseSvgIcon();
        RemoveChild(entry.Button);
        if (ReferenceEquals(entry.Minimize.Parent, handle.OuterFrame))
            handle.OuterFrame.RemoveChild(entry.Minimize);
        Reflow();
    }

    private static void KeepWindowReachable(RetailWindowHandle handle)
    {
        if (handle.OuterFrame.Parent is not { } parent
            || parent.Width <= 0f
            || parent.Height <= 0f)
        {
            return;
        }

        float left = Math.Clamp(
            handle.Left,
            0f,
            MathF.Max(0f, parent.Width - handle.Width));
        float top = Math.Clamp(
            handle.Top,
            0f,
            MathF.Max(0f, parent.Height - handle.Height));
        if (left != handle.Left || top != handle.Top)
            handle.MoveTo(left, top);
    }

    private void Reflow(float? maximumHeight = null)
    {
        float effectiveHeight = maximumHeight
            ?? (_lastLayoutHeight >= 0f ? _lastLayoutHeight : float.PositiveInfinity);
        string[] owners = _order.Select(h => _entries[h].Button.OwnerId).ToArray();
        _layout = PluginDockLayout.Compute(Mode, _collapsed, owners, effectiveHeight, _firstRow);
        _firstRow = _layout.FirstRow;

        for (int i = 0; i < _order.Count; i++)
        {
            PluginShelfButton button = _entries[_order[i]].Button;
            if (_layout.Slots[i] is { } slot)
            {
                Place(button, slot);
                button.Visible = true;
            }
            else
            {
                button.Visible = false;
            }
        }

        Width = _layout.Width;
        Height = _layout.Height;
        Place(_grip, _layout.Handle);
        Place(_toggle, _layout.Toggle);
        Place(_gear, _layout.Gear);
        _gear.Visible = !_collapsed;
        ApplyVisibility();

        if (_homeApplied && !_userPositioned)
        {
            _homeLeft = Left;
            _homeTop = Top;
        }
    }

    private static void Place(UiElement element, DockRect rect)
    {
        element.Left = rect.X;
        element.Top = rect.Y;
        element.Width = rect.W;
        element.Height = rect.H;
    }

    private void ApplyVisibility()
    {
        Visible = _requestedVisible && _entries.Count > 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _windows.WindowUnregistered -= OnWindowUnregistered;
        _windows.WindowRegistered -= OnWindowRegistered;
        if (_ownHandle is { } ownHandle)
            ownHandle.Moved -= OnHandleMoved;

        foreach ((RetailWindowHandle handle, ShelfEntry entry) in _entries)
        {
            entry.Button.ReleaseSvgIcon();
            if (ReferenceEquals(entry.Minimize.Parent, handle.OuterFrame))
                handle.OuterFrame.RemoveChild(entry.Minimize);
        }
        _entries.Clear();
        _order.Clear();
        Visible = false;
    }

    private readonly record struct ShelfEntry(
        PluginShelfButton Button,
        PluginMinimizeButton Minimize);

    /// <summary>The move handle: three dots, shown while the pointer is over the dock or it is collapsed.</summary>
    private sealed class DockGrip(PluginSidePanel dock) : UiPanel
    {
        protected override void OnDraw(UiRenderContext ctx)
        {
            if (!dock.PointerOver && !dock.Collapsed) return;
            HandleDots(ctx, dock.Palette, Width / 2f,
                dock.Collapsed && dock.Mode == PluginDockMode.Floating ? Height / 2f : DockHandleTop + DockHandleHeight / 2f);
        }
    }

    /// <summary>Collapses or expands the dock. Shown with the handle dots.</summary>
    private sealed class DockToggleButton(PluginSidePanel dock) : UiSimpleButton
    {
        public override string? GetTooltipText() => dock.Collapsed ? "Expand the dock" : "Collapse the dock";

        protected override void OnDraw(UiRenderContext ctx)
        {
            if (!dock.PointerOver && !dock.Collapsed) return;
            PluginUiPalette p = dock.Palette;
            GhostButton(ctx, p, Width, Height, ThemeState);
            ChevronDirection direction = (dock.Mode, dock.Collapsed) switch
            {
                (PluginDockMode.Left, false) or (PluginDockMode.Right, true) => ChevronDirection.Left,
                (PluginDockMode.Left, true) or (PluginDockMode.Right, false) => ChevronDirection.Right,
                (_, true) => ChevronDirection.Down,
                _ => ChevronDirection.Up,
            };
            Chevron(ctx, Width / 2f, Height / 2f, 6f, direction,
                ThemeState == UiControlState.Normal ? p.Muted : p.Text);
        }
    }

    /// <summary>The gear slot at the bottom of the dock, which opens plugin appearance.</summary>
    private sealed class DockGearButton(PluginSidePanel dock) : UiSimpleButton
    {
        internal UiControlState State => ThemeState;

        /// <summary>The dock's hover label says "Plugin appearance", so the gear has no tooltip.</summary>
        public override string? GetTooltipText() => null;

        protected override void OnDraw(UiRenderContext ctx)
        {
            PluginUiPalette p = dock.Palette;
            var (wx, wy, ww, wh) = DockSlotWellRect(dock.Mode, Width, Height);
            DockSlotWell(ctx, p, wx, wy, ww, wh, ThemeState);
            Gear(ctx, (Width - DockArt) / 2f, (Height - DockArt) / 2f, DockArt,
                ThemeState == UiControlState.Normal ? p.Muted : p.Text);
        }
    }

    internal sealed class PluginShelfButton : UiSimpleButton
    {
        private readonly PluginSidePanel _dock;
        private readonly RetailWindowHandle _handle;
        private readonly Func<uint, (uint tex, int width, int height)> _resolve;
        private readonly (uint Texture, int Width, int Height)? _fileIcon;
        private readonly uint _iconSurfaceId;
        private readonly string _initialsFallback;
        private readonly Vector4 _monogramHue;

        private bool _iconResolveAttempted;
        private bool _iconAvailable;
        private PluginSvgIconCache.PluginSvgIconEntry? _svgIcon;

        internal PluginShelfButton(
            PluginSidePanel dock,
            PluginPanelDescriptor descriptor,
            PluginUiOwner owner,
            RetailWindowHandle handle,
            Func<uint, (uint tex, int width, int height)> resolve,
            (uint Texture, int Width, int Height)? fileIcon = null,
            PluginSvgIconCache.PluginSvgIconEntry? svgIcon = null)
        {
            _dock = dock;
            _svgIcon = svgIcon;
            _handle = handle;
            _resolve = resolve;
            _fileIcon = fileIcon;
            OwnerId = owner.Id;
            WindowTitle = descriptor.Title;
            OwnerName = owner.DisplayName;
            _iconSurfaceId = PluginIcons.Normalize(descriptor.IconSurfaceId);
            _initialsFallback = Initials(descriptor.IconText, descriptor.Title);
            _monogramHue = MonogramHue(owner.Id);
            Text = _svgIcon is null && _fileIcon is null && _iconSurfaceId == 0 ? _initialsFallback : string.Empty;
            Anchors = AnchorEdges.None;
        }

        internal string OwnerId { get; }

        internal string WindowTitle { get; }

        internal string OwnerName { get; }

        internal bool IsOpen => _handle.IsVisible;

        internal RetailWindowHandle Window => _handle;

        internal UiControlState State => ThemeState;

        /// <summary>How tall this slot's edge pill is now; it eases toward its state's height on a rail.</summary>
        internal float PillHeight { get; set; }

        /// <summary>The dock's hover label names the window, so the slot has no tooltip.</summary>
        public override string? GetTooltipText() => null;

        protected override void OnDraw(UiRenderContext ctx)
        {
            PluginUiPalette p = _dock.Palette;
            var (wx, wy, ww, wh) = DockSlotWellRect(_dock.Mode, Width, Height);
            DockSlotWell(ctx, p, wx, wy, ww, wh, ThemeState);
            Vector4 colour = ThemeState is UiControlState.Hovered or UiControlState.Pressed ? p.Text
                : IsOpen ? p.Accent : p.Muted;
            DrawIcon(ctx, (Width - DockArt) / 2f, (Height - DockArt) / 2f, DockArt, colour);
        }

        /// <summary>
        /// Draws this window's icon in the <paramref name="extent"/> box at (x, y): its
        /// SVG, else its file icon, else its DAT surface, else its monogram. The SVG is
        /// one colour, drawn in <paramref name="colour"/> and snapped to the device grid.
        /// RGBA art ignores the hue of <paramref name="colour"/> and keeps its own
        /// colours (dimmer while closed).
        /// </summary>
        internal void DrawIcon(UiRenderContext ctx, float x, float y, float extent, Vector4 colour)
        {
            if (_svgIcon is { } svg)
            {
                int pixels = PluginSvgIconCache.DevicePixels(extent, ctx.PixelScale);
                uint coverage = svg.TextureFor(pixels);
                if (coverage != 0)
                {
                    float offset = (extent - pixels / ctx.PixelScale) * 0.5f;
                    ctx.DrawCoverageIcon(coverage, x + offset, y + offset, pixels, colour);
                    return;
                }
                // The icon failed for good (it never retries): give it back and use the next one.
                ReleaseSvgIcon();
                if (_fileIcon is null && _iconSurfaceId == 0)
                    Text = _initialsFallback;
            }

            if (_fileIcon is null && !_iconResolveAttempted && _iconSurfaceId != 0)
            {
                _iconResolveAttempted = true;
                (uint tex, int w, int h) = _resolve(_iconSurfaceId);
                _iconAvailable = tex != 0 && w > 0 && h > 0;
                if (!_iconAvailable)
                    Text = _initialsFallback;
            }

            float alpha = IsOpen || ThemeState != UiControlState.Normal ? 1f : ClosedArtAlpha;
            uint texture = 0;
            int width = 0, height = 0;
            if (_fileIcon is { } file)
                (texture, width, height) = file;
            else if (_iconSurfaceId != 0 && _iconAvailable)
                (texture, width, height) = _resolve(_iconSurfaceId);

            if (texture != 0 && width > 0 && height > 0)
            {
                ctx.DrawSprite(texture, x, y, extent, extent, 0f, 0f, 1f, 1f, Vector4.One with { W = alpha });
                return;
            }
            Monogram(ctx, _dock.TitleFont, Text, _monogramHue, x, y, extent, alpha);
        }

        /// <summary>Gives the button's hold on its SVG back; safe to call more than once.</summary>
        internal void ReleaseSvgIcon()
        {
            _svgIcon?.Release();
            _svgIcon = null;
        }

        private static string Initials(string? requested, string title)
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim()[..Math.Min(2, requested.Trim().Length)];

            string[] words = title.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
                return "?";
            if (words.Length == 1)
                return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
            return string.Concat(words.Take(2).Select(static word =>
                char.ToUpperInvariant(word[0])));
        }
    }

    private sealed class PluginMinimizeButton : UiSimpleButton
    {
        private readonly RetailWindowHandle _handle;
        private readonly PluginUiThemeSettings? _themes;
        private readonly UiDatFont? _classicFont;

        internal PluginMinimizeButton(RetailWindowHandle handle, UiDatFont? font, PluginUiThemeSettings? themes)
        {
            _handle = handle;
            _themes = themes;
            _classicFont = font;
            Text = "–";
            DatFont = font;
            Outline = true;
            BackgroundColor = new Vector4(0.02f, 0.02f, 0.015f, 0.94f);
            BorderColor = new Vector4(0.58f, 0.46f, 0.17f, 1f);
            BorderThickness = 1f;
            Click += () => _handle.Hide();
        }

        public override string? GetTooltipText() => "Minimize to the dock";

        /// <summary>Only windows that opted into the shared theme get the ghost button in their header.</summary>
        protected override void OnTick(double deltaSeconds)
        {
            base.OnTick(deltaSeconds);
            ThemePalette = _handle.OuterFrame is UiPluginMarkupPanel ? _themes?.Palette : null;
            Outline = ThemePalette is null;
            TextColor = ThemePalette?.Muted ?? Vector4.One;
            DatFont = ThemePalette is null ? _classicFont : _themes?.ModernFont ?? _classicFont;
        }

        private protected override void DrawThemedFace(UiRenderContext ctx, PluginUiPalette palette) =>
            PluginUiStyle.GhostButton(ctx, palette, Width, Height, ThemeState);
    }
}
