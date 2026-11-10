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

        app = ServerApp.Build(new[] { "--urls", "http://127.0.0.1:0", "--config", TestConfig.Dir, "--web", webDir });
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
}
