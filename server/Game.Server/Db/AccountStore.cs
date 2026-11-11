using System.Collections.Concurrent;
using Npgsql;

namespace Game.Server.Db;

public enum AccountKind
{
    /// <summary>Made by "Chơi ngay"; only the device holding its secret can log in to it.</summary>
    Guest,

    /// <summary>Has a username, a password and a verified email or phone.</summary>
    Player,
}

public sealed record AccountRecord(
    long Id,
    string Username,
    string PasswordHash,
    AccountKind Kind,
    string? Contact,
    int FailedLogins,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt);

public sealed record SessionRecord(long AccountId, DateTimeOffset ExpiresAt);

/// <summary>A verification code waiting to be typed in. Only its hash is kept.</summary>
public sealed record CodeRecord(string Purpose, string Contact, byte[] CodeHash, int Attempts, DateTimeOffset SentAt, DateTimeOffset ExpiresAt);

/// <summary>Accounts, login sessions and verification codes. Characters are in <see cref="ICharacterStore"/>.</summary>
public interface IAccountStore
{
    /// <summary>Throws <see cref="DuplicateException"/> with "username", "contact" or "guest".</summary>
    Task<AccountRecord> CreateAsync(string username, string passwordHash, AccountKind kind, string? contact, byte[]? guestSecretHash, CancellationToken cancel = default);

    Task<AccountRecord?> FindByIdAsync(long id, CancellationToken cancel = default);

    Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancel = default);

    Task<AccountRecord?> FindByContactAsync(string contact, CancellationToken cancel = default);

    Task<AccountRecord?> FindGuestAsync(byte[] guestSecretHash, CancellationToken cancel = default);

    Task SetLoginFailuresAsync(long id, int failures, DateTimeOffset? lockedUntil, CancellationToken cancel = default);

    Task SetPasswordAsync(long id, string passwordHash, CancellationToken cancel = default);

    /// <summary>
    /// Turns a guest into a player with the same id, so its characters stay. Throws
    /// <see cref="DuplicateException"/> with "username" or "contact", and
    /// InvalidOperationException when the account is not a guest any more.
    /// </summary>
    Task<AccountRecord> PromoteGuestAsync(long id, string username, string passwordHash, string contact, CancellationToken cancel = default);

    /// <summary>Creates the session, or moves its expiry when it exists already.</summary>
    Task SaveSessionAsync(byte[] tokenHash, long accountId, DateTimeOffset expiresAt, CancellationToken cancel = default);

    /// <summary>The session, or null when it does not exist or has expired.</summary>
    Task<SessionRecord?> FindSessionAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancel = default);

    Task DeleteSessionAsync(byte[] tokenHash, CancellationToken cancel = default);

    Task DeleteSessionsAsync(long accountId, CancellationToken cancel = default);

    /// <summary>Replaces any code already waiting for the same purpose and contact.</summary>
    Task SaveCodeAsync(CodeRecord code, CancellationToken cancel = default);

    Task<CodeRecord?> FindCodeAsync(string purpose, string contact, CancellationToken cancel = default);

    Task SetCodeAttemptsAsync(string purpose, string contact, int attempts, CancellationToken cancel = default);

    Task DeleteCodeAsync(string purpose, string contact, CancellationToken cancel = default);
}

public sealed class PostgresAccountStore : IAccountStore
{
    private const string Columns = "id, username, password_hash, kind, contact, failed_logins, locked_until, created_at";

    private readonly NpgsqlDataSource source;
    private readonly string accounts;
    private readonly string sessions;
    private readonly string codes;

    public PostgresAccountStore(NpgsqlDataSource source, string schema)
    {
        this.source = source;
        string quoted = Migrator.Quote(schema);
        accounts = $"{quoted}.accounts";
        sessions = $"{quoted}.sessions";
        codes = $"{quoted}.verification_codes";
    }

