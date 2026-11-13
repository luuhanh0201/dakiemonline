using Game.Rules;
using Npgsql;

namespace Game.Server.Db;

/// <summary>One owned item as stored: the catalog id, not its place in the catalog, so config edits do not move items.</summary>
public sealed record StoredItem(long Id, string ItemId, int Quantity, ItemPlace Place, int Slot);

/// <summary>
/// A character's items and where it last stood, as loaded when it comes into a channel. MapId is the
/// map file's id, such as map.house; null with a position means it was saved before there were maps.
/// </summary>
public sealed record StoredBag(IReadOnlyList<StoredItem> Items, double? PositionX, string? MapId = null);

/// <summary>The items were changed by someone else since they were read, or a slot is already taken.</summary>
public sealed class StaleItemsException : Exception
{
    public StaleItemsException(long characterId)
        : base($"The items of character {characterId} changed since they were read; load them again.")
    {
        CharacterId = characterId;
    }

    public long CharacterId { get; }
}

/// <summary>
/// Where items live. Every change is one transaction that first checks each item is still where
/// the caller saw it, and writes a line to the item log; so a retry or a lost connection never
/// copies or loses an item.
/// </summary>
public interface IItemStore
{
    /// <summary>
    /// The character's items and position. The first time, the starter items are given in the same
    /// transaction that marks them given, so they are never given twice.
    /// </summary>
    Task<StoredBag> LoadAsync(long characterId, IReadOnlyList<StoredItem> starter, CancellationToken cancel = default);

    /// <summary>Moves and removes items, each only if still where <c>Before</c> says; otherwise nothing changes.</summary>
    Task ApplyAsync(long characterId, string action, IReadOnlyList<(StoredItem Before, StoredItem After)> moves, IReadOnlyList<StoredItem> removed, CancellationToken cancel = default);

    /// <summary>Puts a new item into a slot and returns it with its id. Throws <see cref="StaleItemsException"/> when the slot is taken.</summary>
    Task<StoredItem> AddAsync(long characterId, string action, StoredItem item, CancellationToken cancel = default);

    /// <summary>Where the character stood when it left: the map's id and how far along.</summary>
    Task SavePositionAsync(long characterId, string mapId, double x, CancellationToken cancel = default);
}

public sealed class PostgresItemStore : IItemStore
{
    private readonly NpgsqlDataSource source;
    private readonly string items;
    private readonly string log;
    private readonly string characters;

    public PostgresItemStore(NpgsqlDataSource source, string schema)
    {
        this.source = source;
        string quoted = Migrator.Quote(schema);
        items = $"{quoted}.items";
        log = $"{quoted}.item_log";
        characters = $"{quoted}.characters";
    }

