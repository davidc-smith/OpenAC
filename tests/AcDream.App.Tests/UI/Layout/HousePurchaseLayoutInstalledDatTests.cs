using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;
using DatReaderWriter;
using DatReaderWriter.Options;

namespace AcDream.App.Tests.UI.Layout;

[Trait("Lane", "InstalledDat")]
public sealed class HousePurchaseLayoutInstalledDatTests
{
    [InstalledDatFact]
    public void Layout2100000E_BindsAndCarriesTheRetailCaptions()
    {
        string datDirectory = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "Asheron's Call");
        using var dats = new DatCollection(datDirectory, DatAccessType.Read);
        ImportedLayout layout = Assert.IsType<ImportedLayout>(LayoutImporter.Import(
            dats,
            HousePurchaseUiController.LayoutId,
            HousePurchaseUiController.RootId,
            static _ => (0u, 0, 0),
            null));

        Assert.IsType<UiItemList>(layout.FindElement(HousePurchaseUiController.BuyItemsListId));
        Assert.IsType<UiItemList>(layout.FindElement(HousePurchaseUiController.MaintenanceItemsListId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.BuyRequirementsTextId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.MaintenanceRequirementsTextId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.BuyOwnerId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.MaintenanceOwnerId));
        Assert.NotNull(layout.FindElement(HousePurchaseUiController.CloseButtonId));

        Assert.Equal("Buy", Caption(layout.FindElement(HousePurchaseUiController.BuyTabId)));
        Assert.Equal("Maintenance", Caption(layout.FindElement(HousePurchaseUiController.MaintenanceTabId)));
        Assert.Equal("Buy", Caption(layout.FindElement(HousePurchaseUiController.BuyButtonId)));
        Assert.Equal("Maintenance", Caption(layout.FindElement(HousePurchaseUiController.MaintenanceButtonId)));

        var objects = new ClientObjectTable();
        var state = new RuntimeHousePurchaseState(objects, static () => 0u);
        using HousePurchaseUiController? controller = HousePurchaseUiController.Bind(layout, new(
            state.View, objects, state.Stage, state.Unstage, state.ClearStaged, () => state.Revalidate(), state.Close,
            static (_, _) => { }, static (_, _) => { }, static (_, _, _, _, _) => 0u,
            static _ => { }, static _ => { }, ItemTooltipCaptionNames.Resolve));
        Assert.NotNull(controller);
    }

    private static string? Caption(UiElement? element) => element switch
    {
        UiButton button => button.Label,
        UiText text => string.Concat(text.LinesProvider().Select(line => line.Text)),
        _ => null,
    };
}
