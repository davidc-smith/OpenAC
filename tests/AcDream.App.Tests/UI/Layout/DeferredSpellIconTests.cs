using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Layout;

// Spell icons are composed (several surfaces decoded, blended and uploaded)
// the first time a row is drawn or read, not when a list is rebuilt: the
// login spellbook message rebuilt every list and composed every icon, shown
// or not, on the frame it arrived.
public sealed class DeferredSpellIconTests
{
    [Fact]
    public void CatalogSlot_MakesItsIconOnceWhenFirstNeeded()
    {
        int calls = 0;
        var slot = new UiCatalogSlot { EntryId = 7u, CatalogIconSource = () => { calls++; return 77u; } };

        Assert.Equal(0, calls);
        Assert.Equal(77u, slot.CatalogIconTexture);
        Assert.Equal(77u, slot.CatalogIconTexture);
        Assert.Equal(1, calls);
        slot.CatalogIconTexture = 5u;
        Assert.Equal(5u, slot.CatalogIconTexture);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TextureElement_MakesItsTextureOnceWhenFirstNeeded()
    {
        int calls = 0;
        var element = new UiTextureElement { TextureSource = () => { calls++; return 9u; } };

        Assert.Equal(0, calls);
        Assert.Equal(9u, element.Texture);
        Assert.Equal(9u, element.Texture);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EffectsRebuild_ComposesNoIconUntilARowIsRead()
    {
        var table = SpellTable.LoadFromReader(new StringReader(
            "Spell ID,Name,Flags [Hex]\n42,Boon,0x4\n43,Bane,0x4\n"));
        var spellbook = new Spellbook(table);
        var root = new UiPanel { Width = 160f, Height = 100f };
        var list = new UiItemList(NoTex) { Width = 160f, Height = 64f };
        var info = new UiText { Width = 160f, Height = 32f };
        root.AddChild(list);
        root.AddChild(info);
        var layout = new ImportedLayout(root, new Dictionary<uint, UiElement>
        {
            [EffectsUiController.ListId] = list,
            [EffectsUiController.InfoTextId] = info,
        });
        var resolved = new List<uint>();
        using EffectsUiController controller = EffectsUiController.Bind(
            layout, spellbook, positive: true, () => 0d, NoTex,
            id => { resolved.Add(id); return id + 1000u; }, () => true,
            new EffectRowTemplateFactory(FixtureLoader.LoadEffectRowTemplateInfos(), NoTex, defaultFont: null),
            "SELECT A SPELL")!;

        spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(42u, 1u, 60f, 1u, Bucket: 1u, SpellCategory: 42u));
        spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(43u, 2u, 60f, 1u, Bucket: 1u, SpellCategory: 43u));

        Assert.Equal(2, list.GetNumUIItems());
        Assert.Empty(resolved);
        UiTemplateListSlot row = Assert.IsType<UiTemplateListSlot>(list.GetItem(0));
        UiTextureElement icon = Descendants(row.Content.FindElement(EffectsUiController.RowIconId)!)
            .OfType<UiTextureElement>().Single();
        Assert.Equal(row.EntryId + 1000u, icon.Texture);
        Assert.Equal([row.EntryId], resolved);
    }

    [Fact]
    public void FavoritesRebuild_ComposesNoIconUntilASlotIsRead()
    {
        var spellbook = new Spellbook(SpellTable.LoadFromReader(new StringReader(
            "Spell ID,Name,Flags [Hex]\n1,One,0x4\n2,Two,0x4\n3,Three,0x4\n4,Four,0x4\n5,Five,0x4\n")));
        for (uint id = 1; id <= 5; id++)
        {
            spellbook.OnSpellLearned(id, 1f);
            spellbook.SetFavorite(0, (int)id - 1, id);
        }
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = 1u, Name = "Player" });
        var selection = new SelectionState();
        ImportedLayout layout = LayoutImporter.Build(FixtureLoader.LoadCombatInfos(), _ => (0u, 0, 0), datFont: null);
        var resolved = new List<uint>();
        using SpellcastingUiController controller = SpellcastingUiController.Bind(layout, spellbook,
            new RuntimeSpellCastState(spellbook, selection, new CastOperations()), objects,
            () => 1u, id => { resolved.Add(id); return id + 1000u; }, item => item.ObjectId, _ => { },
            selection, null, null)!;

        controller.Tick();

        UiCatalogSlot[] slots = Descendants(layout.Root).OfType<UiCatalogSlot>()
            .Where(slot => slot.EntryId is >= 1u and <= 5u).ToArray();
        Assert.Equal(5, slots.Length);
        Assert.Empty(resolved);
        Assert.Equal(1003u, slots.Single(slot => slot.EntryId == 3u).CatalogIconTexture);
        Assert.Equal([3u], resolved);
    }

    private static (uint, int, int) NoTex(uint _) => (0u, 0, 0);

    private static IEnumerable<UiElement> Descendants(UiElement parent)
    {
        foreach (UiElement child in parent.Children)
        {
            yield return child;
            foreach (UiElement descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class CastOperations : IRuntimeSpellCastOperations
    {
        public uint LocalPlayerId => 1;
        public bool CanSend => true;
        public bool HasRequiredComponents(uint spellId) => true;
        public bool IsTargetCompatible(uint targetId, SpellMetadata spell, bool showMessage) => true;
        public void StopCompletely() { }
        public void SendUntargeted(uint spellId) { }
        public void SendTargeted(uint targetId, uint spellId) { }
        public void DisplayMessage(string message) { }
        public void IncrementBusy() { }
    }
}
