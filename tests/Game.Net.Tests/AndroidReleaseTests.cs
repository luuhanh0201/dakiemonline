using DaKiem.Client.Accounts;

namespace Game.Net.Tests;

public sealed class AndroidReleaseTests
{
    private static AndroidReleaseInfo Release(string url = "https://dakiem.online/downloads/DaKiemOnline-2.apk") => new()
    {
        packageId = "com.dakiemonline.game", versionCode = 2, versionName = "1.1.0", apkUrl = url,
    };

    [Fact]
    public void OnlyANewerBuildOfTheInstalledPackageIsOffered()
    {
        Assert.True(Release().IsUpdateFor(1, "com.dakiemonline.game"));
        Assert.False(Release().IsUpdateFor(2, "com.dakiemonline.game"));
        Assert.False(Release().IsUpdateFor(3, "com.dakiemonline.game"));
        Assert.False(Release().IsUpdateFor(0, "com.dakiemonline.game"));
        Assert.False(Release().IsUpdateFor(1, "another.package"));
    }

    [Theory]
    [InlineData("http://dakiem.online/downloads/new.apk")]
    [InlineData("https://dakiem.online.evil.example/downloads/new.apk")]
    [InlineData("https://evil.example/downloads/new.apk")]
    [InlineData("https://dakiem.online:444/downloads/new.apk")]
    [InlineData("https://user:password@dakiem.online/downloads/new.apk")]
    [InlineData("https://dakiem.online/downloads/../../wrong.apk")]
    [InlineData("https://dakiem.online/downloads/page.html")]
    [InlineData("not a URL")]
    public void AManifestCannotRedirectPlayersToAnUntrustedInstaller(string url) =>
        Assert.False(Release(url).IsUpdateFor(1, "com.dakiemonline.game"));

    [Fact]
    public void MissingManifestFieldsDoNotOfferAnUpdate() =>
        Assert.False(new AndroidReleaseInfo().IsUpdateFor(1, "com.dakiemonline.game"));
}
