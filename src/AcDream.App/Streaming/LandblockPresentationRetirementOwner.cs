using AcDream.Core.Lighting;
using AcDream.Core.Rendering;
using AcDream.Core.World;

namespace AcDream.App.Streaming;

public sealed class LandblockPresentationRetirementOwner
{
    private readonly LandblockRenderPublisher _render;
    private readonly LandblockStaticPresentationPublisher _staticPresentation;
    private readonly LightingHookSink _lighting;
    private readonly TranslucencyFadeManager _translucency;
    // The budgeted steps run once per entity; delegates made once, not per
    // step.
    private readonly Action<WorldEntity> _removeLighting;
    private readonly Action<WorldEntity> _removeTranslucency;
    private readonly Action<WorldEntity> _removePluginProjection;
    private static readonly Func<WorldEntity, bool> IsStatic =
        static entity => entity.ServerGuid == 0;

    public LandblockPresentationRetirementOwner(
        LandblockRenderPublisher render,
        LandblockStaticPresentationPublisher staticPresentation,
        LightingHookSink lighting,
        TranslucencyFadeManager translucency)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _staticPresentation = staticPresentation
            ?? throw new ArgumentNullException(nameof(staticPresentation));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _translucency = translucency
            ?? throw new ArgumentNullException(nameof(translucency));
        _removeLighting = _staticPresentation.RemoveLighting;
        _removeTranslucency = _staticPresentation.RemoveTranslucency;
        _removePluginProjection = _staticPresentation.RemovePluginProjection;
        if (!_staticPresentation.MatchesResources(_lighting, _translucency))
        {
            throw new ArgumentException(
                "The retirement resources must be those owned by the static " +
                "presentation publisher.",
                nameof(staticPresentation));
        }
    }

    internal bool Matches(
        LandblockRenderPublisher render,
        LandblockStaticPresentationPublisher staticPresentation) =>
        ReferenceEquals(_render, render)
        && ReferenceEquals(_staticPresentation, staticPresentation)
        && _staticPresentation.MatchesResources(_lighting, _translucency);

    public void Advance(LandblockRetirementTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        ticket.RunForEachEntity(
            LandblockRetirementStage.EntityLighting,
            static entity => entity.ServerGuid == 0,
            _staticPresentation.RemoveLighting);
        ticket.RunForEachEntity(
            LandblockRetirementStage.EntityTranslucency,
            static entity => entity.ServerGuid == 0,
            _staticPresentation.RemoveTranslucency);
        ticket.RunForEachEntity(
            LandblockRetirementStage.PluginProjection,
            static entity => entity.ServerGuid == 0,
            _staticPresentation.RemovePluginProjection);

        if (ticket.Kind == LandblockRetirementKind.Full)
        {
            ticket.RunOnce(
                LandblockRetirementStage.Terrain,
                () => _render.RemoveTerrain(ticket.LandblockId));
        }
        ticket.RunOnce(
            LandblockRetirementStage.CellVisibility,
            () => _render.RemoveCellVisibility(ticket.LandblockId));
        ticket.RunOnce(
            LandblockRetirementStage.BuildingRegistry,
            () => _render.RemoveBuildingRegistry(ticket.LandblockId));
        ticket.RunOnce(
            LandblockRetirementStage.EnvironmentCells,
            () => _render.RemoveEnvironmentCells(ticket.LandblockId));
    }

    internal LandblockRetirementOperationResult AdvanceOne(
        LandblockRetirementTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.NextIncompleteStage switch
        {
            LandblockRetirementStage.EntityLighting =>
                ticket.RunEntityStep(
                    LandblockRetirementStage.EntityLighting,
                    IsStatic,
                    _removeLighting),
            LandblockRetirementStage.EntityTranslucency =>
                ticket.RunEntityStep(
                    LandblockRetirementStage.EntityTranslucency,
                    IsStatic,
                    _removeTranslucency),
            LandblockRetirementStage.PluginProjection =>
                ticket.RunEntityStep(
                    LandblockRetirementStage.PluginProjection,
                    IsStatic,
                    _removePluginProjection),
            LandblockRetirementStage.Terrain =>
                ticket.RunOnceStep(
                    LandblockRetirementStage.Terrain,
                    () => _render.RemoveTerrain(ticket.LandblockId)),
            LandblockRetirementStage.CellVisibility =>
                ticket.RunOnceStep(
                    LandblockRetirementStage.CellVisibility,
                    () => _render.RemoveCellVisibility(ticket.LandblockId)),
            LandblockRetirementStage.BuildingRegistry =>
                ticket.RunOnceStep(
                    LandblockRetirementStage.BuildingRegistry,
                    () => _render.RemoveBuildingRegistry(ticket.LandblockId)),
            LandblockRetirementStage.EnvironmentCells =>
                ticket.RunOnceStep(
                    LandblockRetirementStage.EnvironmentCells,
                    () => _render.RemoveEnvironmentCells(ticket.LandblockId)),
            _ => LandblockRetirementOperationResult.NoWork,
        };
    }
}
