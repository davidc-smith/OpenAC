using AcDream.Core.Items;

namespace AcDream.Runtime.Gameplay;

public static class RuntimeVendorRangeQuery
{
    public static void EnforceRange(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        VendorState vendor = runtime.InventoryOwner.Vendor;
        uint vendorId = vendor.VendorId;
        if (vendorId != 0u && RuntimeUseRange.ShouldClose(runtime, vendorId))
            vendor.Close();
    }
}
