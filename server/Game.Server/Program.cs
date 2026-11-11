using System.Text.Json;
using Game.Rules;
using Game.Server;
using Game.Server.Db;
using Npgsql;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// Usage: dotnet run --project server/Game.Server -- [--urls http://0.0.0.0:7777] [--config <dir>]
//        [--web <dir with the WebGL build>] [--fake-latency-ms 75] [--db <connection string> | memory] [--db-schema dakiem]
WebApplication app = ServerApp.Build(args);
app.Run();

public static class ServerApp
{
    public const string DefaultUrl = "http://0.0.0.0:7777";

    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        string configDir = builder.Configuration["config"] ?? FindUp("config");
        var files = GameConfig.FileNames.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(configDir, name)));
        GameConfig config = GameConfig.Parse(files);
        ServerConfig serverConfig = GameConfig.ParseServer(File.ReadAllText(Path.Combine(configDir, GameConfig.ServerFile)));
        TimeSpan fakeDelay = TimeSpan.FromMilliseconds(double.Parse(builder.Configuration["fake-latency-ms"] ?? "0"));

        builder.Services.AddSingleton(new GameServer(config, serverConfig));
        DatabaseInfo database = AddDatabase(builder, args);
        WebApplication app = builder.Build();
        GameServer server = app.Services.GetRequiredService<GameServer>();
        ICharacterStore characters = app.Services.GetRequiredService<ICharacterStore>();
        app.Logger.LogInformation(
            "Characters are kept in {Kind}{Where}",
            characters.Kind,
            database.Schema == null ? " only while the server runs" : $", schema {database.Schema} at version {database.Version}");
        server.Start(app.Lifetime.ApplicationStopping);
        app.Logger.LogInformation(
            "{Channels} channels, {Players} players each, map {Map}, fake latency {Delay} ms each way",
            serverConfig.Channels,
            serverConfig.PlayersPerChannel,
            configDir,
            fakeDelay.TotalMilliseconds);

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
        ServeWebBuild(app, builder.Configuration["web"]);

        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connection = new WebSocketConnection(socket, server, app.Logger, fakeDelay);
            await connection.RunAsync(context.RequestAborted);
        });

        app.MapGet("/status", async (CancellationToken cancel) => Results.Json(new
        {
            players = server.SessionCount,
            channels = server.Runners.Select(runner => new
            {
                id = runner.Id,
                tick = runner.CurrentTick,
                players = server.Occupancy()[runner.Id],
            }),
            database = new
            {
                kind = characters.Kind,
                schema = database.Schema,
                version = database.Version,
                characters = await characters.CountAsync(cancel),
            },
        }, new JsonSerializerOptions { WriteIndented = true }));

        return app;
    }

    /// <summary>Where characters are kept, as /status reports it. A null schema means memory only.</summary>
    private sealed record DatabaseInfo(string? Schema, int Version);

    /// <summary>
    /// With a connection string, the schema is brought up to date before the server opens, and a
    /// database that cannot be reached stops it: better than playing for hours and losing it all.
    /// Without one, characters live in memory and a warning says so.
    /// </summary>
    private static DatabaseInfo AddDatabase(WebApplicationBuilder builder, string[] args)
    {
        string? localSettings = Database.FindLocalSettings();

        if (localSettings != null)
        {
            builder.Configuration.AddJsonFile(localSettings, optional: true, reloadOnChange: false);
        }

        // Command-line values win over the local file, so a test can point the server elsewhere.
        builder.Configuration.AddCommandLine(args);
        string? connectionString = Database.ConnectionString(builder.Configuration);

        if (connectionString == null)
        {
            Console.WriteLine($"No database (--db memory, or none in --db, DAKIEM_DB or \"db\" in {Database.LocalSettingsFile}); characters are kept in memory only.");
            builder.Services.AddSingleton<ICharacterStore, InMemoryCharacterStore>();
            return new DatabaseInfo(null, 0);
        }

        string schema = builder.Configuration["db-schema"] ?? Database.DefaultSchema;
        NpgsqlDataSource source = Database.CreateSource(connectionString);
        (ICharacterStore store, int version) = Database.OpenAsync(source, schema).GetAwaiter().GetResult();
        builder.Services.AddSingleton(source);
        builder.Services.AddSingleton(store);
        return new DatabaseInfo(schema, version);
    }

    /// <summary>The WebGL build, served from the same address so the page can reach /ws without CORS.</summary>
    private static void ServeWebBuild(WebApplication app, string? webDir)
    {
        webDir ??= Path.Combine(Path.GetDirectoryName(FindUp("config"))!, "client", "Builds", "Web");

        if (!Directory.Exists(webDir))
        {
            app.Logger.LogInformation("No web build at {Dir}; only /ws and /status are served", webDir);
            return;
        }

        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".data"] = "application/octet-stream";
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".symbols.json"] = "application/octet-stream";
        var files = new PhysicalFileProvider(Path.GetFullPath(webDir));
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = types,
            OnPrepareResponse = context => MarkGzip(context.Context.Response, context.File.Name, types),
        });
        app.Logger.LogInformation("Serving the web build from {Dir}", webDir);
    }

    /// <summary>Unity's .gz files are sent as the file inside, so the browser unpacks them without the fallback loader.</summary>
    public static void MarkGzip(HttpResponse response, string fileName, FileExtensionContentTypeProvider types)
    {
        if (!fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string inner = fileName[..^".gz".Length];
        response.Headers.ContentEncoding = "gzip";
        response.ContentType = types.TryGetContentType(inner, out string? type) ? type : "application/octet-stream";
    }

    private static string FindUp(string name)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, name);

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, GameConfig.ServerFile)))
            {
                return candidate;
            }
        }

        for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, name);

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, GameConfig.ServerFile)))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"no {name}/ folder with {GameConfig.ServerFile} found; pass --config");
    }
}
