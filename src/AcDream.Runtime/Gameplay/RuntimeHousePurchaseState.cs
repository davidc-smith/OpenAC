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
    /// tab's staged items have paid: never more than the line asks for.</summary>
    int GetStagedAmount(HousePurchaseTab tab, int lineIndex);
}

/// <summary>
/// The slumlord window's state: the last 0x021D HouseProfile and the items
/// the player has dragged onto each tab. Nothing is sent from here; the
/// window sends the staged guids with BuyHouse or RentHouse, and the server
/// takes only what is still owed.
/// </summary>
/// <remarks>
/// Each tab keeps a running paid amount per payment line. It starts at zero
/// with every profile (the server's paid amounts are shown, never counted),
/// grows as items are staged and shrinks as they are taken off. Staging a
/// stack counts no more than the line still owes; a stack of trade notes
/// counts only the whole notes that fit. Taking an item off subtracts its
/// whole stack, never going below zero.
/// </remarks>
public sealed class RuntimeHousePurchaseState
{
    /// <summary>The pyreal class. Trade notes pay towards it too.</summary>
    public const uint PyrealWcid = 273u;

    private static readonly IReadOnlyDictionary<uint, int> NoDenominations =
        new Dictionary<uint, int>();

    private readonly object _gate = new();
    private readonly ClientObjectTable _objects;
    private readonly Func<uint> _playerGuid;
    private readonly List<StagedItem> _buy = [];
    private readonly List<StagedItem> _rent = [];
    private int[] _buyPaid = [];
    private int[] _rentPaid = [];
    private IReadOnlyDictionary<uint, int> _noteDenominations = NoDenominations;
    private GameEvents.HouseProfile? _profile;
    private long _revision;

    /// <summary>A staged item, with the class and stack size it had when
    /// staged, for taking its payment off after the item is gone.</summary>
    private readonly record struct StagedItem(uint Guid, uint Wcid, int Count);

