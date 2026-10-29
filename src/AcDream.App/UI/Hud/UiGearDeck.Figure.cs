using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiGearDeck
{
    private bool _figureAttached;
    private bool _figureUnavailableLogged;
    private GpuTextureSlot _figureTexture = GpuTextureSlot.Unassigned;

    /// <summary>Whether the shared paper doll draws into this deck; without it Figure view falls back to Slots.</summary>
    internal bool FigureAttached => _figureAttached;

    /// <summary>The doll texture the presenter last published here.</summary>
    internal GpuTextureSlot FigureTexture => _figureTexture;

    /// <summary>How the figure is turned and zoomed; <see cref="PaperdollPose.Default"/> at rest.</summary>
    internal PaperdollPose FigurePose { get; private set; } = PaperdollPose.Default;

    /// <summary>The effective equipment view: Figure only while a figure is attached.</summary>
    private bool ShowsFigureView => _figureAttached && _b.Session.Settings.Equip == GearEquipView.Figure;

    /// <summary>The composition calls this once the paper doll renderer is built.</summary>
    internal void AttachFigure()
    {
        _figureAttached = true;
        SyncGeometry();
    }

    /// <summary>The composition calls this when the paper doll renderer is released.</summary>
    internal void DetachFigure()
    {
        _figureAttached = false;
        _figureTexture = GpuTextureSlot.Unassigned;
        ResetFigurePose();
        SyncGeometry();
    }

    internal void SetFigureTexture(GpuTextureSlot slot) => _figureTexture = slot;

    /// <summary>
    /// The figure's size in points while it is on screen: the deck and every
    /// ancestor visible, a player known, unfolded in Figure view. The doll is
    /// rendered at this size, as the retail viewport renders at its own.
    /// </summary>
    internal bool TryGetFigureSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!_g.Input.Figure || _g.Input.Folded || _model.Packs.Count == 0) return false;
        for (UiElement? e = this; e is not null; e = e.Parent)
            if (!e.Visible) return false;
        width = (int)MathF.Round(_g.Figure.W);
        height = (int)MathF.Round(_g.Figure.H);
        return width > 0 && height > 0;
    }

    private void ResetFigurePose() => FigurePose = PaperdollPose.Default;

    /// <summary>One log line the first time the deck opens wanting the figure without one.</summary>
    private void NoteFigureUnavailable()
    {
        if (_figureAttached || _figureUnavailableLogged || _b.Session.Settings.Equip != GearEquipView.Figure) return;
        _figureUnavailableLogged = true;
        _b.Log?.Invoke("[gear-deck] figure view unavailable; showing Slots");
    }
}
