using System.Collections.Concurrent;
using Game.Net;
using Game.Rules;
using Game.Server.Db;

namespace Game.Server;

/// <summary>
/// The bags of everyone playing (T045, T078). Each action is worked out by <see cref="InventoryRules"/>,
/// stored in one database transaction, and only then shown: to the player as the whole bag, and to
/// the channel as clothes or an item on the ground. Database work never runs on a tick thread.
/// </summary>
public sealed class InventoryService
{
    private readonly GameConfig config;
    private readonly IItemStore store;
    private readonly ILogger? logger;

    // Players without a saved character (server tests, no login) keep a bag only while they play.
    private readonly InMemoryItemStore guests = new();
    private readonly ConcurrentDictionary<long, Bag> bags = new();

    public InventoryService(GameConfig config, IItemStore store, ILogger? logger = null)
    {
        this.config = config;
        this.store = store;
        this.logger = logger;
    }

    /// <summary>
    /// Loads a saved character's bag before it joins a channel, giving the starter items the first
    /// time, and returns where it last stood. A saved spot on a map that no longer exists is
    /// forgotten, so the character starts over on the start map; a spot saved before there were
    /// maps is on the test map.
    /// </summary>
    public async Task<MapSpot?> LoadAsync(long characterId, CancellationToken cancel = default)
    {
        Bag bag = await LoadBagAsync(store, characterId, cancel);

        if (bag.PositionX == null)
        {
            return null;
        }

        int map = bag.MapId == null ? 0 : config.MapIndex(bag.MapId);
        return map < 0 ? null : new MapSpot(map, bag.PositionX.Value);
    }

    /// <summary>Sends the bag to a player who just joined or came back, and dresses them in the channel.</summary>
    public async Task AttachAsync(Session session, IConnection connection, CancellationToken cancel = default)
    {
        long key = Key(session);
        Bag bag = bags.TryGetValue(key, out Bag? loaded) ? loaded : await LoadBagAsync(session.Identity == null ? guests : store, key, cancel);
        await bag.Gate.WaitAsync(cancel);

        try
        {
            session.Runner.SetAppearance(session.PlayerId, InventoryRules.AppearanceOf(config, bag.Items));
            Send(connection, bag);
        }
        finally
        {
            bag.Gate.Release();
        }
    }

    /// <summary>One action from the bag screen. Actions of one player run one after another.</summary>
    public async Task HandleAsync(Session session, IConnection connection, ItemActionMessage action, CancellationToken cancel = default)
    {
        if (!bags.TryGetValue(Key(session), out Bag? bag))
        {
            return;
        }

        await bag.Gate.WaitAsync(cancel);

        try
        {
            InventoryProblem problem = action.Kind == ItemActionKind.PickUp
                ? await PickUpAsync(session, bag, action.Target, cancel)
                : await ChangeAsync(session, bag, action, cancel);

            session.Runner.SetAppearance(session.PlayerId, InventoryRules.AppearanceOf(config, bag.Items));
            Send(connection, bag, problem);
        }
        finally
        {
            bag.Gate.Release();
        }
    }

    /// <summary>A player's place was given up: keeps where they stood and lets go of the cached bag.</summary>
    public async Task LeftAsync(Session session, MapSpot spot)
    {
        bags.TryRemove(Key(session), out _);

        if (session.Identity == null)
        {
            return;
        }

        try
        {
            await store.SavePositionAsync(session.Identity.CharacterId, config.Maps[spot.Map].Id, spot.X);
        }
        catch (Exception error)
        {
            logger?.LogError(error, "Could not save where character {Character} stood", session.Identity.CharacterId);
        }
    }

    /// <summary>The items a player has, as last stored; for tests and status.</summary>
    public IReadOnlyList<OwnedItem>? ItemsOf(Session session) => bags.TryGetValue(Key(session), out Bag? bag) ? bag.Items : null;

    private async Task<InventoryProblem> ChangeAsync(Session session, Bag bag, ItemActionMessage action, CancellationToken cancel)
    {
        InventoryPlan plan = action.Kind switch
        {
            ItemActionKind.Wear => InventoryRules.Wear(config, bag.Items, action.Target),
            ItemActionKind.TakeOff => action.Target is >= 0 and <= (long)GearSlot.Cape
                ? InventoryRules.TakeOff(config.Bag, bag.Items, (GearSlot)action.Target)
                : InventoryPlan.Refuse(InventoryProblem.BadSlot),
            ItemActionKind.Move => action.Target is >= 0 and <= byte.MaxValue
                ? InventoryRules.Move(config.Bag, bag.Items, (int)action.Target, action.To)
                : InventoryPlan.Refuse(InventoryProblem.BadSlot),
            ItemActionKind.Drop => InventoryRules.Drop(bag.Items, action.Target),
            _ => InventoryPlan.Refuse(InventoryProblem.NotFound),
        };

        if (!plan.Accepted)
        {
            return plan.Problem;
        }

        var before = bag.Items.ToDictionary(item => item.Id);
        var moves = plan.Moved.Select(item => (Stored(before[item.Id]), Stored(item))).ToList();
        var removed = plan.Removed.Select(id => Stored(before[id])).ToList();

        try
        {
            await StoreOf(session).ApplyAsync(bag.CharacterId, ActionName(action.Kind), moves, removed, cancel);
        }
        catch (StaleItemsException)
        {
            // Someone else changed the items (another server, an old retry): show what is really there.
            await ReloadAsync(session, bag, cancel);
            return InventoryProblem.Busy;
        }

        bag.Items = InventoryRules.Apply(bag.Items, plan);

        foreach (long id in plan.Removed)
        {
            session.Runner.Drop(session.PlayerId, before[id].Item, before[id].Quantity);
        }

        return InventoryProblem.None;
    }

