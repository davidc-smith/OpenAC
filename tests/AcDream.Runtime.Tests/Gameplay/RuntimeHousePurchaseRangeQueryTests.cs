using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using static AcDream.Runtime.Tests.Gameplay.UseRangeTestWorld;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseRangeQueryTests
{
    private const uint Player = 0x50000001u;
    private const uint Slumlord = 0x70000100u;
    private const uint Landblock = 0x01010001u;

    private static GameRuntime Opened(out RuntimeEntityRecord player, out RuntimeEntityRecord slumlord)
    {
        GameRuntime runtime = Create();
        runtime.PlayerIdentity.ServerGuid = Player;
        player = Add(runtime, Player, Landblock, 100f, 100f);
        slumlord = Add(runtime, Slumlord, Landblock, 102f, 100f, useRadius: 3f);
        runtime.HousePurchaseOwner.ApplyProfile(
            RuntimeHousePurchaseWiringTests.Profile(Slumlord));
        return runtime;
    }

    [Fact]
    public void InRange_StaysOpen()
    {
        using GameRuntime runtime = Opened(out _, out _);

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.True(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void BeyondTheUseRadius_ButWithinNineMetres_StaysOpen()
    {
        using GameRuntime runtime = Opened(out RuntimeEntityRecord player, out _);
        SetPosition(player, Landblock, 110f, 100f);

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.True(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void WalkingAway_Closes()
    {
        using GameRuntime runtime = Opened(out RuntimeEntityRecord player, out _);
        SetPosition(player, Landblock, 122f, 100f);

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void SlumlordGone_Closes()
    {
        using GameRuntime runtime = Opened(out _, out RuntimeEntityRecord slumlord);
        Assert.True(runtime.EntityObjects.Entities.RemoveActive(slumlord));

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void TeleportQueued_Closes()
    {
        using GameRuntime runtime = Opened(out _, out _);
        Assert.True(runtime.TransitOwner.TryQueueTeleportStart(1));

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void NothingOpen_DoesNothing()
    {
        using GameRuntime runtime = Create();

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }
}
