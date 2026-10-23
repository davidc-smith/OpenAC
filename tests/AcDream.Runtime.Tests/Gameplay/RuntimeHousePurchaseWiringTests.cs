using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;
using static AcDream.Runtime.Tests.Gameplay.UseRangeTestWorld;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseWiringTests
{
    internal static GameEvents.HouseProfile Profile(uint slumlord) =>
        new(slumlord, 1u, 0u, 1u, -1, -1, -1, -1, false, 1u, "",
            [new GameEvents.HousePayment(10, 0, 273u, "Pyreal", "Pyreals")], []);

    [Fact]
    public void GameRuntime_ExposesThePurchaseStateAndItsView()
    {
        using GameRuntime runtime = Create();

        runtime.HousePurchaseOwner.ApplyProfile(Profile(0x70000100u));

        Assert.True(runtime.HousePurchase.Snapshot.IsOpen);
    }
}