    public async Task<StoredBag> LoadAsync(long characterId, IReadOnlyList<StoredItem> starter, CancellationToken cancel = default)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync(cancel);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancel);
        bool given;
        double? x;
        string? mapId;

        // Locks the character row, so two servers loading it at once cannot both give the starter items.
        await using (var read = new NpgsqlCommand($"select starter_given, position_x, map_id from {characters} where id = $1 for update", connection, transaction))
        {
            read.Parameters.AddWithValue(characterId);
            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(cancel);

            if (!await reader.ReadAsync(cancel))
            {
                throw new KeyNotFoundException($"No character {characterId}.");
            }

            given = reader.GetBoolean(0);
            x = reader.IsDBNull(1) ? null : reader.GetDouble(1);
            mapId = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        if (!given)
        {
            foreach (StoredItem item in starter)
            {
                await Insert(connection, transaction, characterId, "starter", item, cancel);
            }

            await using var mark = new NpgsqlCommand($"update {characters} set starter_given = true where id = $1", connection, transaction);
            mark.Parameters.AddWithValue(characterId);
            await mark.ExecuteNonQueryAsync(cancel);
        }

        var owned = new List<StoredItem>();

        await using (var list = new NpgsqlCommand($"select id, item_id, quantity, place, slot from {items} where character_id = $1 order by place, slot", connection, transaction))
        {
            list.Parameters.AddWithValue(characterId);
            await using NpgsqlDataReader reader = await list.ExecuteReaderAsync(cancel);

            while (await reader.ReadAsync(cancel))
            {
                owned.Add(new StoredItem(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), (ItemPlace)reader.GetInt16(3), reader.GetInt16(4)));
            }
        }

        await transaction.CommitAsync(cancel);
        return new StoredBag(owned, x, mapId);
    }

    public async Task ApplyAsync(long characterId, string action, IReadOnlyList<(StoredItem Before, StoredItem After)> moves, IReadOnlyList<StoredItem> removed, CancellationToken cancel = default)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync(cancel);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancel);

        foreach ((StoredItem before, StoredItem after) in moves)
        {
            await using var move = new NpgsqlCommand(
                $"update {items} set place = $5, slot = $6 where id = $1 and character_id = $2 and place = $3 and slot = $4",
                connection,
                transaction);
            move.Parameters.AddWithValue(before.Id);
            move.Parameters.AddWithValue(characterId);
            move.Parameters.AddWithValue((short)before.Place);
            move.Parameters.AddWithValue((short)before.Slot);
            move.Parameters.AddWithValue((short)after.Place);
            move.Parameters.AddWithValue((short)after.Slot);

            if (await move.ExecuteNonQueryAsync(cancel) != 1)
            {
                throw new StaleItemsException(characterId);
            }

            await Log(connection, transaction, characterId, action, before, after, cancel);
        }

        foreach (StoredItem item in removed)
        {
            await using var delete = new NpgsqlCommand(
                $"delete from {items} where id = $1 and character_id = $2 and place = $3 and slot = $4 and quantity = $5",
                connection,
                transaction);
            delete.Parameters.AddWithValue(item.Id);
            delete.Parameters.AddWithValue(characterId);
            delete.Parameters.AddWithValue((short)item.Place);
            delete.Parameters.AddWithValue((short)item.Slot);
            delete.Parameters.AddWithValue(item.Quantity);

            if (await delete.ExecuteNonQueryAsync(cancel) != 1)
            {
                throw new StaleItemsException(characterId);
            }

            await Log(connection, transaction, characterId, action, item, null, cancel);
        }

        await Commit(transaction, characterId, cancel);
    }

    public async Task<StoredItem> AddAsync(long characterId, string action, StoredItem item, CancellationToken cancel = default)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync(cancel);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancel);
        StoredItem added = await Insert(connection, transaction, characterId, action, item, cancel);
        await Commit(transaction, characterId, cancel);
        return added;
    }

    public async Task SavePositionAsync(long characterId, string mapId, double x, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"update {characters} set position_x = $2, map_id = $3 where id = $1");
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(x);
        command.Parameters.AddWithValue(mapId);
        await command.ExecuteNonQueryAsync(cancel);
    }

    /// <summary>The slot check is deferred to the commit, so a taken slot shows up here.</summary>
    private static async Task Commit(NpgsqlTransaction transaction, long characterId, CancellationToken cancel)
    {
        try
        {
            await transaction.CommitAsync(cancel);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new StaleItemsException(characterId);
        }
    }

    private async Task<StoredItem> Insert(NpgsqlConnection connection, NpgsqlTransaction transaction, long characterId, string action, StoredItem item, CancellationToken cancel)
    {
        await using var insert = new NpgsqlCommand(
            $"insert into {items} (character_id, item_id, quantity, place, slot) values ($1, $2, $3, $4, $5) returning id",
            connection,
            transaction);
        insert.Parameters.AddWithValue(characterId);
        insert.Parameters.AddWithValue(item.ItemId);
        insert.Parameters.AddWithValue(item.Quantity);
        insert.Parameters.AddWithValue((short)item.Place);
        insert.Parameters.AddWithValue((short)item.Slot);
        StoredItem added = item with { Id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancel)) };
        await Log(connection, transaction, characterId, action, null, added, cancel);
        return added;
    }

    private async Task Log(NpgsqlConnection connection, NpgsqlTransaction transaction, long characterId, string action, StoredItem? from, StoredItem? to, CancellationToken cancel)
    {
        StoredItem item = to ?? from!;
        await using var line = new NpgsqlCommand(
            $"insert into {log} (character_id, item_row, item_id, quantity, action, from_place, from_slot, to_place, to_slot) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
            connection,
            transaction);
        line.Parameters.AddWithValue(characterId);
        line.Parameters.AddWithValue(item.Id);
        line.Parameters.AddWithValue(item.ItemId);
        line.Parameters.AddWithValue(item.Quantity);
        line.Parameters.AddWithValue(action);
        line.Parameters.AddWithValue(from == null ? DBNull.Value : (object)(short)from.Place);
        line.Parameters.AddWithValue(from == null ? DBNull.Value : (object)(short)from.Slot);
        line.Parameters.AddWithValue(to == null ? DBNull.Value : (object)(short)to.Place);
        line.Parameters.AddWithValue(to == null ? DBNull.Value : (object)(short)to.Slot);
        await line.ExecuteNonQueryAsync(cancel);
    }
}