    public async Task<AccountRecord> CreateAsync(string username, string passwordHash, AccountKind kind, string? contact, byte[]? guestSecretHash, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"insert into {accounts} (username, password_hash, kind, contact, guest_secret_hash) values ($1, $2, $3, $4, $5) returning {Columns}");
        command.Parameters.AddWithValue(username);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(KindText(kind));
        command.Parameters.AddWithValue((object?)contact ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)guestSecretHash ?? DBNull.Value);
        return (await ReadAccount(command, cancel))!;
    }

    public Task<AccountRecord?> FindByIdAsync(long id, CancellationToken cancel = default) => FindBy("id", id, cancel);

    public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancel = default) => FindBy("username", username, cancel);

    public Task<AccountRecord?> FindByContactAsync(string contact, CancellationToken cancel = default) => FindBy("contact", contact, cancel);

    public Task<AccountRecord?> FindGuestAsync(byte[] guestSecretHash, CancellationToken cancel = default) => FindBy("guest_secret_hash", guestSecretHash, cancel);

    public async Task SetLoginFailuresAsync(long id, int failures, DateTimeOffset? lockedUntil, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"update {accounts} set failed_logins = $2, locked_until = $3 where id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(failures);
        command.Parameters.AddWithValue((object?)lockedUntil ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task SetPasswordAsync(long id, string passwordHash, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"update {accounts} set password_hash = $2, failed_logins = 0, locked_until = null where id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(passwordHash);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task<AccountRecord> PromoteGuestAsync(long id, string username, string passwordHash, string contact, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"update {accounts} set username = $2, password_hash = $3, contact = $4, kind = 'player', guest_secret_hash = null where id = $1 and kind = 'guest' returning {Columns}");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(username);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(contact);
        return await ReadAccount(command, cancel) ?? throw new InvalidOperationException($"Account {id} is not a guest.");
    }

    public async Task SaveSessionAsync(byte[] tokenHash, long accountId, DateTimeOffset expiresAt, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"insert into {sessions} (token_hash, account_id, expires_at) values ($1, $2, $3) on conflict (token_hash) do update set expires_at = excluded.expires_at");
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(expiresAt);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task<SessionRecord?> FindSessionAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select account_id, expires_at from {sessions} where token_hash = $1 and expires_at > $2");
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(now);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancel);
        return await reader.ReadAsync(cancel) ? new SessionRecord(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    public async Task DeleteSessionAsync(byte[] tokenHash, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"delete from {sessions} where token_hash = $1");
        command.Parameters.AddWithValue(tokenHash);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task DeleteSessionsAsync(long accountId, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"delete from {sessions} where account_id = $1");
        command.Parameters.AddWithValue(accountId);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task SaveCodeAsync(CodeRecord code, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"insert into {codes} (purpose, contact, code_hash, attempts, sent_at, expires_at) values ($1, $2, $3, $4, $5, $6) "
            + "on conflict (purpose, contact) do update set code_hash = excluded.code_hash, attempts = excluded.attempts, sent_at = excluded.sent_at, expires_at = excluded.expires_at");
        command.Parameters.AddWithValue(code.Purpose);
        command.Parameters.AddWithValue(code.Contact);
        command.Parameters.AddWithValue(code.CodeHash);
        command.Parameters.AddWithValue(code.Attempts);
        command.Parameters.AddWithValue(code.SentAt);
        command.Parameters.AddWithValue(code.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task<CodeRecord?> FindCodeAsync(string purpose, string contact, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand(
            $"select purpose, contact, code_hash, attempts, sent_at, expires_at from {codes} where purpose = $1 and contact = $2");
        command.Parameters.AddWithValue(purpose);
        command.Parameters.AddWithValue(contact);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancel);

        if (!await reader.ReadAsync(cancel))
        {
            return null;
        }

        return new CodeRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<byte[]>(2),
            reader.GetInt32(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task SetCodeAttemptsAsync(string purpose, string contact, int attempts, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"update {codes} set attempts = $3 where purpose = $1 and contact = $2");
        command.Parameters.AddWithValue(purpose);
        command.Parameters.AddWithValue(contact);
        command.Parameters.AddWithValue(attempts);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task DeleteCodeAsync(string purpose, string contact, CancellationToken cancel = default)
    {
        await using NpgsqlCommand command = source.CreateCommand($"delete from {codes} where purpose = $1 and contact = $2");
        command.Parameters.AddWithValue(purpose);
        command.Parameters.AddWithValue(contact);
        await command.ExecuteNonQueryAsync(cancel);
    }

    private async Task<AccountRecord?> FindBy(string column, object value, CancellationToken cancel)
    {
        await using NpgsqlCommand command = source.CreateCommand($"select {Columns} from {accounts} where {column} = $1");
        command.Parameters.AddWithValue(value);
        return await ReadAccount(command, cancel);
    }

    private static async Task<AccountRecord?> ReadAccount(NpgsqlCommand command, CancellationToken cancel)
    {
        NpgsqlDataReader reader;

        try
        {
            reader = await command.ExecuteReaderAsync(cancel);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateException(error.ConstraintName switch
            {
                "accounts_contact" => "contact",
                "accounts_guest_secret" => "guest",
                _ => "username",
            });
        }

        await using (reader)
        {
            if (!await reader.ReadAsync(cancel))
            {
                return null;
            }

            return new AccountRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3) == "guest" ? AccountKind.Guest : AccountKind.Player,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7));
        }
    }

    private static string KindText(AccountKind kind) => kind == AccountKind.Guest ? "guest" : "player";
}

