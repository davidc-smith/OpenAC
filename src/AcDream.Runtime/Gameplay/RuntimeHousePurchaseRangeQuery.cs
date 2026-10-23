namespace AcDream.Runtime.Gameplay;

public static class RuntimeHousePurchaseRangeQuery
{
    /// <summary>The slumlord window closes at this distance, whatever the
    /// slumlord's own use radius.</summary>
    public const float CloseDistance = 9f;

    public static void EnforceRange(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        RuntimeHousePurchaseState purchase = runtime.HousePurchaseOwner;
        if (purchase.View.Snapshot.Profile is { } profile
            && RuntimeUseRange.ShouldClose(runtime, profile.SlumlordGuid, CloseDistance))
        {
            purchase.Close();
        }
    }
}