/// <summary>Items kept only while the server runs: for tests, players without accounts and running without a database.</summary>
public sealed class InMemoryItemStore : IItemStore
{
    private readonly object gate = new();
    private readonly Dictionary<long, List<StoredItem>> owned = new();
    private readonly HashSet<long> given = new();
    private readonly Dictionary<long, (string MapId, double X)> positions = new();
    private long lastId;

    /// <summary>Every change, as the database's item log keeps it: (character, action, item before, item after).</summary>
    public List<(long Character, string Action, StoredItem? From, StoredItem? To)> Log { get; } = new();

    public Task<StoredBag> LoadAsync(long characterId, IReadOnlyList<StoredItem> starter, CancellationToken cancel = default)
    {
        lock (gate)
        {
            List<StoredItem> items = Items(characterId);

            if (given.Add(characterId))
            {
                foreach (StoredItem item in starter)
                {
                    StoredItem added = item with { Id = ++lastId };
                    items.Add(added);
                    Log.Add((characterId, "starter", null, added));
                }
            }

            return Task.FromResult(positions.TryGetValue(characterId, out (string MapId, double X) spot)
                ? new StoredBag(items.ToList(), spot.X, spot.MapId)
                : new StoredBag(items.ToList(), null));
        }
    }

    public Task ApplyAsync(long characterId, string action, IReadOnlyList<(StoredItem Before, StoredItem After)> moves, IReadOnlyList<StoredItem> removed, CancellationToken cancel = default)
    {
        lock (gate)
        {
            List<StoredItem> items = Items(characterId);
            var after = items.ToList();

            foreach ((StoredItem before, StoredItem now) in moves)
            {
                int at = after.FindIndex(item => item == before);

                if (at < 0)
                {
                    throw new StaleItemsException(characterId);
                }

                after[at] = now;
            }

            foreach (StoredItem item in removed)
            {
                if (!after.Remove(item))
                {
                    throw new StaleItemsException(characterId);
                }
            }

            if (after.GroupBy(item => (item.Place, item.Slot)).Any(group => group.Count() > 1))
            {
                throw new StaleItemsException(characterId);
            }

            items.Clear();
            items.AddRange(after);
            Log.AddRange(moves.Select(move => (characterId, action, (StoredItem?)move.Before, (StoredItem?)move.After)));
            Log.AddRange(removed.Select(item => (characterId, action, (StoredItem?)item, (StoredItem?)null)));
            return Task.CompletedTask;
        }
    }

    public Task<StoredItem> AddAsync(long characterId, string action, StoredItem item, CancellationToken cancel = default)
    {
        lock (gate)
        {
            List<StoredItem> items = Items(characterId);

            if (items.Any(other => other.Place == item.Place && other.Slot == item.Slot))
            {
                throw new StaleItemsException(characterId);
            }

            StoredItem added = item with { Id = ++lastId };
            items.Add(added);
            Log.Add((characterId, action, null, added));
            return Task.FromResult(added);
        }
    }

    public Task SavePositionAsync(long characterId, string mapId, double x, CancellationToken cancel = default)
    {
        lock (gate)
        {
            positions[characterId] = (mapId, x);
            return Task.CompletedTask;
        }
    }

    private List<StoredItem> Items(long characterId)
    {
        if (!owned.TryGetValue(characterId, out List<StoredItem>? items))
        {
            owned[characterId] = items = new List<StoredItem>();
        }

        return items;
    }
}
