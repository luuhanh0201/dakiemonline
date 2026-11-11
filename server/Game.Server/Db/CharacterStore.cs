using System.Collections.Concurrent;
using Npgsql;

namespace Game.Server.Db;

/// <summary>
/// A saved character. It belongs to one account on one game server; gear, position and stats
/// come with T045. Revision goes up with every save.
/// </summary>
public sealed record CharacterRecord(
    long Id,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Revision,
    long? AccountId = null,
    int? ServerNumber = null);

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

/// <summary>Something that must be unique already exists, such as a username or a character name on a server.</summary>
public sealed class DuplicateException : Exception
{
    public DuplicateException(string field)
        : base($"That {field} is already taken.")
    {
        Field = field;
    }

    /// <summary>"username", "contact", "guest", "character" (one per account and server) or "name".</summary>
    public string Field { get; }
}

/// <summary>Where characters live between sessions. The game never reaches the database any other way.</summary>
public interface ICharacterStore
{
    /// <summary>"postgres" or "memory", for /status.</summary>
    string Kind { get; }

    Task<CharacterRecord> CreateAsync(string? displayName, CancellationToken cancel = default);

    /// <summary>
    /// The account's character on a server. Throws <see cref="DuplicateException"/> with "character"
    /// when the account already has one there, or "name" when another character there has the name.
    /// </summary>
    Task<CharacterRecord> CreateForAccountAsync(long accountId, int serverNumber, string name, CancellationToken cancel = default);

    Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default);

    Task<CharacterRecord?> FindByAccountAsync(long accountId, int serverNumber, CancellationToken cancel = default);

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

    /// <summary>What two names must differ in to both exist on one server: case does not count, marks do.</summary>
    public static string? NameKey(string? name) => CleanName(name)?.ToLowerInvariant();
}

/// <summary>Characters in PostgreSQL. Every call opens a pooled connection and runs one statement.</summary>
public sealed class PostgresCharacterStore : ICharacterStore
{
    private const string Columns = "id, display_name, created_at, updated_at, revision, account_id, server_number";

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

    public async Task<CharacterRecord> CreateForAccountAsync(long accountId, int serverNumber, string name, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"insert into {table} (display_name, name_key, account_id, server_number) values ($1, $2, $3, $4) returning {Columns}");
        command.Parameters.AddWithValue(CharacterRules.CleanName(name)!);
        command.Parameters.AddWithValue(CharacterRules.NameKey(name)!);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(serverNumber);

        try
        {
            return (await ReadOne(command, cancel))!;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateException(error.ConstraintName == "characters_name_server" ? "name" : "character");
        }
    }

    public async Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select {Columns} from {table} where id = $1");
        command.Parameters.AddWithValue(id);
        return await ReadOne(command, cancel);
    }

    public async Task<CharacterRecord?> FindByAccountAsync(long accountId, int serverNumber, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select {Columns} from {table} where account_id = $1 and server_number = $2");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(serverNumber);
        return await ReadOne(command, cancel);
    }

    public async Task<CharacterRecord> SaveAsync(CharacterRecord character, CancellationToken cancel = default)
    {
        // One statement: the revision check and the write cannot be split by another save.
        await using NpgsqlCommand command = source.CreateCommand(
            $"update {table} set display_name = $3, name_key = $4, updated_at = now(), revision = revision + 1 where id = $1 and revision = $2 returning {Columns}");
        command.Parameters.AddWithValue(character.Id);
        command.Parameters.AddWithValue(character.Revision);
        command.Parameters.AddWithValue((object?)CharacterRules.CleanName(character.DisplayName) ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)CharacterRules.NameKey(character.DisplayName) ?? DBNull.Value);

        CharacterRecord? saved;

        try
        {
            saved = await ReadOne(command, cancel);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateException("name");
        }

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
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6));
    }
}

/// <summary>Characters kept only while the server runs: for tests, and for running without a database.</summary>
public sealed class InMemoryCharacterStore : ICharacterStore
{
    private readonly ConcurrentDictionary<long, CharacterRecord> characters = new();
    private readonly object gate = new();
    private long lastId;

    public string Kind => "memory";

    public Task<CharacterRecord> CreateAsync(string? displayName, CancellationToken cancel = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var record = new CharacterRecord(Interlocked.Increment(ref lastId), CharacterRules.CleanName(displayName), now, now, 0);
        characters[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<CharacterRecord> CreateForAccountAsync(long accountId, int serverNumber, string name, CancellationToken cancel = default)
    {
        lock (gate)
        {
            string? key = CharacterRules.NameKey(name);

            if (characters.Values.Any(other => other.AccountId == accountId && other.ServerNumber == serverNumber))
            {
                throw new DuplicateException("character");
            }

            if (characters.Values.Any(other => other.ServerNumber == serverNumber && CharacterRules.NameKey(other.DisplayName) == key))
            {
                throw new DuplicateException("name");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var record = new CharacterRecord(++lastId, CharacterRules.CleanName(name), now, now, 0, accountId, serverNumber);
            characters[record.Id] = record;
            return Task.FromResult(record);
        }
    }

    public Task<CharacterRecord?> FindAsync(long id, CancellationToken cancel = default)
    {
        return Task.FromResult(characters.TryGetValue(id, out CharacterRecord? record) ? record : null);
    }

    public Task<CharacterRecord?> FindByAccountAsync(long accountId, int serverNumber, CancellationToken cancel = default)
    {
        return Task.FromResult(characters.Values.FirstOrDefault(record => record.AccountId == accountId && record.ServerNumber == serverNumber));
    }

    public Task<CharacterRecord> SaveAsync(CharacterRecord character, CancellationToken cancel = default)
    {
        lock (gate)
        {
            if (!characters.TryGetValue(character.Id, out CharacterRecord? stored))
            {
                throw new KeyNotFoundException($"No character {character.Id}.");
            }

            if (stored.Revision != character.Revision)
            {
                throw new StaleCharacterException(character.Id, character.Revision);
            }

            string? key = CharacterRules.NameKey(character.DisplayName);

            if (stored.ServerNumber != null && key != null
                && characters.Values.Any(other => other.Id != stored.Id && other.ServerNumber == stored.ServerNumber && CharacterRules.NameKey(other.DisplayName) == key))
            {
                throw new DuplicateException("name");
            }

            CharacterRecord saved = stored with
            {
                DisplayName = CharacterRules.CleanName(character.DisplayName),
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = stored.Revision + 1,
            };

            characters[character.Id] = saved;
            return Task.FromResult(saved);
        }
    }

    public Task<long> CountAsync(CancellationToken cancel = default) => Task.FromResult((long)characters.Count);
}
