using System.Text;
using System.Text.Json;
using DaKiem.Client.Content;

namespace Game.Net.Tests;

public sealed class ContentPatchTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dakiem-content-test-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static ContentFile Text(string text, string key = "resources/UI/notice") => new()
    {
        key = key, resource = key.StartsWith("resources/") ? key[10..] : "", kind = key.StartsWith("config/") ? "config" : "text",
        sha256 = ContentHash.Text(text), size = Encoding.UTF8.GetByteCount(text),
    };
    private static ContentManifest Manifest(params ContentFile[] files) => new()
    {
        platform = "windows", buildCode = 5, version = 0, minClientCode = 5, rulesHash = ContentHash.Text("rules"), files = files,
    };
    private static string Serialize(ContentManifest manifest) => JsonSerializer.Serialize(manifest, Json);
    private static ContentManifest Clone(ContentManifest manifest) => JsonSerializer.Deserialize<ContentManifest>(Serialize(manifest), Json)!;
    private void Put(ContentCache cache, ContentFile file, string text)
    {
        File.WriteAllText(cache.PartPath(file), text, new UTF8Encoding(false));
        cache.AcceptPart(file);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("resources/../../account.token")]
    [InlineData("/resources/UI/a")]
    [InlineData("resources\\UI\\a")]
    [InlineData("config/sub/folder.json")]
    public void ManifestKeysCannotEscapeTheirLogicalNamespaces(string key)
    {
        var file = Text("abc", key);
        var manifest = Manifest(file);
        Assert.Throws<InvalidDataException>(() => manifest.Validate("windows", 5));
    }

    [Fact]
    public void ManifestRejectsDuplicateKeysOtherPlatformsAndIncompatibleClients()
    {
        var manifest = Manifest(Text("a"), Text("b"));
        Assert.Throws<InvalidDataException>(() => manifest.Validate("windows", 5));
        manifest.files = new[] { Text("a") };
        Assert.Throws<InvalidDataException>(() => manifest.Validate("android", 5));
        manifest.minClientCode = 6;
        Assert.Throws<InvalidDataException>(() => manifest.Validate("windows", 5));
        manifest.minClientCode = 5; manifest.maxClientCode = 5;
        Assert.Throws<InvalidDataException>(() => manifest.Validate("windows", 6));
    }

    [Fact]
    public void NativeUnchangedPayloadNeedsNoDownloadButMetadataChangesDo()
    {
        ContentFile native = Text("abc");
        ContentManifest baseline = Manifest(native);
        var cache = new ContentCache(root, baseline);
        Assert.True(cache.Complete(Clone(baseline)));
        Assert.False(File.Exists(cache.BlobPath(native)));
        ContentFile changed = Clone(baseline).files[0];
        changed.kind = "image";
        Assert.True(cache.NeedsPayload(changed));
    }

    [Fact]
    public void PartialAndCorruptedPayloadsCannotReplaceTheActiveSnapshot()
    {
        var cache = new ContentCache(root, Manifest(Text("native")));
        var first = Manifest(Text("valid-one")); first.version = 1;
        Put(cache, first.files[0], "valid-one");
        cache.Commit(first, Serialize(first));
        string before = File.ReadAllText(Path.Combine(root, "current.json"));
        var next = Manifest(Text("valid-two")); next.version = 2;
        File.WriteAllText(cache.PartPath(next.files[0]), "valid");
        Assert.Throws<InvalidDataException>(() => cache.AcceptPart(next.files[0]));
        Assert.Throws<InvalidDataException>(() => cache.Commit(next, Serialize(next)));
        File.WriteAllText(cache.PartPath(next.files[0]), "wrong-two");
        Assert.Throws<InvalidDataException>(() => cache.AcceptPart(next.files[0]));
        Assert.Equal(before, File.ReadAllText(Path.Combine(root, "current.json")));
        Assert.True(cache.Complete(first));
    }

    [Fact]
    public void ACompleteNewReleaseRetainsThePreviousManifestForRollback()
    {
        var cache = new ContentCache(root, Manifest(Text("native")));
        var first = Manifest(Text("one")); first.version = 1;
        Put(cache, first.files[0], "one"); cache.Commit(first, Serialize(first));
        var next = Manifest(Text("two")); next.version = 2;
        Put(cache, next.files[0], "two"); cache.Commit(next, Serialize(next));
        Assert.Equal(Serialize(next), File.ReadAllText(Path.Combine(root, "current.json")));
        Assert.Equal(Serialize(first), File.ReadAllText(Path.Combine(root, "previous.json")));
        File.WriteAllText(cache.BlobPath(next.files[0]), "bad");
        Assert.False(cache.Complete(next));
        Assert.True(cache.Complete(first));
    }

    [Fact]
    public void AHashCannotNameAnArbitraryFileInTheCache()
    {
        var cache = new ContentCache(root, Manifest());
        var file = Text("a"); file.sha256 = "../../account.token";
        Assert.Throws<InvalidDataException>(() => cache.BlobPath(file));
    }

    [Fact]
    public void TwoGameWindowsCannotWriteTheSamePartialFilesAtOnce()
    {
        var cache = new ContentCache(root, Manifest());
        using (var first = cache.AcquireLease()) Assert.Throws<IOException>(() => cache.AcquireLease());
        using var next = cache.AcquireLease();
        Assert.True(next.CanWrite);
    }

    [Fact]
    public void RulesFingerprintIsOrderIndependentAndChangesWithTheActualConfiguration()
    {
        var first = new Dictionary<string, string> { ["one"] = "x", ["two"] = "y" };
        var reordered = new Dictionary<string, string> { ["two"] = "y", ["one"] = "x" };
        Assert.Equal(ContentRules.Fingerprint(first), ContentRules.Fingerprint(reordered));
        reordered["one"] = "z";
        Assert.NotEqual(ContentRules.Fingerprint(first), ContentRules.Fingerprint(reordered));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
