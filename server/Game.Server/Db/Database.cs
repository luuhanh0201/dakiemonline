using Npgsql;

namespace Game.Server.Db;

/// <summary>
/// Finds the connection string and opens the character store. The string comes from, in order:
/// --db on the command line, the DAKIEM_DB environment variable, or "db" in
/// server/Game.Server/appsettings.Local.json, which git ignores so the password stays on this machine.
/// </summary>
public static class Database
{
    /// <summary>What appsettings.Local.json holds until the password is filled in.</summary>
    public const string PasswordPlaceholder = "DIEN_MAT_KHAU_VAO_DAY";

    /// <summary>
    /// Not "public": Supabase publishes the public schema through its web API, and the game's
    /// tables should only ever be reached through the game server.
    /// </summary>
    public const string DefaultSchema = "dakiem";

    public const string LocalSettingsFile = "appsettings.Local.json";

    /// <summary>--db memory keeps characters in memory even when a connection string is configured, as tests need.</summary>
    public const string Memory = "memory";

    /// <summary>The configured connection string, or null when there is none or the password was never filled in.</summary>
    public static string? ConnectionString(IConfiguration configuration)
    {
        string? text = configuration["db"];

        if (string.IsNullOrWhiteSpace(text))
        {
            text = Environment.GetEnvironmentVariable("DAKIEM_DB");
        }

        if (string.IsNullOrWhiteSpace(text)
            || string.Equals(text.Trim(), Memory, StringComparison.OrdinalIgnoreCase)
            || text.Contains(PasswordPlaceholder, StringComparison.Ordinal))
        {
            return null;
        }

        return text;
    }

    /// <summary>server/Game.Server/appsettings.Local.json, looked for upward from where the program runs.</summary>
    public static string? FindLocalSettings()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir != null; dir = dir.Parent)
            {
                foreach (string candidate in new[]
                {
                    Path.Combine(dir.FullName, LocalSettingsFile),
                    Path.Combine(dir.FullName, "server", "Game.Server", LocalSettingsFile),
                })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    public static NpgsqlDataSource CreateSource(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // A dead database should stop the server quickly, not hang it.
            Timeout = 15,
            ApplicationName = "dakiem-server",
        };

        return NpgsqlDataSource.Create(builder.ConnectionString);
    }

    /// <summary>Brings the schema up to date and returns stores on it, with the schema's version.</summary>
    public static async Task<(ICharacterStore Store, int Version)> OpenAsync(NpgsqlDataSource source, string schema, CancellationToken cancel = default)
    {
        int version = await Migrator.MigrateAsync(source, schema, cancel);
        return (new PostgresCharacterStore(source, schema), version);
    }

    public static IAccountStore Accounts(NpgsqlDataSource source, string schema) => new PostgresAccountStore(source, schema);
}
