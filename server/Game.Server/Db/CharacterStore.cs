using System.Collections.Concurrent;
using Npgsql;

namespace Game.Server.Db;

/// <summary>
/// A saved character. T041 keeps only who it is; gear, position and stats come with T045.
/// Revision goes up with every save.
/// </summary>
public sealed record CharacterRecord(long Id, string? DisplayName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Revision);

/// <summary>A save that started from an older revision than the one stored, so it would overwrite newer data.</summary>
public sealed class StaleCharacterException : Exception
{
    public StaleCharacterException(long id, int revision)
        : base($"Character {id} was saved by someone else since revision {revision}; read it again before saving.")
    {
        Id = id;
        Revision = revision;
    }

    public long Id { get; }
    public int Revision { get; }
}

/// <summary>Where characters live between sessions. The game never reaches the database any other way.</summary>
public interface ICharacterStore
{
    /// <summary>"postgres" or "memory", for /status.</summary>
    string Kind { get; }

    Task<CharacterRecord> CreateAsync(string? displayName, CancellationToken cancel = default);

    Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default);

    /// <summary>
    /// Stores the record's fields if nobody saved since it was read, and returns it with the new
    /// revision. Throws <see cref="StaleCharacterException"/> otherwise, and KeyNotFoundException
    /// when there is no such character.
    /// </summary>
    Task<CharacterRecord> SaveAsync(CharacterRecord character, CancellationToken cancel = default);

    Task<long> CountAsync(CancellationToken cancel = default);
}

public static class CharacterRules
{
    public const int MaxNameLength = 32;

    /// <summary>Trims the name; empty means none. Throws when it is too long for the column.</summary>
    public static string? CleanName(string? name)
    {
        string? trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        if (trimmed != null && trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A name has at most {MaxNameLength} characters.", nameof(name));
        }

        return trimmed;
    }
}

/// <summary>Characters in PostgreSQL. Every call opens a pooled connection and runs one statement.</summary>
public sealed class PostgresCharacterStore : ICharacterStore
{
    private const string Columns = "id, display_name, created_at, updated_at, revision";

    private readonly NpgsqlDataSource source;
    private readonly string table;

    public PostgresCharacterStore(NpgsqlDataSource source, string schema)
    {
        this.source = source;
        table = $"{Migrator.Quote(schema)}.characters";
    }

    public string Kind => "postgres";

    public async Task<CharacterRecord> CreateAsync(string? displayName, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"insert into {table} (display_name) values ($1) returning {Columns}");
        command.Parameters.AddWithValue((object?)CharacterRules.CleanName(displayName) ?? DBNull.Value);
        return (await ReadOne(command, cancel))!;
    }

    public async Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select {Columns} from {table} where id = $1");
        command.Parameters.AddWithValue(id);
        return await ReadOne(command, cancel);
    }

    public async Task<CharacterRecord> SaveAsync(CharacterRecord character, CancellationToken cancel = default)
    {
        // One statement: the revision check and the write cannot be split by another save.
        await using NpgsqlCommand command = source.CreateCommand(
            $"update {table} set display_name = $3, updated_at = now(), revision = revision + 1 where id = $1 and revision = $2 returning {Columns}");
        command.Parameters.AddWithValue(character.Id);
        command.Parameters.AddWithValue(character.Revision);
        command.Parameters.AddWithValue((object?)CharacterRules.CleanName(character.DisplayName) ?? DBNull.Value);

        CharacterRecord? saved = await ReadOne(command, cancel);

        if (saved != null)
        {
            return saved;
        }

        return await FindAsync(character.Id, cancel) == null
            ? throw new KeyNotFoundException($"No character {character.Id}.")
            : throw new StaleCharacterException(character.Id, character.Revision);
    }

    public async Task<long> CountAsync(CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select count(*) from {table}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancel));
    }

    private static async Task<CharacterRecord?> ReadOne(NpgsqlCommand command, CancellationToken cancel)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancel);

        if (!await reader.ReadAsync(cancel))
        {
            return null;
        }

        return new CharacterRecord(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4));
    }
}

/// <summary>Characters kept only while the server runs: for tests, and for running without a database.</summary>
public sealed class InMemoryCharacterStore : ICharacterStore
{
    private readonly ConcurrentDictionary<long, CharacterRecord> characters = new();
    private long lastId;

    public string Kind => "memory";

    public Task<CharacterRecord> CreateAsync(string? displayName, CancellationToken cancel = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var record = new CharacterRecord(Interlocked.Increment(ref lastId), CharacterRules.CleanName(displayName), now, now, 0);
        characters[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default)
    {
        return Task.FromResult(characters.TryGetValue(id, out CharacterRecord? record) ? record : null);
    }

    public Task<CharacterRecord> SaveAsync(CharacterRecord character, CancellationToken cancel = default)
    {
        while (true)
        {
            if (!characters.TryGetValue(character.Id, out CharacterRecord? stored))
            {
                throw new KeyNotFoundException($"No character {character.Id}.");
            }

            if (stored.Revision != character.Revision)
            {
                throw new StaleCharacterException(character.Id, character.Revision);
            }

            CharacterRecord saved = stored with
            {
                DisplayName = CharacterRules.CleanName(character.DisplayName),
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = stored.Revision + 1,
            };

            if (characters.TryUpdate(character.Id, saved, stored))
            {
                return Task.FromResult(saved);
            }
        }
    }

    public Task<long> CountAsync(CancellationToken cancel = default) => Task.FromResult((long)characters.Count);
}
