using System.Text.Json;
using Game.Rules;
using Game.Server;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// Usage: dotnet run --project server/Game.Server -- [--urls http://0.0.0.0:7777] [--config <dir>]
//        [--web <dir with the WebGL build>] [--fake-latency-ms 75]
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
        WebApplication app = builder.Build();
        GameServer server = app.Services.GetRequiredService<GameServer>();
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

        app.MapGet("/status", () => Results.Json(new
        {
            players = server.SessionCount,
            channels = server.Runners.Select(runner => new
            {
                id = runner.Id,
                tick = runner.CurrentTick,
                players = server.Occupancy()[runner.Id],
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));

        return app;
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
