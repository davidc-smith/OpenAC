using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthObjectMirrorTests
{
    private static (AcDream.Runtime.GameRuntime, RuntimeRynthGameView, RynthObjectMirror) Make()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Name = "Sammy" });
        var pack = new ClientObject { ObjectId = 0x80000110, ContainerId = 0x50000001, ItemsCapacity = 24, Name = "Pack" };
        var gem = new ClientObject { ObjectId = 0x80000111, ContainerId = 0x80000110, Name = "Gem" };
        gem.Properties.Int64s[1] = 42;
        gem.Properties.Ints[5] = 7;
        runtime.InventoryOwner.Objects.AddOrUpdate(pack);
        runtime.InventoryOwner.Objects.AddOrUpdate(gem);
        runtime.InventoryOwner.Objects.ReplaceContents(0x50000001, new uint[] { 0x80000110 });
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000111 });
        var live = new RuntimeRynthGameView(runtime);
        return (runtime, live, new RynthObjectMirror(live, runtime));
    }

    [Fact]
    public void ObjectsAreDeepCopies()
    {
        var (runtime, live, mirror) = Make();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.True(part.TryGet(0x80000111, out RynthObjectEntry gem));
        Assert.Equal("Gem", gem.Name);
        Assert.Equal(7, gem.Info.Ints[5]);
        runtime.InventoryOwner.Objects.Get(0x80000111)!.Properties.Ints[5] = 99;
        Assert.Equal(7, gem.Info.Ints[5]);
        Assert.True(gem.Int64s.TryGetValue(1, out long v) && v == 42);
        runtime.InventoryOwner.Objects.Get(0x80000111)!.Properties.Int64s[1] = 43;
        Assert.Equal(42, gem.Int64s[1]);
    }

    [Fact]
    public void PublishedCollectionsAreNotTheLiveOnes()
    {
        var (runtime, live, mirror) = Make();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.NotSame(live.LiveObjectIds, part.LiveObjectIds);
        part.TryGet(0x80000111, out RynthObjectEntry gem);
        ClientObject liveGem = runtime.InventoryOwner.Objects.Get(0x80000111)!;
        Assert.NotSame(liveGem.Properties.Ints, gem.Info.Ints);
        Assert.NotSame(liveGem.Properties.Strings, gem.Info.Strings);
        Assert.NotSame(liveGem.AppraisedSpellIds, gem.Info.AppraisedSpellIds);
    }

    [Fact]
    public void OnlyChangedObjectsAreCopiedAgain()
    {
        var (runtime, live, mirror) = Make();
        RynthObjectsPart a = mirror.Build(live.PlayerId);
        RynthObjectsPart b = mirror.Build(live.PlayerId);
        Assert.Same(a, b);
        a.TryGet(0x80000110, out RynthObjectEntry packA);
        b.TryGet(0x80000110, out RynthObjectEntry packB);
        Assert.Same(packA, packB);
        b.TryGet(0x80000111, out RynthObjectEntry gemB);

        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000110, ContainerId = 0x50000001, ItemsCapacity = 24, Name = "Big Pack" });
        RynthObjectsPart c = mirror.Build(live.PlayerId);
        c.TryGet(0x80000110, out RynthObjectEntry packC);
        Assert.NotSame(packB, packC);
        Assert.Equal("Big Pack", packC.Name);
        c.TryGet(0x80000111, out RynthObjectEntry gemC);
        Assert.Same(gemB, gemC);

        runtime.InventoryOwner.Objects.Remove(0x80000111);
        Assert.False(mirror.Build(live.PlayerId).TryGet(0x80000111, out _));
    }

    [Fact]
    public void ClearedEmptiesIt()
    {
        var (runtime, live, mirror) = Make();
        Assert.True(mirror.Build(live.PlayerId).TryGet(0x80000110, out _));
        runtime.InventoryOwner.Objects.Clear();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.Empty(part.Entries);
        Assert.Empty(part.Contents(0x80000110));
    }

    [Fact]
    public void IndexesAndPredicatesMatchTheLiveView()
    {
        var (_, live, mirror) = Make();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.Equal(live.GetContainerContents(0x80000110), part.Contents(0x80000110));
        Assert.Equal(live.LiveObjectIds!.OrderBy(i => i), part.LiveObjectIds!.OrderBy(i => i));
        Assert.Equal(live.InventoryRevision, part.Revision);
        Assert.Equal(live.TryCountContents(0x50000001, out int i1, out int c1), part.TryCountContents(0x50000001, out int i2, out int c2));
        Assert.Equal((i1, c1), (i2, c2));
        foreach (uint id in new uint[] { 0x50000001, 0x80000110, 0x80000111 })
        {
            part.TryGet(id, out RynthObjectEntry e);
            Assert.Equal(live.IsOwnedItem(id), e.IsOwnedItem);
            Assert.Equal(live.IsAttackable(id), e.IsAttackable);
            Assert.Equal(live.IsOwnedTinkeringTool(id), e.IsOwnedTinkeringTool);
            Assert.Equal(live.GetObjectEnchantments(id), e.Enchantments);
        }
    }

    [Fact]
    public void PredicatesFollowAMoveOfTheContainer()
    {
        var (runtime, live, mirror) = Make();
        Assert.True(mirror.Build(live.PlayerId).TryGet(0x80000111, out RynthObjectEntry before) && before.IsOwnedItem);
        // The pack goes to the ground: the gem itself was not reported, but it is no longer owned.
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000110, ItemsCapacity = 24, Name = "Pack" });
        runtime.InventoryOwner.Objects.ReplaceContents(0x50000001, Array.Empty<uint>());
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        part.TryGet(0x80000111, out RynthObjectEntry after);
        Assert.False(after.IsOwnedItem);
        Assert.Equal(live.IsOwnedItem(0x80000111), after.IsOwnedItem);
        Assert.Equal(live.GetContainerContents(0x50000001), part.Contents(0x50000001));
    }

    [Fact]
    public void AContentsListingOfKnownObjectsReachesContentsAndCounts()
    {
        var (runtime, live, mirror) = Make();
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000112, ContainerId = 0x80000110, Name = "Ruby" });
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000111, 0x80000112 });
        mirror.Build(live.PlayerId);
        // Every id is known already: the table reports the listing with ContainerContentsReplaced alone.
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000112 });
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.Equal(live.GetContainerContents(0x80000110), part.Contents(0x80000110));
        Assert.Equal(live.TryCountContents(0x80000110, out int i1, out int c1), part.TryCountContents(0x80000110, out int i2, out int c2));
        Assert.Equal((i1, c1), (i2, c2));

        runtime.InventoryOwner.Objects.StopViewingContents(0x80000110);
        part = mirror.Build(live.PlayerId);
        Assert.Equal(live.GetContainerContents(0x80000110), part.Contents(0x80000110));
        Assert.Equal(live.TryCountContents(0x80000110, out i1, out c1), part.TryCountContents(0x80000110, out i2, out c2));
        Assert.Equal((i1, c1), (i2, c2));
    }

    [Fact]
    public void AMoveRenumbersItsSiblings()
    {
        var (runtime, live, mirror) = Make();
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000112, ContainerId = 0x80000110, Name = "Ruby" });
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000111, 0x80000112 });
        runtime.InventoryOwner.Objects.MoveItemOptimistic(0x80000111, 0x80000110, newSlot: 0);
        mirror.Build(live.PlayerId);
        // The ruby goes to the front; only it is reported, but the gem's slot changes too.
        runtime.InventoryOwner.Objects.MoveItemOptimistic(0x80000112, 0x80000110, newSlot: 0);
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.True(live.TryGetObject(0x80000111, out RynthObjectInfo gem));
        part.TryGet(0x80000111, out RynthObjectEntry entry);
        Assert.Equal(1, gem.ContainerSlot);
        Assert.Equal(gem.ContainerSlot, entry.Info.ContainerSlot);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        var (runtime, live, mirror) = Make();
        mirror.Build(live.PlayerId).TryGet(0x80000111, out RynthObjectEntry before);
        mirror.Dispose();
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000111, ContainerId = 0x80000110, Name = "Big Gem" });
        // The table's update no longer reaches the mirror, so the gem is not copied again.
        mirror.Build(live.PlayerId).TryGet(0x80000111, out RynthObjectEntry after);
        Assert.Same(before, after);
    }
}
