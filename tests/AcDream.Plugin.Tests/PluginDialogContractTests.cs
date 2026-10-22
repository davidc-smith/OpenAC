// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The dialog members are additive: a registry written before them still
/// compiles and answers false without calling back, and a dialog without
/// its own buttons has OK and Cancel.
/// </summary>
public sealed class PluginDialogContractTests
{
    private sealed class BareRegistry : IScopedUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;
    }

    [Fact]
    public void ARegistryWrittenBeforeDialogsAnswersFalse()
    {
        IScopedUiRegistry registry = new BareRegistry();
        var owner = new PluginUiOwner("acdream.test", "Test");
        bool called = false;

        Assert.False(((IUiRegistry)registry).Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.False(((IUiRegistry)registry).ShowModal("options", "main"));
        Assert.Null(registry.Confirm(owner, new PluginDialog("T", "M"), _ => called = true));
        Assert.False(registry.ShowModal(owner, "options", "main"));
        Assert.False(called);
    }

    [Fact]
    public void TheHostThatDrawsNothingAnswersFalse()
    {
        IScopedUiRegistry registry = NoOpUiRegistry.Instance;
        bool called = false;

        Assert.False(((IUiRegistry)registry).Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.Null(registry.Confirm(new PluginUiOwner("p", "P"), new PluginDialog("T", "M"), _ => called = true));
        Assert.False(((IUiRegistry)registry).ShowModal("options"));
        Assert.False(called);
    }

    [Fact]
    public void ADialogWithoutButtonsHasOkAndCancel()
    {
        Assert.Equal(["OK", "Cancel"], new PluginDialog("T", "M").EffectiveButtons);
        Assert.Same(PluginDialog.DefaultButtons, new PluginDialog("T", "M").EffectiveButtons);
        Assert.Equal(["Yes"], new PluginDialog("T", "M", ["Yes"]).EffectiveButtons);
    }
}
