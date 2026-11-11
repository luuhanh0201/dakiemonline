using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;

namespace Game.Server.Db;

/// <summary>
/// Brings a schema up to date from the numbered SQL files in Db/Migrations, each in its own
/// transaction, and records which ones ran. Two servers starting together wait for each other.
/// </summary>
public static partial class Migrator
{
    // Any number; it only has to be the same for every server that migrates this database.
    private const long LockKey = 0x44614B69656D;

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaName();

    public sealed record Migration(int Version, string Name, string Sql);

    /// <summary>The migrations built into the server, in order.</summary>
    public static IReadOnlyList<Migration> All { get; } = Load();

    public static int LatestVersion => All.Count == 0 ? 0 : All[^1].Version;

    /// <summary>Applies whatever has not run yet and returns the schema's version afterwards.</summary>
    public static async Task<int> MigrateAsync(NpgsqlDataSource source, string schema, CancellationToken cancel = default)
    {
        string quoted = Quote(schema);
        await using NpgsqlConnection connection = await source.OpenConnectionAsync(cancel);

        await Execute(connection, null, $"select pg_advisory_lock({LockKey})", cancel);

        try
        {
            await Execute(connection, null, $"create schema if not exists {quoted}", cancel);
            await Execute(
                connection,
                null,
                $"create table if not exists {quoted}.schema_version (version integer primary key, name text not null, applied_at timestamptz not null default now())",
                cancel);

            int current = await CurrentVersionAsync(connection, schema, cancel);

            foreach (Migration migration in All.Where(migration => migration.Version > current))
            {
                await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancel);
                await Execute(connection, transaction, $"set local search_path to {quoted}", cancel);
                await Execute(connection, transaction, migration.Sql, cancel);

                await using (var record = new NpgsqlCommand($"insert into {quoted}.schema_version (version, name) values ($1, $2)", connection, transaction))
                {
                    record.Parameters.AddWithValue(migration.Version);
                    record.Parameters.AddWithValue(migration.Name);
                    await record.ExecuteNonQueryAsync(cancel);
                }

                await transaction.CommitAsync(cancel);
                current = migration.Version;
            }

            return current;
        }
        finally
        {
            await Execute(connection, null, $"select pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
    }

    public static async Task<int> CurrentVersionAsync(NpgsqlConnection connection, string schema, CancellationToken cancel = default)
    {
        await using var command = new NpgsqlCommand($"select coalesce(max(version), 0) from {Quote(schema)}.schema_version", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancel));
    }

    /// <summary>A schema name made only of lowercase letters, digits and underscores, safe to put in SQL.</summary>
    public static string Quote(string schema)
    {
        return SchemaName().IsMatch(schema)
            ? $"\"{schema}\""
            : throw new ArgumentException($"'{schema}' is not a plain lowercase schema name", nameof(schema));
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancel)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancel);
    }

    private static IReadOnlyList<Migration> Load()
    {
        Assembly assembly = typeof(Migrator).Assembly;
        var migrations = new List<Migration>();

        foreach (string resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Migrations.") && name.EndsWith(".sql")))
        {
            string name = resource["Migrations.".Length..^".sql".Length];
            int underscore = name.IndexOf('_');

            if (underscore <= 0 || !int.TryParse(name[..underscore], out int version) || version <= 0)
            {
                throw new InvalidOperationException($"Migration '{resource}' must be named like 0001_what_it_does.sql");
            }

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            migrations.Add(new Migration(version, name, reader.ReadToEnd()));
        }

        migrations.Sort((left, right) => left.Version.CompareTo(right.Version));

        for (int index = 0; index < migrations.Count; index++)
        {
            if (migrations[index].Version != index + 1)
            {
                throw new InvalidOperationException($"Migrations must be numbered 1, 2, 3… without gaps; found {migrations[index].Version} at place {index + 1}");
            }
        }

        return migrations;
    }
}