    public RuntimeHousePurchaseState(ClientObjectTable objects, Func<uint> playerGuid)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _playerGuid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        View = new PurchaseView(this);
    }

    public IRuntimeHousePurchaseView View { get; }

    /// <summary>Installs the trade-note classes from the portal data, each
    /// with its value in pyreals. Only these classes pay as trade notes.</summary>
    public void InstallTradeNoteDenominations(IReadOnlyDictionary<uint, int> denominations)
    {
        ArgumentNullException.ThrowIfNull(denominations);
        lock (_gate)
            _noteDenominations = denominations;
    }

    /// <summary>Every profile, the same house's included, empties both
    /// strips: staging starts again from nothing, whatever the server says
    /// is already paid.</summary>
    public void ApplyProfile(GameEvents.HouseProfile profile)
    {
        lock (_gate)
        {
            GameEvents.HousePayment[] buy = (profile.Buy ?? []).ToArray();
            GameEvents.HousePayment[] rent = (profile.Rent ?? []).ToArray();
            _profile = profile with { Buy = buy, Rent = rent };
            _buy.Clear();
            _rent.Clear();
            _buyPaid = new int[buy.Length];
            _rentPaid = new int[rent.Length];
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
            // An item not carried is refused first, whatever the tab.
            if (!IsInInventory(itemGuid) || _objects.Get(itemGuid) is not { } item)
                return HouseStageResult.NotInInventory;
            if (tab == HousePurchaseTab.Buy && profile.OwnerGuid != 0u)
                return HouseStageResult.AlreadyOwned;
            // Nobody pays maintenance on a house nobody owns.
            if (tab == HousePurchaseTab.Rent && profile.OwnerGuid == 0u)
                return HouseStageResult.NotOwned;
            if (tab == HousePurchaseTab.Rent && profile.MaintenanceFree)
                return HouseStageResult.MaintenanceFree;

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

        List<StagedItem> staged = Staged(tab);
        if (staged.Exists(entry => entry.Guid == itemGuid))
            return HouseStageResult.AlreadyStaged;

        IReadOnlyList<GameEvents.HousePayment> lines = Lines(profile, tab);
        int line = LineIndex(lines, item.WeenieClassId);
        if (line < 0)
            return HouseStageResult.NotRequired;
        int[] paid = Paid(tab);
        int owed = lines[line].Num - paid[line];
        if (owed <= 0)
            return HouseStageResult.AlreadyCovered;

        int count = PaymentCount(item);
        if (TradeNoteValue(item.WeenieClassId) is { } note)
        {
            // A note can't be split: as many whole notes as fit, at least one.
            int notes = note > 0 && owed >= note ? Math.Min(owed / note, count) : 0;
            if (notes <= 0)
                return HouseStageResult.NoteTooLarge;
            paid[line] += notes * note;
        }
        else
        {
            // A stack may run over (the server takes only what it needs),
            // but it counts no more than is owed.
            paid[line] += Math.Min(count, owed);
        }

        staged.Add(new StagedItem(itemGuid, item.WeenieClassId, count));
        _revision++;
        return HouseStageResult.Staged;
    }

    public void Unstage(HousePurchaseTab tab, uint itemGuid)
    {
        lock (_gate)
        {
            int index = Staged(tab).FindIndex(entry => entry.Guid == itemGuid);
            if (index < 0)
                return;
            RemoveLocked(tab, index);
            _revision++;
        }
    }

    /// <summary>Empties one tab's strip, as sending its payment does.</summary>
    public void ClearStaged(HousePurchaseTab tab)
    {
        lock (_gate)
        {
            List<StagedItem> staged = Staged(tab);
            if (staged.Count == 0)
                return;
            staged.Clear();
            Array.Clear(Paid(tab));
            _revision++;
        }
    }

    /// <summary>Drops staged items that are no longer in the player's
    /// inventory, taking their payment off. True when anything was dropped.</summary>
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
            _buyPaid = [];
            _rentPaid = [];
            _revision++;
        }
    }

    private bool RevalidateLocked()
    {
        bool removed = false;
        foreach (HousePurchaseTab tab in new[] { HousePurchaseTab.Buy, HousePurchaseTab.Rent })
        {
            List<StagedItem> staged = Staged(tab);
            for (int i = staged.Count - 1; i >= 0; i--)
            {
                if (IsInInventory(staged[i].Guid))
                    continue;
                RemoveLocked(tab, i);
                removed = true;
            }
        }
        return removed;
    }

    // Takes an item off the strip, and its whole stack off its line's paid
    // amount, never below zero.
    private void RemoveLocked(HousePurchaseTab tab, int index)
    {
        List<StagedItem> staged = Staged(tab);
        StagedItem entry = staged[index];
        staged.RemoveAt(index);
        if (_profile is not { } profile)
            return;

        int line = LineIndex(Lines(profile, tab), entry.Wcid);
        if (line < 0)
            return;
        int count = _objects.Get(entry.Guid) is { } item ? PaymentCount(item) : entry.Count;
        int amount = TradeNoteValue(entry.Wcid) is { } note ? count * note : count;
        if (amount <= 0)
            return;
        int[] paid = Paid(tab);
        paid[line] = amount > paid[line] ? 0 : paid[line] - amount;
    }

    private bool IsInInventory(uint itemGuid)
    {
        uint player = _playerGuid();
        return itemGuid != player
            && _objects.Get(itemGuid) is { WielderId: 0u }
            && _objects.IsOwnedByObject(itemGuid, player);
    }

    private List<StagedItem> Staged(HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? _buy : _rent;

    private int[] Paid(HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? _buyPaid : _rentPaid;

    private static IReadOnlyList<GameEvents.HousePayment> Lines(
        GameEvents.HouseProfile profile, HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;

    /// <summary>A trade note's value in pyreals, or null for any other class.</summary>
    private int? TradeNoteValue(uint wcid) =>
        _noteDenominations.TryGetValue(wcid, out int value) ? value : null;

    // How many of an item one staging offers: its stack size, at least one.
    private static int PaymentCount(ClientObject item) =>
        item.StackSize != 0 ? item.StackSize : 1;

    // The first line the class pays: a trade note pays the pyreal line,
    // anything else the line asking for its own class.
    private int LineIndex(IReadOnlyList<GameEvents.HousePayment> lines, uint wcid)
    {
        bool isNote = TradeNoteValue(wcid) is not null;
        for (int i = 0; i < lines.Count; i++)
        {
            if ((isNote && lines[i].WeenieID == PyrealWcid) || lines[i].WeenieID == wcid)
                return i;
        }
        return -1;
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
            {
                return owner.Staged(tab)
                    .Select(entry => entry.Guid)
                    .Where(owner.IsInInventory)
                    .ToArray();
            }
        }

        public int GetStagedAmount(HousePurchaseTab tab, int lineIndex)
        {
            lock (owner._gate)
            {
                int[] paid = owner.Paid(tab);
                return lineIndex >= 0 && lineIndex < paid.Length ? paid[lineIndex] : 0;
            }
        }
    }
}
