using System.Text.Json;
using Game.Server.Db;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Game.Net.Tests;

/// <summary>
/// A test that needs the real database. It is skipped, not failed, when no connection string is
/// set up or the database cannot be reached; a database that answers but refuses (a wrong
/// password, say) still fails, so a broken setup is noticed.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    private static readonly Lazy<(string? ConnectionString, string? SkipReason)> Probe = new(Check);

    public DatabaseFactAttribute()
    {
        if (Probe.Value.SkipReason != null)
        {
            Skip = Probe.Value.SkipReason;
        }
    }

    public static string ConnectionString => Probe.Value.ConnectionString ?? throw new InvalidOperationException(Probe.Value.SkipReason);

    private static (string?, string?) Check()
    {
        string? text = Environment.GetEnvironmentVariable("DAKIEM_DB");
        string? file = Database.FindLocalSettings();

        if (string.IsNullOrWhiteSpace(text) && file != null)
        {
            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(file));
            text = settings.RootElement.TryGetProperty("db", out JsonElement db) ? db.GetString() : null;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["db"] = text }).Build();
        string? connectionString = Database.ConnectionString(configuration);

        if (connectionString == null)
        {
            return (null, $"No database: set DAKIEM_DB or fill the password in {Database.LocalSettingsFile}.");
        }

        try
        {
            using NpgsqlDataSource source = Database.CreateSource(connectionString);
            using NpgsqlConnection connection = source.OpenConnection();
            return (connectionString, null);
        }
        catch (PostgresException)
        {
            // The server answered and said no: let the tests run and show why.
            return (connectionString, null);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or System.Net.Sockets.SocketException)
        {
            return (null, $"Database unreachable: {error.Message}");
        }
    }
}

/// <summary>The same expectations for every store, so memory and PostgreSQL cannot drift apart.</summary>
internal static class StoreContract
{
    public static async Task CreatesThenFindsTheSameCharacter(ICharacterStore store)
    {
        CharacterRecord created = await store.CreateAsync("  Thủy Kiếm  ");
        CharacterRecord? found = await store.FindAsync(created.Id);

        Assert.True(created.Id > 0);
        Assert.Equal("Thủy Kiếm", created.DisplayName);
        Assert.Equal(0, created.Revision);
        Assert.Equal(created, found);
    }

    public static async Task EachCharacterGetsItsOwnId(ICharacterStore store)
    {
        CharacterRecord first = await store.CreateAsync(null);
        CharacterRecord second = await store.CreateAsync(null);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Null(first.DisplayName);
    }

    public static async Task UnknownIdIsNotFound(ICharacterStore store)
    {
        Assert.Null(await store.FindAsync(long.MaxValue));
    }

    public static async Task SaveRaisesTheRevisionAndKeepsTheChange(ICharacterStore store)
    {
        CharacterRecord created = await store.CreateAsync("Cũ");

        CharacterRecord saved = await store.SaveAsync(created with { DisplayName = "Mới" });
        CharacterRecord? found = await store.FindAsync(created.Id);

        Assert.Equal(1, saved.Revision);
        Assert.Equal("Mới", found!.DisplayName);
        Assert.Equal(saved, found);
        Assert.True(saved.UpdatedAt >= created.UpdatedAt);
        Assert.Equal(created.CreatedAt, saved.CreatedAt);
    }

    public static async Task SavingAnOldCopyIsRefused(ICharacterStore store)
    {
        CharacterRecord created = await store.CreateAsync("A");
        await store.SaveAsync(created with { DisplayName = "B" });

        StaleCharacterException error = await Assert.ThrowsAsync<StaleCharacterException>(() => store.SaveAsync(created with { DisplayName = "C" }));

        Assert.Equal(created.Id, error.Id);
        Assert.Equal("B", (await store.FindAsync(created.Id))!.DisplayName);
    }

    public static async Task SavingAMissingCharacterFails(ICharacterStore store)
    {
        var ghost = new CharacterRecord(long.MaxValue, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SaveAsync(ghost));
    }

    public static async Task OnlyOneOfTwoRacingSavesWins(ICharacterStore store)
    {
        CharacterRecord created = await store.CreateAsync("Gốc");

        Task<CharacterRecord>[] saves = Enumerable.Range(0, 8)
            .Select(index => Task.Run(() => store.SaveAsync(created with { DisplayName = $"Lần {index}" })))
            .ToArray();
        int won = 0;

        foreach (Task<CharacterRecord> save in saves)
        {
            try
            {
                await save;
                won++;
            }
            catch (StaleCharacterException)
            {
            }
        }

        Assert.Equal(1, won);
        Assert.Equal(1, (await store.FindAsync(created.Id))!.Revision);
    }

    public static async Task NamesTooLongAreRefused(ICharacterStore store)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(new string('x', CharacterRules.MaxNameLength + 1)));
        Assert.Equal(CharacterRules.MaxNameLength, (await store.CreateAsync(new string('x', CharacterRules.MaxNameLength))).DisplayName!.Length);
    }

    public static async Task CountsWhatWasCreated(ICharacterStore store)
    {
        long before = await store.CountAsync();
        await store.CreateAsync(null);
        await store.CreateAsync(null);

        Assert.Equal(before + 2, await store.CountAsync());
    }
}

public class InMemoryCharacterStoreTests
{
    private readonly InMemoryCharacterStore store = new();

