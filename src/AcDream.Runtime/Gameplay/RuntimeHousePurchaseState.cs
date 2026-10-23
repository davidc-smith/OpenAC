using AcDream.Core.Items;
using AcDream.Core.Net.Messages;

namespace AcDream.Runtime.Gameplay;

/// <summary>The two pages of the slumlord window.</summary>
public enum HousePurchaseTab
{
    Buy = 0,
    Rent = 1,
}

/// <summary>What happened to an item offered to the slumlord window.</summary>
public enum HouseStageResult
{
    Staged,
    NotOpen,
    AlreadyOwned,
    NotOwned,
    MaintenanceFree,
    NotInInventory,
    AlreadyStaged,
    NotRequired,
    AlreadyCovered,
    NoteTooLarge,
}

public readonly record struct RuntimeHousePurchaseSnapshot(
    long Revision,
    bool IsOpen,
    GameEvents.HouseProfile? Profile);

public interface IRuntimeHousePurchaseView
{
    RuntimeHousePurchaseSnapshot Snapshot { get; }

    /// <summary>Staged item guids for one tab, in stage order, leaving out
    /// any that are no longer in the player's inventory.</summary>
    IReadOnlyList<uint> GetStaged(HousePurchaseTab tab);

    /// <summary>How much of payment line <paramref name="lineIndex"/> the
    /// tab's staged items cover: stack sizes, or a trade note's value.</summary>
    int GetStagedAmount(HousePurchaseTab tab, int lineIndex);
}

/// <summary>
/// The slumlord window's state: the last 0x021D HouseProfile and the items
/// the player has dragged onto each tab. Nothing is sent from here; the
/// window sends the staged guids with BuyHouse or RentHouse, and the server
/// takes only what is still owed.
/// </summary>
public sealed class RuntimeHousePurchaseState
{
    /// <summary>The pyreal class. Trade notes pay towards it too.</summary>
    public const uint PyrealWcid = 273u;

    private readonly object _gate = new();
    private readonly ClientObjectTable _objects;
    private readonly Func<uint> _playerGuid;
    private readonly List<uint> _buy = [];
    private readonly List<uint> _rent = [];
    private GameEvents.HouseProfile? _profile;
    private long _revision;