/// <summary>Accounts kept only while the server runs: for tests, and for running without a database.</summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly object gate = new();
    private readonly Dictionary<long, (AccountRecord Account, string? GuestSecret)> accounts = new();
    private readonly ConcurrentDictionary<string, (long AccountId, DateTimeOffset ExpiresAt)> sessions = new();
    private readonly ConcurrentDictionary<(string, string), CodeRecord> codes = new();
    private long lastId;

    public Task<AccountRecord> CreateAsync(string username, string passwordHash, AccountKind kind, string? contact, byte[]? guestSecretHash, CancellationToken cancel = default)
    {
        lock (gate)
        {
            string? secret = guestSecretHash == null ? null : Convert.ToHexString(guestSecretHash);
            CheckUnique(0, username, contact, secret);
            var account = new AccountRecord(++lastId, username, passwordHash, kind, contact, 0, null, DateTimeOffset.UtcNow);
            accounts[account.Id] = (account, secret);
            return Task.FromResult(account);
        }
    }

    public Task<AccountRecord?> FindByIdAsync(long id, CancellationToken cancel = default) => Find(entry => entry.Account.Id == id);

    public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancel = default) => Find(entry => entry.Account.Username == username);

    public Task<AccountRecord?> FindByContactAsync(string contact, CancellationToken cancel = default) => Find(entry => entry.Account.Contact == contact);

    public Task<AccountRecord?> FindGuestAsync(byte[] guestSecretHash, CancellationToken cancel = default)
    {
        string secret = Convert.ToHexString(guestSecretHash);
        return Find(entry => entry.GuestSecret == secret);
    }

    public Task SetLoginFailuresAsync(long id, int failures, DateTimeOffset? lockedUntil, CancellationToken cancel = default)
    {
        Update(id, account => account with { FailedLogins = failures, LockedUntil = lockedUntil });
        return Task.CompletedTask;
    }

    public Task SetPasswordAsync(long id, string passwordHash, CancellationToken cancel = default)
    {
        Update(id, account => account with { PasswordHash = passwordHash, FailedLogins = 0, LockedUntil = null });
        return Task.CompletedTask;
    }

    public Task<AccountRecord> PromoteGuestAsync(long id, string username, string passwordHash, string contact, CancellationToken cancel = default)
    {
        lock (gate)
        {
            if (!accounts.TryGetValue(id, out var entry) || entry.Account.Kind != AccountKind.Guest)
            {
                throw new InvalidOperationException($"Account {id} is not a guest.");
            }

            CheckUnique(id, username, contact, null);
            AccountRecord promoted = entry.Account with { Username = username, PasswordHash = passwordHash, Contact = contact, Kind = AccountKind.Player };
            accounts[id] = (promoted, null);
            return Task.FromResult(promoted);
        }
    }

    public Task SaveSessionAsync(byte[] tokenHash, long accountId, DateTimeOffset expiresAt, CancellationToken cancel = default)
    {
        sessions[Convert.ToHexString(tokenHash)] = (accountId, expiresAt);
        return Task.CompletedTask;
    }

    public Task<SessionRecord?> FindSessionAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancel = default)
    {
        bool found = sessions.TryGetValue(Convert.ToHexString(tokenHash), out var session) && session.ExpiresAt > now;
        return Task.FromResult(found ? new SessionRecord(session.AccountId, session.ExpiresAt) : null);
    }

    public Task DeleteSessionAsync(byte[] tokenHash, CancellationToken cancel = default)
    {
        sessions.TryRemove(Convert.ToHexString(tokenHash), out _);
        return Task.CompletedTask;
    }

    public Task DeleteSessionsAsync(long accountId, CancellationToken cancel = default)
    {
        foreach (var pair in sessions.Where(pair => pair.Value.AccountId == accountId).ToList())
        {
            sessions.TryRemove(pair.Key, out _);
        }

        return Task.CompletedTask;
    }

    public Task SaveCodeAsync(CodeRecord code, CancellationToken cancel = default)
    {
        codes[(code.Purpose, code.Contact)] = code;
        return Task.CompletedTask;
    }

    public Task<CodeRecord?> FindCodeAsync(string purpose, string contact, CancellationToken cancel = default)
    {
        return Task.FromResult(codes.TryGetValue((purpose, contact), out CodeRecord? code) ? code : null);
    }

    public Task SetCodeAttemptsAsync(string purpose, string contact, int attempts, CancellationToken cancel = default)
    {
        if (codes.TryGetValue((purpose, contact), out CodeRecord? code))
        {
            codes[(purpose, contact)] = code with { Attempts = attempts };
        }

        return Task.CompletedTask;
    }

    public Task DeleteCodeAsync(string purpose, string contact, CancellationToken cancel = default)
    {
        codes.TryRemove((purpose, contact), out _);
        return Task.CompletedTask;
    }

    private void CheckUnique(long self, string username, string? contact, string? guestSecret)
    {
        foreach ((AccountRecord other, string? otherSecret) in accounts.Values.Where(entry => entry.Account.Id != self))
        {
            if (other.Username == username)
            {
                throw new DuplicateException("username");
            }

            if (contact != null && other.Contact == contact)
            {
                throw new DuplicateException("contact");
            }

            if (guestSecret != null && otherSecret == guestSecret)
            {
                throw new DuplicateException("guest");
            }
        }
    }

    private Task<AccountRecord?> Find(Func<(AccountRecord Account, string? GuestSecret), bool> match)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.Values.Where(match).Select(entry => entry.Account).FirstOrDefault());
        }
    }

    private void Update(long id, Func<AccountRecord, AccountRecord> change)
    {
        lock (gate)
        {
            if (accounts.TryGetValue(id, out var entry))
            {
                accounts[id] = (change(entry.Account), entry.GuestSecret);
            }
        }
    }
}