    [Fact] public Task CreatesThenFindsTheSameCharacter() => StoreContract.CreatesThenFindsTheSameCharacter(store);
    [Fact] public Task EachCharacterGetsItsOwnId() => StoreContract.EachCharacterGetsItsOwnId(store);
    [Fact] public Task UnknownIdIsNotFound() => StoreContract.UnknownIdIsNotFound(store);
    [Fact] public Task SaveRaisesTheRevisionAndKeepsTheChange() => StoreContract.SaveRaisesTheRevisionAndKeepsTheChange(store);
    [Fact] public Task SavingAnOldCopyIsRefused() => StoreContract.SavingAnOldCopyIsRefused(store);
    [Fact] public Task SavingAMissingCharacterFails() => StoreContract.SavingAMissingCharacterFails(store);
    [Fact] public Task OnlyOneOfTwoRacingSavesWins() => StoreContract.OnlyOneOfTwoRacingSavesWins(store);
    [Fact] public Task NamesTooLongAreRefused() => StoreContract.NamesTooLongAreRefused(store);
    [Fact] public Task CountsWhatWasCreated() => StoreContract.CountsWhatWasCreated(store);
    [Fact] public void KindIsMemory() => Assert.Equal("memory", store.Kind);
}

/// <summary>
/// The real database, in a schema made for this run and dropped afterwards, so nothing the game
/// keeps is touched.
/// </summary>
public sealed class PostgresCharacterStoreTests : IAsyncLifetime
{
    private readonly string schema = "t041_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource? source;
    private ICharacterStore store = null!;

    public async Task InitializeAsync()
    {
        if (new DatabaseFactAttribute().Skip != null)
        {
            return;
        }

        source = Database.CreateSource(DatabaseFactAttribute.ConnectionString);
        (store, _) = await Database.OpenAsync(source, schema);
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

    [DatabaseFact] public Task CreatesThenFindsTheSameCharacter() => StoreContract.CreatesThenFindsTheSameCharacter(store);
    [DatabaseFact] public Task EachCharacterGetsItsOwnId() => StoreContract.EachCharacterGetsItsOwnId(store);
    [DatabaseFact] public Task UnknownIdIsNotFound() => StoreContract.UnknownIdIsNotFound(store);
    [DatabaseFact] public Task SaveRaisesTheRevisionAndKeepsTheChange() => StoreContract.SaveRaisesTheRevisionAndKeepsTheChange(store);
    [DatabaseFact] public Task SavingAnOldCopyIsRefused() => StoreContract.SavingAnOldCopyIsRefused(store);
    [DatabaseFact] public Task SavingAMissingCharacterFails() => StoreContract.SavingAMissingCharacterFails(store);
    [DatabaseFact] public Task OnlyOneOfTwoRacingSavesWins() => StoreContract.OnlyOneOfTwoRacingSavesWins(store);
    [DatabaseFact] public Task NamesTooLongAreRefused() => StoreContract.NamesTooLongAreRefused(store);
    [DatabaseFact] public Task CountsWhatWasCreated() => StoreContract.CountsWhatWasCreated(store);

    [DatabaseFact]
    public async Task MigratingAgainChangesNothing()
    {
        int again = await Migrator.MigrateAsync(source!, schema);

        Assert.Equal(Migrator.LatestVersion, again);
        await using NpgsqlCommand rows = source!.CreateCommand($"select count(*) from {Migrator.Quote(schema)}.schema_version");
        Assert.Equal((long)Migrator.All.Count, Convert.ToInt64(await rows.ExecuteScalarAsync()));
    }

    [DatabaseFact]
    public async Task CharactersSurviveANewConnection()
    {
        CharacterRecord created = await store.CreateAsync("Còn đó");

        await using NpgsqlDataSource other = Database.CreateSource(DatabaseFactAttribute.ConnectionString);
        CharacterRecord? found = await new PostgresCharacterStore(other, schema).FindAsync(created.Id);

        Assert.Equal(created, found);
    }

    [DatabaseFact]
    public async Task TheDatabaseRefusesANameTooLongEvenPastTheServerCheck()
    {
        await using NpgsqlCommand insert = source!.CreateCommand($"insert into {Migrator.Quote(schema)}.characters (display_name) values ($1)");
        insert.Parameters.AddWithValue(new string('x', CharacterRules.MaxNameLength + 1));

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}

public class DatabaseSetupTests
{
    private static string? ConnectionString(string? value)
    {
        return Database.ConnectionString(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["db"] = value }).Build());
    }

    [Fact]
    public void MigrationsAreNumberedFromOneWithoutGaps()
    {
        Assert.NotEmpty(Migrator.All);
        Assert.Equal(Enumerable.Range(1, Migrator.All.Count), Migrator.All.Select(migration => migration.Version));
        Assert.Equal("0001_characters", Migrator.All[0].Name);
        Assert.Contains("create table characters", Migrator.All[0].Sql);
    }

    [Theory]
    [InlineData("dakiem")]
    [InlineData("t041_abc123")]
    public void PlainSchemaNamesAreQuoted(string schema)
    {
        Assert.Equal($"\"{schema}\"", Migrator.Quote(schema));
    }

    [Theory]
    [InlineData("Dakiem")]
    [InlineData("dakiem; drop table characters")]
    [InlineData("")]
    [InlineData("a\"b")]
    public void AnythingElseIsRefusedAsASchemaName(string schema)
    {
        Assert.Throws<ArgumentException>(() => Migrator.Quote(schema));
    }

    [Fact]
    public void NoPasswordYetMeansNoDatabase()
    {
        Assert.Null(ConnectionString($"Host=x;Password={Database.PasswordPlaceholder}"));
        Assert.Null(ConnectionString("memory"));
        Assert.Null(ConnectionString(" MEMORY "));
        Assert.Equal("Host=x;Password=y", ConnectionString("Host=x;Password=y"));
    }

    [Fact]
    public void TheLocalSettingsFileIsNeverCommitted()
    {
        string? file = Database.FindLocalSettings();
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));

        Assert.Contains(Database.LocalSettingsFile, ignore);
        Assert.True(file == null || file.EndsWith(Database.LocalSettingsFile));
    }
}
