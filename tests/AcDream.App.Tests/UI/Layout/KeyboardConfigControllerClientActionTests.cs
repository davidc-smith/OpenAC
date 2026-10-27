using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Input;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;
using Xunit;

namespace AcDream.App.Tests.UI.Layout;

public sealed class KeyboardConfigControllerClientActionTests
{
    private static (uint, int, int) NoTex(uint _) => (0, 0, 0);

    private static ElementInfo? Find(ElementInfo n, uint id)
    {
        if (n.Id == id) return n;
        foreach (ElementInfo c in n.Children)
        {
            ElementInfo? f = Find(c, id);
            if (f is not null) return f;
        }
        return null;
    }

    private static Func<uint, uint, UiElement?> MakeTemplateResolver()
    {
        ElementInfo root = FixtureLoader.LoadKeyboardConfigInfos();
        return (layoutId, elementId) =>
        {
            if (layoutId != KeyboardConfigController.LayoutId) return null;
            ElementInfo? info = Find(root, elementId);
            return info is null ? null : LayoutImporter.Build(info, NoTex, null).Root;
        };
    }

    private static string? Resolve(uint _, uint hash) => "Retail spell";

    private static (KeyboardConfigController C, List<(InputAction, IReadOnlyList<Binding>)> Sets,
        Dictionary<InputAction, List<Binding>> Mapped, Action<KeyChord?>[] Pending,
        List<string> Messages, (string, Action<bool>)?[] Confirm)
        Build(Dictionary<InputAction, List<Binding>>? mapped = null)
    {
        mapped ??= new();
        var sets = new List<(InputAction, IReadOnlyList<Binding>)>();
        var pending = new Action<KeyChord?>[1];
        var messages = new List<string>();
        var confirm = new (string, Action<bool>)?[1];
        var bindings = new KeyboardConfigController.Bindings(
            CurrentForAction: a => mapped.TryGetValue(a, out var v) ? v : Array.Empty<Binding>(),
            SetForAction: (a, v) => { mapped[a] = v.ToList(); sets.Add((a, v)); },
            CurrentForUnmapped: _ => Array.Empty<KeyChord>(),
            SetForUnmapped: (_, _) => { },
            BeginCapture: cb => pending[0] = cb,
            Save: () => { },
            Toggle: () => { },
            ResolveTemplate: (key, vars) => key switch
            {
                "ID_ActionKeyMap_OverwriteExistingBinding" =>
                    "overwrite " + vars[DatStringResolver.ComputeHash("ACTION")],
                "ID_ActionKeyMap_Binding" => "binding",
                _ => null,
            },
            ShowMessage: m => messages.Add(m),
            ConfirmOverwrite: (m, r) => confirm[0] = (m, r));
        RetailActionIdentityTable.TryGetRetailIdentity(InputAction.UseSpellSlot_1, out var id);
        var snapshot = new RetailActionMapSnapshot(new[]
        {
            new RetailActionMapRow(id.InputMapId, id.ActionId, RetailActionClass.Combat, 1, 0,
                Array.Empty<RetailKeyChord>()),
        });
        ImportedLayout layout = FixtureLoader.LoadKeyboardConfig();
        var c = KeyboardConfigController.Bind(layout, snapshot,
            MakeTemplateResolver(), Resolve, bindings)!;
        return (c, sets, mapped, pending, messages, confirm);
    }

    private static IEnumerable<KeyboardConfigController.RowView> ClientRows(KeyboardConfigController c) =>
        c.Rows.Where(r => r.MappedAction is { } a && ClientActionCatalog.Entries.Any(e => e.Action == a));

    [Fact]
    public void CombatPage_ListsEveryCatalogEntryInOrder()
    {
        var (c, _, _, _, _, _) = Build();
        var rows = ClientRows(c).ToList();
        Assert.Equal(ClientActionCatalog.Entries.Count, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(ClientActionCatalog.Entries[i].Action, rows[i].MappedAction);
            Assert.Equal(ClientActionCatalog.Entries[i].Label, rows[i].Label);
        }
    }

    [Fact]
    public void SettingChordOnClientRow_BindsWithMagicCombatScopeAndPress()
    {
        var (c, sets, _, pending, _, _) = Build();
        var row = ClientRows(c).Single(r => r.MappedAction == InputAction.CastItemSpell);
        var f7 = new KeyChord(Key.F7, ModifierMask.None);
        row.KeyButtons[0].OnClick!.Invoke();
        pending[0](f7);
        var (action, value) = Assert.Single(sets);
        Assert.Equal(InputAction.CastItemSpell, action);
        Assert.Equal(new Binding(f7, InputAction.CastItemSpell, ActivationType.Press, InputScope.MagicCombat),
            Assert.Single(value));
    }

    [Fact]
    public void CapturingChordHeldByRetailSpellRow_OpensConfirm()
    {
        var one = new KeyChord(Key.Number1, ModifierMask.None);
        var (c, _, _, pending, messages, confirm) = Build(new()
        {
            [InputAction.UseSpellSlot_1] = new() { new Binding(one, InputAction.UseSpellSlot_1,
                ActivationType.Press, InputScope.MagicCombat) },
        });
        var row = ClientRows(c).First(r => r.MappedAction == InputAction.UseSpellRow2Slot1);
        row.KeyButtons[0].OnClick!.Invoke();
        pending[0](one);
        Assert.Empty(messages);
        Assert.NotNull(confirm[0]);
        Assert.Contains("Retail spell", confirm[0]!.Value.Item1);
    }

    [Fact]
    public void CapturingChordHeldByAnotherClientRow_IsAnOrdinaryConflict_NotNonBindable()
    {
        var shift2 = new KeyChord(Key.Number2, ModifierMask.Shift);
        var (c, _, _, pending, messages, confirm) = Build(new()
        {
            [InputAction.UseSpellRow2Slot2] = new() { new Binding(shift2, InputAction.UseSpellRow2Slot2,
                ActivationType.Press, InputScope.MagicCombat) },
        });
        var row = ClientRows(c).First(r => r.MappedAction == InputAction.UseSpellRow2Slot1);
        row.KeyButtons[0].OnClick!.Invoke();
        pending[0](shift2);
        Assert.Empty(messages);
        Assert.NotNull(confirm[0]);
    }
}
