using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// Plugin dialogs on screen. <c>Confirm</c> boxes queue per owner window
/// (ownerless ones share a queue) and show one at a time per owner; plugin
/// windows can be shown modal over another. Both are window-modal through
/// <see cref="UiRoot.BlockWindow"/>: only the owner is blocked. Every dialog
/// is answered exactly once, and the answer runs after the dialog is gone.
/// UI thread only; <see cref="Tick"/> once per frame.
/// </summary>
internal sealed class PluginDialogHost : IDisposable
{
    private sealed class ConfirmEntry
    {
        public required string Title { get; init; }
        public required string Message { get; init; }
        public required IReadOnlyList<string> Buttons { get; init; }
        public required UiElement? Owner { get; init; }
        public required Action<int> OnResult { get; init; }
        public UiPluginDialog? View { get; set; }
        public IDisposable? Block { get; set; }
        public UiElement? PreviousFocus { get; set; }
        public bool Done { get; set; }
        public int CancelIndex => Buttons.Count - 1;
    }

    private sealed record ModalEntry(UiElement Panel, UiElement Owner, IDisposable Block);

    private readonly UiRoot _root;
    private readonly Func<ImportedLayout?> _retailLayout;
    private readonly UiDatFont? _font;
    private readonly List<ConfirmEntry> _confirms = new();
    private readonly List<ModalEntry> _modals = new();
    private bool _disposed;

