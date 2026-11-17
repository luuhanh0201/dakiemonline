using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using DaKiem.Client.Content;
using System.IO.Compression;

if (args.Length == 3 && args[0] == "--verify-public")
{
    string repo = Path.GetFullPath(args[1]);
    string reportDir = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(reportDir);
    var verified = new List<string>();
    var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
    using var publicHttp = new HttpClient();
    publicHttp.Timeout = TimeSpan.FromSeconds(30);
    foreach (string platform in new[] { "android", "windows" })
    {
        string nativeJson;
        if (platform == "android")
        {
            using ZipArchive apk = ZipFile.OpenRead(Path.Combine(repo, "client/Builds/AndroidRelease/downloads/DaKiemOnline-5.apk"));
            using var reader = new StreamReader(apk.GetEntry("assets/content/baseline.json")!.Open());
            nativeJson = reader.ReadToEnd();
        }
        else nativeJson = File.ReadAllText(Path.Combine(repo, "client/Builds/PC/DaKiemOnline_Data/StreamingAssets/content/baseline.json"));
        var native = JsonSerializer.Deserialize<ContentManifest>(nativeJson, jsonOptions)!;
        native.Validate(platform, native.buildCode);
        string publishedJson = await publicHttp.GetStringAsync("https://dakiem.online/content/" + platform + "/current.json?check=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var published = JsonSerializer.Deserialize<ContentManifest>(publishedJson, jsonOptions)!;
        published.Validate(platform, native.buildCode);
        var store = new ContentCache(Path.Combine(reportDir, platform), native);
        int changedFiles = published.files.Count(store.NeedsPayload);
        if (published.version != 1 || published.rulesHash != native.rulesHash || published.files.Length != native.files.Length || changedFiles != 0) throw new InvalidDataException("Initial public release differs from the shipped native baseline.");
        store.Commit(published, publishedJson);
        var restored = JsonSerializer.Deserialize<ContentManifest>(store.ReadSnapshots().First(), jsonOptions)!;
        if (restored.version != 1 || !store.Complete(restored)) throw new InvalidDataException("The real public manifest cannot be activated by the product cache.");
        foreach (ContentFile payload in new[] { published.files.First(file => file.kind == "image"), published.files.First(file => file.kind == "config") })
        {
            string payloadUrl = "https://dakiem.online/content/blobs/" + payload.sha256 + ".bin";
            byte[] bytes = await publicHttp.GetByteArrayAsync(payloadUrl);
            if (bytes.LongLength != payload.size || ContentHash.Bytes(bytes) != payload.sha256) throw new InvalidDataException("Public payload checksum mismatch.");
            using var request = new HttpRequestMessage(HttpMethod.Get, payloadUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
            using var response = await publicHttp.SendAsync(request);
            byte[] partial = await response.Content.ReadAsByteArrayAsync();
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent || !partial.SequenceEqual(bytes.Take(16))) throw new InvalidDataException("Public payload ranges do not match the verified bytes.");
        }
        verified.Add($"PASS {platform}: public release 1 validates; all {published.files.Length} entries match the native baseline; zero redundant downloads; cache activation, public image/config SHA and range downloads pass.");
    }
    using JsonDocument publicApk = JsonDocument.Parse(await publicHttp.GetStringAsync("https://dakiem.online/updates/android.json?check=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    string expectedApkHash = ContentHash.File(Path.Combine(repo, "client/Builds/AndroidRelease/downloads/DaKiemOnline-5.apk"));
    if (publicApk.RootElement.GetProperty("versionCode").GetInt32() != 5 || publicApk.RootElement.GetProperty("sha256").GetString() != expectedApkHash) throw new InvalidDataException("Public APK update manifest does not identify the shipped installer.");
    verified.Add("PASS public APK manifest identifies the verified 1.2.0 installer (code 5).");
    verified.Add("RESULT PASS");
    File.WriteAllLines(Path.Combine(reportDir, "checks.txt"), verified);
    foreach (string check in verified) Console.WriteLine(check);
    return;
}

if (args.Length != 3) throw new ArgumentException("Usage: Game.ContentTest <fixture directory> <PC exe> <output directory>");
string fixture = Path.GetFullPath(args[0]);
string executable = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "metadata.json")));
string slowHash = metadata.RootElement.GetProperty("slowHash").GetString()!;
using JsonDocument good = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "good.json")));
string rulesHash = good.RootElement.GetProperty("rulesHash").GetString()!;
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
await using WebApplication app = builder.Build();
var requests = new List<string>();
app.Use(async (context, next) =>
{
    string range = context.Request.Headers.Range.ToString();
    lock (requests) requests.Add(context.Request.Path + " Range=" + range);
    if (File.Exists(Path.Combine(fixture, "ignore-range.flag"))) context.Request.Headers.Remove("Range");
    if (context.Request.Path == "/content/blobs/" + slowHash + ".bin")
    {
        string path = Path.Combine(fixture, "content/blobs", slowHash + ".bin");
        await using var stream = File.OpenRead(path);
        long start = 0;
        Match match = Regex.Match(context.Request.Headers.Range.ToString(), "^bytes=([0-9]+)-$");
        if (match.Success) start = long.Parse(match.Groups[1].Value);
        if (start >= stream.Length) { context.Response.StatusCode = 416; return; }
        context.Response.StatusCode = match.Success ? 206 : 200;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength = stream.Length - start;
        context.Response.Headers.AcceptRanges = "bytes";
        if (match.Success) context.Response.Headers.ContentRange = $"bytes {start}-{stream.Length - 1}/{stream.Length}";
        stream.Position = start;
        var buffer = new byte[16384];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(5, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) when (context.RequestAborted.IsCancellationRequested) { }
        return;
    }
    await next();
});
var types = new FileExtensionContentTypeProvider();
types.Mappings[".bin"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(fixture), ContentTypeProvider = types, OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });
app.MapGet("/status", () => new { rulesHash });
await app.StartAsync();
string origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var launch = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
foreach (string argument in new[] { "-content-ui-smoke", output, "-content-origin", origin, "-content-fixture", fixture, "-screen-width", "1170", "-screen-height", "540", "-logFile", Path.Combine(output, "player.log") }) launch.ArgumentList.Add(argument);
using Process client = Process.Start(launch) ?? throw new InvalidOperationException("Cannot start the content test client.");
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await client.WaitForExitAsync(deadline.Token);
    if (client.ExitCode != 0) throw new InvalidOperationException("Content UI smoke failed; see checks.txt and player.log.");
    Console.WriteLine(File.ReadAllText(Path.Combine(output, "checks.txt")));
}
finally
{
    if (!client.HasExited) client.Kill(true);
    await app.StopAsync();
    lock (requests) File.WriteAllLines(Path.Combine(output, "http-requests.txt"), requests);
}
