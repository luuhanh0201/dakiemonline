using System;

namespace DaKiem.Client.Accounts
{
    [Serializable]
    public sealed class AndroidReleaseInfo
    {
        public string packageId = string.Empty;
        public int versionCode;
        public string versionName = string.Empty;
        public string apkUrl = string.Empty;
        public string releaseNotes = string.Empty;
        public long sizeBytes;
        public string sha256 = string.Empty;

        public bool IsUpdateFor(int installedCode, string installedPackage)
        {
            return installedCode > 0 && versionCode > installedCode
                && packageId == installedPackage && !string.IsNullOrWhiteSpace(versionName)
                && Uri.TryCreate(apkUrl, UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps && url.Host == "dakiem.online"
                && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo)
                && url.AbsolutePath.StartsWith("/downloads/", StringComparison.Ordinal)
                && url.AbsolutePath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
        }
    }
}
