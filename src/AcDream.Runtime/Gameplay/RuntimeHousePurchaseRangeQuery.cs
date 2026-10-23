namespace AcDream.Runtime.Gameplay;

public static class RuntimeHousePurchaseRangeQuery
{
    public static void EnforceRange(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        RuntimeHousePurchaseState purchase = runtime.HousePurchaseOwner;
        if (purchase.View.Snapshot.Profile is { } profile
            && RuntimeUseRange.ShouldClose(runtime, profile.SlumlordGuid))
        {
            purchase.Close();
        }
    }
}