    /// <summary>
    /// The item is reserved on the ground first, so two players cannot both get it; it goes into the
    /// bag in the database, and only then leaves the ground. Any failure puts it back for anyone.
    /// </summary>
    private async Task<InventoryProblem> PickUpAsync(Session session, Bag bag, long groundId, CancellationToken cancel)
    {
        if (groundId is <= 0 or > int.MaxValue)
        {
            return InventoryProblem.Gone;
        }

        (InventoryProblem problem, int item, int quantity) = await session.Runner.ClaimAsync(session.PlayerId, (int)groundId);

        if (problem != InventoryProblem.None)
        {
            return problem;
        }

        int? slot = InventoryRules.FreeBagSlot(config.Bag, bag.Items);

        if (slot == null)
        {
            session.Runner.Release((int)groundId);
            return InventoryProblem.BagFull;
        }

        try
        {
            StoredItem added = await StoreOf(session).AddAsync(bag.CharacterId, "pick_up", new StoredItem(0, config.Gear[item].Id, quantity, ItemPlace.Bag, slot.Value), cancel);
            bag.Items = bag.Items.Append(new OwnedItem(added.Id, item, quantity, ItemPlace.Bag, slot.Value)).ToList();
            session.Runner.Take((int)groundId);
            return InventoryProblem.None;
        }
        catch (Exception error)
        {
            session.Runner.Release((int)groundId);

            if (error is not StaleItemsException)
            {
                logger?.LogError(error, "Could not store a picked-up item for character {Character}", bag.CharacterId);
            }

            await ReloadAsync(session, bag, cancel);
            return InventoryProblem.Busy;
        }
    }

    private async Task<Bag> LoadBagAsync(IItemStore from, long characterId, CancellationToken cancel)
    {
        StoredBag stored = await from.LoadAsync(characterId, Starter(), cancel);
        var bag = new Bag(characterId) { Items = Owned(stored.Items, characterId), PositionX = stored.PositionX, MapId = stored.MapId };
        return bags.GetOrAdd(characterId, bag);
    }

    private async Task ReloadAsync(Session session, Bag bag, CancellationToken cancel)
    {
        StoredBag stored = await StoreOf(session).LoadAsync(bag.CharacterId, Starter(), cancel);
        bag.Items = Owned(stored.Items, bag.CharacterId);
    }

    /// <summary>Stored items as the rules see them; an item no longer in the catalog stays stored but is not shown.</summary>
    private List<OwnedItem> Owned(IEnumerable<StoredItem> stored, long characterId)
    {
        var owned = new List<OwnedItem>();

        foreach (StoredItem item in stored)
        {
            int index = IndexOf(item.ItemId);

            if (index < 0)
            {
                logger?.LogWarning("Character {Character} owns {Item}, which is not in the catalog", characterId, item.ItemId);
                continue;
            }

            owned.Add(new OwnedItem(item.Id, index, item.Quantity, item.Place, item.Slot));
        }

        return owned;
    }

    private IReadOnlyList<StoredItem> Starter() =>
        InventoryRules.Starter(config).Select(item => new StoredItem(0, config.Gear[item.Item].Id, item.Quantity, item.Place, item.Slot)).ToList();

    private StoredItem Stored(OwnedItem item) => new StoredItem(item.Id, config.Gear[item.Item].Id, item.Quantity, item.Place, item.Slot);

    private int IndexOf(string itemId)
    {
        for (int index = 0; index < config.Gear.Count; index++)
        {
            if (config.Gear[index].Id == itemId)
            {
                return index;
            }
        }

        return -1;
    }

    private void Send(IConnection connection, Bag bag, InventoryProblem problem = InventoryProblem.None)
    {
        connection.Send(Packet.Encode(new InventoryMessage(config.Bag.Slots, bag.Items, problem)));
    }

    private IItemStore StoreOf(Session session) => session.Identity == null ? guests : store;

    /// <summary>A saved character by its id; a player without one by a negative number nobody else has.</summary>
    private static long Key(Session session) => session.Identity?.CharacterId ?? -session.PlayerId;

    private static string ActionName(ItemActionKind kind) => kind switch
    {
        ItemActionKind.Wear => "wear",
        ItemActionKind.TakeOff => "take_off",
        ItemActionKind.Move => "move",
        ItemActionKind.Drop => "drop",
        _ => "pick_up",
    };

    private sealed class Bag
    {
        public Bag(long characterId)
        {
            CharacterId = characterId;
        }

        public long CharacterId { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<OwnedItem> Items { get; set; } = new();
        public double? PositionX { get; set; }
        public string? MapId { get; set; }
    }
}
