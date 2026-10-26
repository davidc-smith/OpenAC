using AcDream.App.Spells;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    private sealed class NoSpellActions : ISpellDeckActions
    {
        public void CastSpell(uint spellId) { }
        public void CastItemSpell(uint itemId) { }
        public void SetFavorite(int tab, int position, uint spellId) { }
        public void RemoveFavorite(int tab, uint spellId) { }
        public void ExamineSpell(uint spellId) { }
        public void ShowMessage(string text) { }
    }

    /// <summary>The Command Deck, the Spell Deck, the layout and the character sync,
    /// wired the way <c>RetailUiRuntime</c> wires them.</summary>
    private sealed class SpellDeckRig : IDisposable
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly SettingsStore Store;
        public readonly UiSpellDeck Spell;
        public readonly SpellDeckController Controller;
        public readonly RetailWindowLayoutPersistence Persistence;
        public readonly SpellDeckCharacterSync Sync;
        public string Key = "default";

        public SpellDeckRig(string path)
        {
            Store = new SettingsStore(path);
            UiCommandDeck deck = MountDeck(Root);
            SpellDeckCharacterSync? sync = null;
            SpellDeckDock? dock = null;
            Controller = new SpellDeckController(
                new NoSpellActions(), _ => [], () => (ItemSpell?)null, () => 12,
                SpellDeckSettings.Default, s => sync?.Save(s), () => false, () => dock?.CanAddRow() ?? true);
            Spell = new UiSpellDeck(Controller, _ => 0u, _ => null, _ => "", () => null, () => 0.0,
                () => false, () => { }, _ => { });
            RetailWindowHandle spellHandle = RetailWindowFrame.Mount(
                Root, Spell, _ => (0u, 0, 0), UiSpellDeck.MountOptions(DeckLeft, 40f) with { Visible = true });
            Assert.True(Root.WindowManager.TryGet(WindowNames.CommandDeck, out RetailWindowHandle deckHandle));
            Persistence = new RetailWindowLayoutPersistence(
                Root.WindowManager, Store, () => Key, () => (1600, 900),
                [WindowNames.CommandDeck, WindowNames.SpellDeck]);
            Persistence.SetCaptureGate(WindowNames.SpellDeck, () => !Controller.Attached);
            Persistence.ResetToDefaults();
            dock = new SpellDeckDock(
                new SpellDeckWindowDockHost(Root, () => Persistence, deck, deckHandle, Spell, spellHandle), Controller);
            sync = new SpellDeckCharacterSync(Controller, dock, Store, () => Key, () => Persistence.RestoredCharacter);
            Sync = sync;
        }

        /// <summary>The runtime's frame.</summary>
        public void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Sync.Tick();
                Spell.Refresh();
            }
        }

        /// <summary>A character enters the world: its key appears, frames run, then the
        /// login restores the layout (<c>RetailUiRuntime.RestoreLayout</c>'s order).</summary>
        public void EnterWorld(string key)
        {
            Key = key;
            Frames(3);
            Sync.LoadIfChanged();
            Persistence.RestoreAll();
            Sync.AfterRestore();
            Frames(3);
        }

        public void Dispose() => Persistence.Dispose();
    }

    private static UiWindowLayout StoreDetachedSpellDeck(
        SettingsStore store, string key, float left, float top, float scale, int rows)
    {
        store.SaveSpellDeck(key, new StoredSpellDeck(false,
            Enumerable.Range(1, rows).Select(i => (i, (i - 1) % 8)).ToArray()));
        SpellDeckGeometry g = SpellDeckGeometry.Create(scale, 1f, rows);
        var layout = new UiWindowLayout(left, top, g.Width, g.Height, false, false, false);
        store.SaveWindowLayout(key, "1600x900", WindowNames.SpellDeck, layout);
        return layout;
    }

    private static void AssertSpellDeckAt(SpellDeckRig rig, UiWindowLayout expected, string key, float scale, int rows)
    {
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout(key, "1600x900", WindowNames.SpellDeck, default));
        Assert.Equal(expected.X, saved.X, 2);
        Assert.Equal(expected.Y, saved.Y, 2);
        Assert.Equal(expected.Width, saved.Width, 2);
        Assert.Equal(expected.Height, saved.Height, 2);

        Assert.Equal(expected.X, rig.Spell.Left, 2);
        Assert.Equal(expected.Y, rig.Spell.Top, 2);
        Assert.Equal(rows, rig.Spell.Geometry.Rows);
        Assert.Equal(scale, rig.Spell.Geometry.Scale, 3);
        Assert.Equal(rig.Spell.Width, rig.Spell.Geometry.Width, 2);
        Assert.Equal(rig.Spell.Height, rig.Spell.Geometry.Height, 2);
    }

    [Fact]
    public void SpellDeck_DetachedPlacementSurvivesLogin()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 5);
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);

        rig.EnterWorld("Bob");

        Assert.False(rig.Controller.Attached);
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }

    [Fact]
    public void SpellDeck_DetachedToDetachedSwitchKeepsBothPlacements()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 5);
        UiWindowLayout carol = StoreDetachedSpellDeck(store, "Carol", 520f, 260f, 0.9f, 2);
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);

        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);

        rig.EnterWorld("Carol");
        Assert.False(rig.Controller.Attached);
        AssertSpellDeckAt(rig, carol, "Carol", 0.9f, 2);

        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }
}