    public RuntimeHousePurchaseState(ClientObjectTable objects, Func<uint> playerGuid)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _playerGuid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        View = new PurchaseView(this);
    }

    public IRuntimeHousePurchaseView View { get; }

    public void ApplyProfile(GameEvents.HouseProfile profile)
    {
        lock (_gate)
        {
            bool sameHouse = _profile is { } current
                && current.SlumlordGuid == profile.SlumlordGuid;
            _profile = profile with
            {
                Buy = (profile.Buy ?? []).ToArray(),
                Rent = (profile.Rent ?? []).ToArray(),
            };
            if (sameHouse)
            {
                RevalidateLocked();
            }
            else
            {
                _buy.Clear();
                _rent.Clear();
            }
            _revision++;
        }
    }

    /// <summary>Stages one item, or for a pack, every item in it that the
    /// tab accepts.</summary>
    public HouseStageResult Stage(HousePurchaseTab tab, uint itemGuid)
    {
        lock (_gate)
        {
            if (_profile is not { } profile)
                return HouseStageResult.NotOpen;
            if (tab == HousePurchaseTab.Buy && profile.OwnerGuid != 0u)
                return HouseStageResult.AlreadyOwned;
            // Nobody pays maintenance on a house nobody owns.
            if (tab == HousePurchaseTab.Rent && profile.OwnerGuid == 0u)
                return HouseStageResult.NotOwned;
            if (tab == HousePurchaseTab.Rent && profile.MaintenanceFree)
                return HouseStageResult.MaintenanceFree;
            if (!IsInInventory(itemGuid) || _objects.Get(itemGuid) is not { } item)
                return HouseStageResult.NotInInventory;

            if ((item.Type & ItemType.Container) != 0)
            {
                bool any = false;
                foreach (uint content in _objects.GetContents(itemGuid))
                    any |= StageItemLocked(profile, tab, content) == HouseStageResult.Staged;
                return any ? HouseStageResult.Staged : HouseStageResult.NotRequired;
            }

            return StageItemLocked(profile, tab, itemGuid);
        }
    }

    private HouseStageResult StageItemLocked(
        GameEvents.HouseProfile profile, HousePurchaseTab tab, uint itemGuid)
    {
        if (!IsInInventory(itemGuid) || _objects.Get(itemGuid) is not { } item)
            return HouseStageResult.NotInInventory;

        List<uint> staged = Staged(tab);
        if (staged.Contains(itemGuid))
            return HouseStageResult.AlreadyStaged;

        IReadOnlyList<GameEvents.HousePayment> lines = Lines(profile, tab);
        int line = LineIndex(lines, item);
        if (line < 0)
            return HouseStageResult.NotRequired;
        int owed = lines[line].Num - lines[line].Paid - StagedAmountLocked(tab, line);
        if (owed <= 0)
            return HouseStageResult.AlreadyCovered;
        // A stack may run over (the server takes only what it needs), but
        // a note can't be split, so it must fit in what is still owed.
        if (IsTradeNote(item) && item.Value > owed)
            return HouseStageResult.NoteTooLarge;

        staged.Add(itemGuid);
        _revision++;
        return HouseStageResult.Staged;
    }

    public void Unstage(HousePurchaseTab tab, uint itemGuid)
    {
        lock (_gate)
        {
            if (Staged(tab).Remove(itemGuid))
                _revision++;
        }
    }

    /// <summary>Empties one tab's strip, as sending its payment does.</summary>
    public void ClearStaged(HousePurchaseTab tab)
    {
        lock (_gate)
        {
            List<uint> staged = Staged(tab);
            if (staged.Count == 0)
                return;
            staged.Clear();
            _revision++;
        }
    }

    /// <summary>Drops staged items that are no longer in the player's
    /// inventory. True when anything was dropped.</summary>
    public bool Revalidate()
    {
        lock (_gate)
        {
            if (!RevalidateLocked())
                return false;
            _revision++;
            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_profile is null && _buy.Count == 0 && _rent.Count == 0)
                return;
            _profile = null;
            _buy.Clear();
            _rent.Clear();
            _revision++;
        }
    }

    private bool RevalidateLocked()
    {
        int removed = _buy.RemoveAll(guid => !IsInInventory(guid))
            + _rent.RemoveAll(guid => !IsInInventory(guid));
        return removed > 0;
    }

    private bool IsInInventory(uint itemGuid)
    {
        uint player = _playerGuid();
        return itemGuid != player
            && _objects.Get(itemGuid) is { WielderId: 0u }
            && _objects.IsOwnedByObject(itemGuid, player);
    }

    private List<uint> Staged(HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? _buy : _rent;

    private static IReadOnlyList<GameEvents.HousePayment> Lines(
        GameEvents.HouseProfile profile, HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;

    private static bool IsTradeNote(ClientObject item) =>
        (item.Type & ItemType.PromissoryNote) != 0;

    private static int Amount(ClientObject item) =>
        IsTradeNote(item) ? item.Value : item.StackSize;

    private static int LineIndex(
        IReadOnlyList<GameEvents.HousePayment> lines, ClientObject item)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].WeenieID == item.WeenieClassId
                || (IsTradeNote(item) && lines[i].WeenieID == PyrealWcid))
            {
                return i;
            }
        }
        return -1;
    }

    private int StagedAmountLocked(HousePurchaseTab tab, int lineIndex)
    {
        if (_profile is not { } profile)
            return 0;
        IReadOnlyList<GameEvents.HousePayment> lines = Lines(profile, tab);
        int total = 0;
        foreach (uint guid in Staged(tab))
        {
            if (IsInInventory(guid)
                && _objects.Get(guid) is { } item
                && LineIndex(lines, item) == lineIndex)
            {
                total += Amount(item);
            }
        }
        return total;
    }

    private sealed class PurchaseView(RuntimeHousePurchaseState owner)
        : IRuntimeHousePurchaseView
    {
        public RuntimeHousePurchaseSnapshot Snapshot
        {
            get
            {
                lock (owner._gate)
                {
                    return new RuntimeHousePurchaseSnapshot(
                        owner._revision, owner._profile is not null, owner._profile);
                }
            }
        }

        public IReadOnlyList<uint> GetStaged(HousePurchaseTab tab)
        {
            lock (owner._gate)
                return owner.Staged(tab).Where(owner.IsInInventory).ToArray();
        }

        public int GetStagedAmount(HousePurchaseTab tab, int lineIndex)
        {
            lock (owner._gate)
                return owner.StagedAmountLocked(tab, lineIndex);
        }
    }
}
