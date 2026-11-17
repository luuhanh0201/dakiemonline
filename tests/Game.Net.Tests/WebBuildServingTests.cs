using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Net.Tests;

/// <summary>The server hands out the web build next to /ws, with Unity's gzip files marked so browsers unpack them.</summary>
public sealed class WebBuildServingTests : IAsyncLifetime
{
    private readonly string webDir = Path.Combine(Path.GetTempPath(), "dakiem-web-" + Guid.NewGuid().ToString("N"));
    private WebApplication app = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(webDir, "Build"));
        await File.WriteAllTextAsync(Path.Combine(webDir, "index.html"), "<html>game</html>");
        await File.WriteAllBytesAsync(Path.Combine(webDir, "Build", "Web.wasm.gz"), new byte[] { 0x1f, 0x8b, 8, 0 });
        await File.WriteAllBytesAsync(Path.Combine(webDir, "Build", "Web.framework.js.gz"), new byte[] { 0x1f, 0x8b, 8, 0 });
        await File.WriteAllBytesAsync(Path.Combine(webDir, "Build", "Web.data.gz"), new byte[] { 0x1f, 0x8b, 8, 0 });

        app = ServerApp.Build(new[] { "--urls", "http://127.0.0.1:0", "--config", TestConfig.Dir, "--web", webDir, "--db", "memory" });
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.None })
        {
            BaseAddress = new Uri(address),
        };
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        Directory.Delete(webDir, true);
    }

    [Fact]
    public async Task RootServesTheGamePage()
    {
        Assert.Equal("<html>game</html>", await http.GetStringAsync("/"));
    }

    [Fact]
    public async Task AndroidInstallerDownloadsSupportTheCorrectTypeAndResumeRanges()
    {
        Directory.CreateDirectory(Path.Combine(webDir, "downloads"));
        await File.WriteAllBytesAsync(Path.Combine(webDir, "downloads", "DaKiemOnline-2.apk"), new byte[] { 0x50, 0x4b, 3, 4, 5, 6, 7, 8 });
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/downloads/DaKiemOnline-2.apk"));
        head.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.android.package-archive", head.Content.Headers.ContentType!.MediaType);
        Assert.Equal(8, head.Content.Headers.ContentLength);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/downloads/DaKiemOnline-2.apk");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        using var partial = await http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(new byte[] { 0x50, 0x4b, 3, 4 }, await partial.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ContentPayloadsSupportPartialDownloads()
    {
        Directory.CreateDirectory(Path.Combine(webDir, "content", "blobs"));
        await File.WriteAllBytesAsync(Path.Combine(webDir, "content", "blobs", "fixture.bin"), new byte[] { 1, 2, 3, 4, 5, 6 });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/content/blobs/fixture.bin");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(3, null);
        using var response = await http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 4, 5, 6 }, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AndroidUpdateManifestPreservesVietnameseNotes()
    {
        Directory.CreateDirectory(Path.Combine(webDir, "updates"));
        const string manifest = "{\"versionCode\":2,\"releaseNotes\":\"Cập nhật APK\"}";
        await File.WriteAllTextAsync(Path.Combine(webDir, "updates", "android.json"), manifest);
        using var response = await http.GetAsync("/updates/android.json");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(manifest, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl!.NoCache);
    }

    [Fact]
    public async Task AClientWithDifferentRuleDataIsAskedToUpdateBeforeAccountActions()
    {
        using var status = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("/status"));
        string hash = status.RootElement.GetProperty("rulesHash").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/guest") { Content = System.Net.Http.Json.JsonContent.Create(new { secret = (string?)null }) };
        request.Headers.Add("X-Game-Rules", "outdated");
        using var response = await http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        using var error = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("content_outdated", error.RootElement.GetProperty("error").GetString());
        using var valid = new HttpRequestMessage(HttpMethod.Post, "/api/guest") { Content = System.Net.Http.Json.JsonContent.Create(new { secret = (string?)null }) };
        valid.Headers.Add("X-Game-Rules", hash);
        using var accepted = await http.SendAsync(valid);
        accepted.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/Build/Web.wasm.gz", "application/wasm")]
    [InlineData("/Build/Web.framework.js.gz", "text/javascript")]
    [InlineData("/Build/Web.data.gz", "application/octet-stream")]
    public async Task GzipFilesAreSentAsTheFileInside(string path, string type)
    {
        using HttpResponseMessage response = await http.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal(type, response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Build/Web.wasm.gz")]
    [InlineData("/Build/Web.framework.js.gz")]
    [InlineData("/Build/Web.data.gz")]
    public async Task BrowsersCheckEveryFileAgainBeforeReusingIt(string path)
    {
        using HttpResponseMessage response = await http.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoCache, $"{path} must be sent with Cache-Control: no-cache");
    }
}
