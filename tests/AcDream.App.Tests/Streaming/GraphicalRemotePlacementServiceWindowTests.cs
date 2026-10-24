using AcDream.App.Streaming;
using AcDream.Core.World;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Tests.Streaming;

public sealed class GraphicalRemotePlacementServiceWindowTests
{
    private const uint LandblockHighWord = 0x0A0B0000u;
    private const uint CanonicalLandblock = LandblockHighWord | 0xFFFFu;
    private const uint OutdoorCell = LandblockHighWord | 0x0001u;

    [Fact]
    public void ConstructorRequiresPresentationFence()
    {
        var state = new GpuWorldState();

        Assert.Throws<ArgumentNullException>(() =>
            new GraphicalRemotePlacementServiceWindow(state, null!, static _ => true));
        Assert.DoesNotContain(
            typeof(GraphicalRemotePlacementServiceWindow)
                .GetConstructors(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic),
            constructor => constructor.GetParameters().Length == 1);
    }

    [Fact]
    public void True_WhenTheLandblockIsNearTier()
    {
        var state = new GpuWorldState();
        state.AddLandblock(new LoadedLandblock(
            CanonicalLandblock,
            new LandBlock(),
            Array.Empty<AcDream.Core.World.WorldEntity>()));
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            static _ => true,
            static _ => true);

        Assert.True(window.IsWithinServiceWindow(OutdoorCell));
    }

    [Fact]
    public void False_WhenTheSessionHasNotPublishedTheCellsCollision()
    {
        // Drawn and near is not enough: a body cannot be put down where the
        // session has no collision yet. The cell asked about is passed on
        // as it is.
        var state = new GpuWorldState();
        state.AddLandblock(new LoadedLandblock(
            CanonicalLandblock,
            new LandBlock(),
            Array.Empty<AcDream.Core.World.WorldEntity>()));
        uint asked = 0;
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            static _ => true,
            cell =>
            {
                asked = cell;
                return false;
            });

        Assert.False(window.IsWithinServiceWindow(OutdoorCell));
        Assert.Equal(OutdoorCell, asked);
    }

    [Fact]
    public void False_WhenTheLandblockWasNeverPublished()
    {
        var state = new GpuWorldState();
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            static _ => true,
            static _ => true);

        Assert.False(window.IsWithinServiceWindow(OutdoorCell));
    }

    [Fact]
    public void False_WhenTheLandblockIsOnlyPendingNotYetCollisionPublished()
    {
        var state = new GpuWorldState();
        Assert.False(
            state.AddEntitiesToExistingLandblock(
                LandblockHighWord, Array.Empty<AcDream.Core.World.WorldEntity>()));
        Assert.True(state.IsNearTierOrPending(CanonicalLandblock));
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            static _ => true,
            static _ => true);

        Assert.False(window.IsWithinServiceWindow(OutdoorCell));
    }

    [Fact]
    public void False_AfterTheLandblockRetiresViaDetachNearLayer()
    {
        var state = new GpuWorldState();
        state.AddLandblock(new LoadedLandblock(
            CanonicalLandblock,
            new LandBlock(),
            Array.Empty<AcDream.Core.World.WorldEntity>()));
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            static _ => true,
            static _ => true);
        Assert.True(window.IsWithinServiceWindow(OutdoorCell));

        _ = state.DetachNearLayer(LandblockHighWord);

        Assert.False(window.IsWithinServiceWindow(OutdoorCell));
    }

    [Fact]
    public void False_WhenExactPresentationPrefixIsIncomplete()
    {
        var state = new GpuWorldState();
        state.AddLandblock(new LoadedLandblock(
            CanonicalLandblock,
            new LandBlock(),
            Array.Empty<AcDream.Core.World.WorldEntity>()));
        uint observed = 0;
        var window = new GraphicalRemotePlacementServiceWindow(
            state,
            id =>
            {
                observed = id;
                return false;
            },
            static _ => true);

        Assert.False(window.IsWithinServiceWindow(OutdoorCell));
        Assert.Equal(CanonicalLandblock, observed);
    }
}
