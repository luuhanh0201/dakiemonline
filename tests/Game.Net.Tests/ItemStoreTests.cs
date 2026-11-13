using Game.Rules;
using Game.Server.Db;
using Npgsql;

namespace Game.Net.Tests;

/// <summary>The same expectations for both item stores, so memory and PostgreSQL cannot drift apart.</summary>
internal static class ItemContract
{
    public static readonly IReadOnlyList<StoredItem> Starter = new[]
    {
        new StoredItem(0, "gear.kiem", 1, ItemPlace.Worn, 0),
        new StoredItem(0, "gear.nodachi", 1, ItemPlace.Bag, 0),
        new StoredItem(0, "gear.giap", 1, ItemPlace.Bag, 1),
    };

    private static StoredItem In(StoredBag bag, string id) => bag.Items.Single(item => item.ItemId == id);

    public static async Task StarterItemsAreGivenOnce(IItemStore store, long character)
    {
        StoredBag first = await store.LoadAsync(character, Starter);
        await store.ApplyAsync(character, "drop", Array.Empty<(StoredItem, StoredItem)>(), new[] { In(first, "gear.nodachi") });
        StoredBag again = await store.LoadAsync(character, Starter);

        Assert.Equal(3, first.Items.Count);
        Assert.All(first.Items, item => Assert.True(item.Id > 0));
        Assert.Equal(2, again.Items.Count);
        Assert.DoesNotContain(again.Items, item => item.ItemId == "gear.nodachi");
    }

    public static async Task TwoItemsSwapPlaces(IItemStore store, long character)
    {
        StoredBag bag = await store.LoadAsync(character, Starter);
        StoredItem kiem = In(bag, "gear.kiem");
        StoredItem nodachi = In(bag, "gear.nodachi");

        await store.ApplyAsync(character, "wear", new[]
        {
            (nodachi, nodachi with { Place = ItemPlace.Worn, Slot = 0 }),
            (kiem, kiem with { Place = ItemPlace.Bag, Slot = nodachi.Slot }),
        }, Array.Empty<StoredItem>());

        StoredBag after = await store.LoadAsync(character, Starter);
        Assert.Equal((ItemPlace.Worn, 0), (In(after, "gear.nodachi").Place, In(after, "gear.nodachi").Slot));
        Assert.Equal((ItemPlace.Bag, 0), (In(after, "gear.kiem").Place, In(after, "gear.kiem").Slot));
    }

    public static async Task AnOldViewChangesNothing(IItemStore store, long character)
    {
        StoredBag bag = await store.LoadAsync(character, Starter);
        StoredItem giap = In(bag, "gear.giap");
        await store.ApplyAsync(character, "move", new[] { (giap, giap with { Slot = 5 }) }, Array.Empty<StoredItem>());

        // The same move again, from the view before it: the item is no longer in slot 1.
        await Assert.ThrowsAsync<StaleItemsException>(() =>
            store.ApplyAsync(character, "move", new[] { (giap, giap with { Slot = 7 }) }, Array.Empty<StoredItem>()));
        await Assert.ThrowsAsync<StaleItemsException>(() =>
            store.ApplyAsync(character, "drop", Array.Empty<(StoredItem, StoredItem)>(), new[] { giap }));

        Assert.Equal(5, In(await store.LoadAsync(character, Starter), "gear.giap").Slot);
    }

    public static async Task TwoItemsCannotShareASlot(IItemStore store, long character)
    {
        StoredBag bag = await store.LoadAsync(character, Starter);
        StoredItem giap = In(bag, "gear.giap");

        await Assert.ThrowsAsync<StaleItemsException>(() =>
            store.ApplyAsync(character, "move", new[] { (giap, giap with { Slot = 0 }) }, Array.Empty<StoredItem>()));
        await Assert.ThrowsAsync<StaleItemsException>(() =>
            store.AddAsync(character, "pick_up", new StoredItem(0, "gear.mu", 1, ItemPlace.Bag, 0)));

        Assert.Equal(3, (await store.LoadAsync(character, Starter)).Items.Count);
    }

    public static async Task PickedUpItemsGetANewId(IItemStore store, long character)
    {
        StoredBag bag = await store.LoadAsync(character, Starter);
        StoredItem added = await store.AddAsync(character, "pick_up", new StoredItem(0, "gear.mu", 1, ItemPlace.Bag, 9));

        Assert.True(added.Id > bag.Items.Max(item => item.Id));
        Assert.Contains(added, (await store.LoadAsync(character, Starter)).Items);
    }

    public static async Task PositionIsKept(IItemStore store, long character)
    {
        Assert.Null((await store.LoadAsync(character, Starter)).PositionX);

        await store.SavePositionAsync(character, "map.house", 12.5d);
        StoredBag back = await store.LoadAsync(character, Starter);

        Assert.Equal(12.5d, back.PositionX);
        Assert.Equal("map.house", back.MapId);
    }
}