    /// <param name="root">The root the dialogs are added to.</param>
    /// <param name="retailLayout">A fresh retail confirmation layout on each call, or null when the art is not to hand.</param>
    /// <param name="font">The Classic font for a drawn box.</param>
    public PluginDialogHost(UiRoot root, Func<ImportedLayout?> retailLayout, UiDatFont? font)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _retailLayout = retailLayout ?? throw new ArgumentNullException(nameof(retailLayout));
        _font = font;
    }

    /// <summary>The dialog showing for an owner window, or for the ownerless queue when null.</summary>
    internal UiPluginDialog? ShownFor(UiElement? owner) => HeadFor(owner)?.View;

    /// <summary>Dialogs showing or queued.</summary>
    internal int PendingCount => _confirms.Count;

    /// <summary>
    /// Queues a dialog and returns a handle whose disposal answers it with
    /// its last button; null once the host is disposed, or when the dialog
    /// was due on screen at once and could not be shown (nothing is queued
    /// then and the answer never runs). An owner that is not
    /// a shown top-level window counts as none: the dialog is centred on the
    /// screen and blocks nothing. Button-count and caption validation is the
    /// caller's (the registry's) job.
    /// </summary>
    public IDisposable? Confirm(
        string title, string message, IReadOnlyList<string> buttons,
        UiElement? owner, Action<int> onResult)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(onResult);
        if (buttons.Count == 0)
            throw new ArgumentException("A dialog needs a button.", nameof(buttons));
        if (_disposed)
            return null;
        if (OwnerGone(owner))
            owner = null;

        var entry = new ConfirmEntry
        {
            Title = title, Message = message, Buttons = buttons, Owner = owner, OnResult = onResult,
        };
        _confirms.Add(entry);
        if (ReferenceEquals(HeadFor(owner), entry) && !TryShow(entry))
        {
            // Never shown, never queued: the plugin hears false, not an answer.
            entry.Done = true;
            _confirms.Remove(entry);
            return null;
        }
        return new ConfirmHandle(this, entry);
    }

    /// <summary>
    /// Blocks <paramref name="owner"/> until <paramref name="panel"/> is
    /// hidden or removed. The same panel again over the same owner changes
    /// nothing; over another owner the block moves. No owner, an unmounted
    /// one, or the panel itself blocks nothing. Unlike <c>Confirm</c>, a
    /// mounted-but-hidden owner is still blocked; the modal ends only when the
    /// panel hides or goes, or the owner is removed. A panel is not shown
    /// modal over a window it is itself blocked by (directly or through other
    /// modals): the two would block each other for good.
    /// </summary>
    public void ShowModal(UiElement panel, UiElement? owner)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (_disposed)
            return;
        if (owner is not null && BlockedBy(panel, owner))
            return;
        foreach (ModalEntry existing in _modals.Where(m => ReferenceEquals(m.Panel, panel)).ToArray())
        {
            if (ReferenceEquals(existing.Owner, owner))
            {
                _root.BringToFront(panel);
                return;
            }
            End(existing);
        }
        if (owner is null
            || ReferenceEquals(owner, panel)
            || !ReferenceEquals(owner.Parent, _root)
            || !ReferenceEquals(panel.Parent, _root))
            return;
        _modals.Add(new ModalEntry(panel, owner, _root.BlockWindow(owner, panel)));
        _root.BringToFront(panel);
    }

    /// <summary>Answers the dialogs of closed owners, keeps dialogs on their owners, and ends modals whose panel is gone.</summary>
    public void Tick()
    {
        foreach (ConfirmEntry entry in _confirms.ToArray())
        {
            if (entry.Done)
                continue;
            if (entry.Owner is not null && OwnerGone(entry.Owner))
                Finish(entry, entry.CancelIndex);
            else if (entry.View is null)
            {
                if (ReferenceEquals(HeadFor(entry.Owner), entry) && !TryShow(entry))
                    Finish(entry, entry.CancelIndex);
            }
            else
                entry.View.Place(entry.Owner, _root.EffectiveCanvasSize);
        }
        foreach (ModalEntry modal in _modals.ToArray())
        {
            if (!modal.Panel.Visible
                || !ReferenceEquals(modal.Panel.Parent, _root)
                || !ReferenceEquals(modal.Owner.Parent, _root))
                End(modal);
        }
    }

    /// <summary>Answers every dialog with its last button and ends every modal.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ConfirmEntry entry in _confirms.ToArray())
            Finish(entry, entry.CancelIndex);
        foreach (ModalEntry modal in _modals.ToArray())
            End(modal);
    }

    private bool OwnerGone(UiElement? owner) =>
        owner is not null && (!owner.Visible || !ReferenceEquals(owner.Parent, _root));

    private ConfirmEntry? HeadFor(UiElement? owner) =>
        _confirms.FirstOrDefault(entry => ReferenceEquals(entry.Owner, owner));

    // Whether window is blocked by panel, directly or through a chain of
    // modal panels each blocking the next.
    private bool BlockedBy(UiElement window, UiElement panel)
    {
        var seen = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<UiElement>();
        pending.Push(panel);
        while (pending.Count > 0)
        {
            UiElement blocker = pending.Pop();
            if (!seen.Add(blocker))
                continue;
            foreach (ModalEntry modal in _modals)
            {
                if (!ReferenceEquals(modal.Panel, blocker))
                    continue;
                if (ReferenceEquals(modal.Owner, window))
                    return true;
                pending.Push(modal.Owner);
            }
        }
        return false;
    }

    // Shows the dialog, or undoes what was done and returns false when the
    // view cannot be built or mounted.
    private bool TryShow(ConfirmEntry entry)
    {
        try
        {
            Show(entry);
            return true;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[UI] plugin dialog could not be shown: {error.Message}");
            entry.Block?.Dispose();
            entry.Block = null;
            if (entry.View is { } view)
            {
                view.Chosen = null;
                bool hadFocus = ReferenceEquals(_root.KeyboardFocus, view);
                if (ReferenceEquals(view.Parent, _root))
                    _root.RemoveChild(view);
                entry.View = null;
                if (hadFocus && entry.PreviousFocus is { } previous && _root.CanFocus(previous))
                    _root.SetKeyboardFocus(previous);
            }
            return false;
        }
    }

    private void Show(ConfirmEntry entry)
    {
        UiPluginDialog view = CreateView(entry);
        entry.View = view;
        entry.PreviousFocus = _root.KeyboardFocus;
        view.Chosen = index => Finish(entry, index);
        _root.AddChild(view);
        _root.BringToFront(view);
        view.Place(entry.Owner, _root.EffectiveCanvasSize);
        if (entry.Owner is { } owner)
            entry.Block = _root.BlockWindow(owner, view);
        _root.SetKeyboardFocus(view);
    }

    private UiPluginDialog CreateView(ConfirmEntry entry)
    {
        if (entry.Owner is UiPluginMarkupPanel panel && panel.Palette is not null)
        {
            return new UiDrawnPluginDialog(
                entry.Title, entry.Message, entry.Buttons, panel.ModernFont ?? _font, () => panel.Palette);
        }
        try
        {
            if (_retailLayout() is { } layout
                && UiRetailPluginDialog.TryCreate(layout, _retailLayout, entry.Title, entry.Message, entry.Buttons)
                    is { } retail)
                return retail;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[UI] plugin dialog drawn without retail art: {error.Message}");
        }
        return new UiDrawnPluginDialog(entry.Title, entry.Message, entry.Buttons, _font, static () => null);
    }

    private void Finish(ConfirmEntry entry, int index)
    {
        if (entry.Done)
            return;
        entry.Done = true;
        _confirms.Remove(entry);
        if (entry.View is { } view)
        {
            view.Chosen = null;
            // A button press takes the focus off the dialog before it
            // answers, so an answer by press counts as having it unless the
            // focus has since gone somewhere else.
            bool hadFocus = ReferenceEquals(_root.KeyboardFocus, view)
                || (view.AnsweredByPress && _root.KeyboardFocus is null);
            entry.Block?.Dispose();
            entry.Block = null;
            _root.RemoveChild(view);
            entry.View = null;
            if (hadFocus && entry.PreviousFocus is { } previous && _root.CanFocus(previous))
                _root.SetKeyboardFocus(previous);
            // One that cannot be shown stays queued; Tick tries once more and
            // then answers it with its last button, after this answer.
            if (!_disposed && HeadFor(entry.Owner) is { } next && !OwnerGone(next.Owner))
                TryShow(next);
        }
        try
        {
            entry.OnResult(index);
        }
        catch (Exception error)
        {
            Console.WriteLine($"[UI] plugin dialog answer handler failed: {error.Message}");
        }
    }

    private void End(ModalEntry modal)
    {
        if (_modals.Remove(modal))
            modal.Block.Dispose();
    }

    private sealed class ConfirmHandle(PluginDialogHost host, ConfirmEntry entry) : IDisposable
    {
        public void Dispose() => host.Finish(entry, entry.CancelIndex);
    }
}
