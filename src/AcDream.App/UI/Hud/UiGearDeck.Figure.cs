using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.Core.Items;

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

    /// <summary>Radians the figure turns per point dragged, at scale 1.</summary>
    internal const float TurnPerPoint = 0.012f;

    /// <summary>The pointer x the last turn step was measured from, while a press on the figure turns it.</summary>
    private float? _turnFromX;

    /// <summary>
    /// The geometry's hit with the body filled in: a Figure hit's Index is the
    /// body region under the point as its <see cref="EquipMask"/> bits (0 for
    /// none), so a move to another region is a new hover, and a press keeps
    /// the region it landed on.
    /// </summary>
    internal GearDeckHit HitAt(float x, float y)
    {
        GearDeckHit hit = _g.HitAt(x, y);
        return hit.Zone == GearZone.Figure ? hit with { Index = (int)FigureRegionAt(x, y) } : hit;
    }

    /// <summary>The body region at a deck point through the retail click map, sampled 1:1 at scale 1;
    /// none without a map, off the map, or while the figure is turned or zoomed.</summary>
    private EquipMask FigureRegionAt(float x, float y)
    {
        if (_b.ClickMap is not { } map || !FigurePose.IsDefault) return EquipMask.None;
        DeckRect m = _g.ClickMap;
        if (!m.Contains(x, y)) return EquipMask.None;
        return map.GetBodyLocation((int)((x - m.X) / _scale), (int)((y - m.Y) / _scale));
    }

    /// <summary>The item worn outermost on a body region, as the retail doll picks it; null for none or bare skin.</summary>
    private uint? WornOnBody(EquipMask region)
    {
        if (region == EquipMask.None) return null;
        uint player = _b.PlayerGuid();
        uint id = PaperdollSelectionPolicy.GetUpperInventoryObject(_b.Objects(), player, region);
        return id == 0u || id == player ? null : id;
    }

    /// <summary>Turns the figure as a held press moves; true while a turn is under way.</summary>
    private bool TurnTo(float x)
    {
        if (_turnFromX is not float from) return false;
        FigurePose = FigurePose.Turned((x - from) / _scale * TurnPerPoint);
        _turnFromX = x;
        return true;
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