public class InMemoryItemStoreTests
{
    private readonly InMemoryItemStore store = new();

    [Fact] public Task StarterItemsAreGivenOnce() => ItemContract.StarterItemsAreGivenOnce(store, 1);
    [Fact] public Task TwoItemsSwapPlaces() => ItemContract.TwoItemsSwapPlaces(store, 1);
    [Fact] public Task AnOldViewChangesNothing() => ItemContract.AnOldViewChangesNothing(store, 1);
    [Fact] public Task TwoItemsCannotShareASlot() => ItemContract.TwoItemsCannotShareASlot(store, 1);
    [Fact] public Task PickedUpItemsGetANewId() => ItemContract.PickedUpItemsGetANewId(store, 1);
    [Fact] public Task PositionIsKept() => ItemContract.PositionIsKept(store, 1);

    [Fact]
    public async Task EveryChangeIsLogged()
    {
        StoredBag bag = await store.LoadAsync(4, ItemContract.Starter);
        StoredItem giap = bag.Items.Single(item => item.ItemId == "gear.giap");
        await store.ApplyAsync(4, "drop", Array.Empty<(StoredItem, StoredItem)>(), new[] { giap });

        Assert.Equal(new[] { "starter", "starter", "starter", "drop" }, store.Log.Select(line => line.Action));
        Assert.Equal(giap, store.Log[^1].From);
        Assert.Null(store.Log[^1].To);
    }
}

/// <summary>The real database, in a schema made for this run and dropped afterwards.</summary>
public sealed class PostgresItemStoreTests : IAsyncLifetime
{
    private readonly string schema = "t045_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource? source;
    private IItemStore store = null!;
    private long character;

    public async Task InitializeAsync()
    {
        if (new DatabaseFactAttribute().Skip != null)
        {
            return;
        }

        source = Database.CreateSource(DatabaseFactAttribute.ConnectionString);
        (ICharacterStore characters, _) = await Database.OpenAsync(source, schema);
        character = (await characters.CreateAsync("Túi đồ")).Id;
        store = Database.Items(source, schema);
    }

    public async Task DisposeAsync()
    {
        if (source == null)
        {
            return;
        }

        await using (NpgsqlCommand drop = source.CreateCommand($"drop schema if exists {Migrator.Quote(schema)} cascade"))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await source.DisposeAsync();
    }

    [DatabaseFact] public Task StarterItemsAreGivenOnce() => ItemContract.StarterItemsAreGivenOnce(store, character);
    [DatabaseFact] public Task TwoItemsSwapPlaces() => ItemContract.TwoItemsSwapPlaces(store, character);
    [DatabaseFact] public Task AnOldViewChangesNothing() => ItemContract.AnOldViewChangesNothing(store, character);
    [DatabaseFact] public Task TwoItemsCannotShareASlot() => ItemContract.TwoItemsCannotShareASlot(store, character);
    [DatabaseFact] public Task PickedUpItemsGetANewId() => ItemContract.PickedUpItemsGetANewId(store, character);
    [DatabaseFact] public Task PositionIsKept() => ItemContract.PositionIsKept(store, character);

    [DatabaseFact]
    public async Task EveryChangeIsLoggedInTheDatabase()
    {
        StoredBag bag = await store.LoadAsync(character, ItemContract.Starter);
        StoredItem giap = bag.Items.Single(item => item.ItemId == "gear.giap");
        await store.ApplyAsync(character, "drop", Array.Empty<(StoredItem, StoredItem)>(), new[] { giap });

        await using NpgsqlCommand lines = source!.CreateCommand($"select action from {Migrator.Quote(schema)}.item_log where character_id = $1 order by id");
        lines.Parameters.AddWithValue(character);
        var actions = new List<string>();
        await using (NpgsqlDataReader reader = await lines.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actions.Add(reader.GetString(0));
            }
        }

        Assert.Equal(new[] { "starter", "starter", "starter", "drop" }, actions);
    }

    [DatabaseFact]
    public async Task ItemsGoWithADeletedCharacterButTheLogStays()
    {
        await store.LoadAsync(character, ItemContract.Starter);
        string quoted = Migrator.Quote(schema);

        await using (NpgsqlCommand delete = source!.CreateCommand($"delete from {quoted}.characters where id = $1"))
        {
            delete.Parameters.AddWithValue(character);
            await delete.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand count = source.CreateCommand($"select (select count(*) from {quoted}.items), (select count(*) from {quoted}.item_log)");
        await using NpgsqlDataReader reader = await count.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(3L, reader.GetInt64(1));
    }
}
