using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaKiem.Client.Accounts;
using Game.Net;
using Game.Rules;
using UnityEngine;
using UnityEngine.Networking;

namespace DaKiem.Client.Content
{
    /// <summary>A transaction: download/resume, verify, validate configuration, then atomically activate.</summary>
    public sealed class ContentUpdateSession
    {
        public ContentCache Cache { get; }
        public ContentManifest Current { get; private set; }
        public ContentManifest Candidate { get; private set; }
        public string Error { get; private set; }
        public bool Unavailable { get; private set; }
        public bool Cancelled { get; private set; }
        public bool Success { get; private set; }
        public int DownloadedFiles { get; private set; }
        public long ResumedBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public long DoneBytes { get; private set; }
        public float Progress { get; private set; }
        private readonly Uri origin;
        private readonly Dictionary<string, string> originals;
        private UnityWebRequest activeRequest;
        private bool cancelRequested;

        [Serializable] private sealed class ServerStatus { public string rulesHash; }

        public ContentUpdateSession(ContentCache cache, Dictionary<string, string> originalConfigs, Uri source)
        {
            if (source.Scheme != "https" && !(source.Scheme == "http" && source.IsLoopback)) throw new ArgumentException("HTTPS content origin required.");
            Cache = cache;
            originals = originalConfigs;
            origin = new Uri(source.GetLeftPart(UriPartial.Authority) + "/");
            LoadSaved(false);
        }

        private void LoadSaved(bool repair)
        {
            Current = Cache.Baseline;
            int snapshotNumber = -1;
            foreach (string saved in Cache.ReadSnapshots())
            {
                snapshotNumber++;
                try
                {
                    ContentManifest manifest = Parse(saved);
                    if (!Cache.Complete(manifest)) continue;
                    ValidateConfigs(manifest);
                    Current = manifest;
                    if (repair && snapshotNumber > 0) Cache.RepairCurrent(saved);
                    break;
                }
                catch (Exception error) { Debug.LogWarning("Ignoring invalid cached content: " + error.Message); }
            }
        }

        public void Cancel()
        {
            cancelRequested = true;
            activeRequest?.Abort();
        }

        public IEnumerator Run(Action<string> report)
        {
            Error = null; Unavailable = false; Success = false; Progress = 0;
            FileStream lease = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (lease == null && Time.realtimeSinceStartup < deadline)
            {
                try { lease = Cache.AcquireLease(); }
                catch (IOException) { }
                catch (Exception error) { Fail(error); yield break; }
                if (lease == null) { report("Đang chờ cửa sổ game khác cập nhật dữ liệu..."); yield return new WaitForSecondsRealtime(0.25f); }
            }
            if (lease == null) { Error = "Dữ liệu đang được cập nhật ở cửa sổ khác. Hãy thử lại."; yield break; }
            using (lease)
            {
                LoadSaved(true);
                yield return RunLocked(report);
            }
        }

        private IEnumerator RunLocked(Action<string> report)
        {
            Error = null; Candidate = null; Unavailable = false; Cancelled = false; Success = false; cancelRequested = false;
            DownloadedFiles = 0; ResumedBytes = 0; TotalBytes = 0; DoneBytes = 0; Progress = 0f;
            string json;
            report("Đang kiểm tra dữ liệu mới...");
            using (var request = UnityWebRequest.Get(new Uri(origin, "content/" + Cache.Baseline.platform + "/current.json?check=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).AbsoluteUri))
            {
                request.timeout = 10;
                request.SetRequestHeader("Cache-Control", "no-cache");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Error = "Chưa kiểm tra được dữ liệu mới. Đang dùng bản dữ liệu trên máy.";
                    Unavailable = true;
                    yield break;
                }
                json = request.downloadHandler.text;
            }
            try
            {
                Candidate = Parse(json);
                if (Candidate.version < Current.version) throw new InvalidDataException("Content version went backwards.");
                if (Candidate.version == Current.version)
                {
                    // Reusing a release number with different contents would make cache behavior ambiguous.
                    if (SnapshotFingerprint(Candidate) != SnapshotFingerprint(Current)) throw new InvalidDataException("Content version was reused.");
                    Progress = 1; Success = true;
                    yield break;
                }
                TotalBytes = Candidate.files.Where(file => Cache.NeedsPayload(file) && !Cache.HasPayload(file)).Sum(file => file.size);
            }
            catch (Exception error) { Fail(error); yield break; }

            foreach (ContentFile file in Candidate.files)
            {
                if (!Cache.NeedsPayload(file) || Cache.HasPayload(file)) continue;
                yield return Download(file, report);
                if (Error != null || Cancelled) yield break;
            }
            report("Đang kiểm tra và áp dụng dữ liệu...");
            foreach (ContentFile file in Candidate.files.Where(Cache.NeedsPayload))
            {
                try
                {
                    if (file.kind == "image")
                    {
                        Texture2D checkedImage = ContentAssets.DecodeTexture(file, Cache.BlobPath(file));
                        foreach (Sprite sprite in ContentAssets.CreateSprites(file, checkedImage)) UnityEngine.Object.Destroy(sprite);
                        UnityEngine.Object.Destroy(checkedImage);
                    }
                    else ContentAssets.ReadText(Cache.BlobPath(file));
                }
                catch (Exception error) { Fail(error); yield break; }
                yield return null;
            }
            try { ValidateConfigs(Candidate); }
            catch (Exception error) { Fail(error); yield break; }

            if (Candidate.rulesHash != Current.rulesHash)
            {
                using (var request = UnityWebRequest.Get(new Uri(origin, "status").AbsoluteUri))
                {
                    request.timeout = 10;
                    yield return request.SendWebRequest();
                    string serverHash = null;
                    try { if (request.result == UnityWebRequest.Result.Success) serverHash = JsonUtility.FromJson<ServerStatus>(request.downloadHandler.text)?.rulesHash; }
                    catch (ArgumentException) { }
                    if (serverHash != Candidate.rulesHash)
                    {
                        Error = "Dữ liệu mới chưa khớp với máy chủ. Hãy thử cập nhật lại sau.";
                        yield break;
                    }
                }
            }
            try
            {
                Cache.Commit(Candidate, json);
                Current = Candidate;
                Progress = 1f;
                Success = true;
                report("Dữ liệu đã sẵn sàng.");
            }
            catch (Exception error) { Fail(error); }
        }

        private IEnumerator Download(ContentFile file, Action<string> report)
        {
            long completedBefore = DoneBytes;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                long offset = 0;
                UnityWebRequest request = null;
                try
                {
                    string part = Cache.PartPath(file);
                    if (File.Exists(part)) offset = new FileInfo(part).Length;
                    if (offset >= file.size) { Cache.ResetPart(file); offset = 0; }
                    request = UnityWebRequest.Get(new Uri(origin, "content/blobs/" + file.sha256 + ".bin").AbsoluteUri);
                    request.downloadHandler = new DownloadHandlerFile(part, offset > 0) { removeFileOnAbort = false };
                    request.timeout = 60;
                    if (offset > 0) request.SetRequestHeader("Range", "bytes=" + offset + "-");
                }
                catch (Exception error) { request?.Dispose(); Fail(error); yield break; }
                long responseCode;
                UnityWebRequest.Result result;
                using (request)
                {
                    activeRequest = request;
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        DoneBytes = completedBefore + Math.Min(file.size, offset + (long)request.downloadedBytes);
                        Progress = TotalBytes == 0 ? 1f : Mathf.Clamp01((float)DoneBytes / TotalBytes);
                        report($"Đang tải dữ liệu: {DoneBytes / 1048576f:0.0} / {TotalBytes / 1048576f:0.0} MB");
                        yield return null;
                    }
                    activeRequest = null;
                    responseCode = request.responseCode;
                    result = request.result;
                }
                if (cancelRequested) { Cancelled = true; yield break; }
                if (result != UnityWebRequest.Result.Success)
                {
                    Error = "Tải dữ liệu bị gián đoạn. Phần đã tải được giữ lại để thử tiếp.";
                    yield break;
                }
                if (offset > 0 && responseCode != 206)
                {
                    Cache.ResetPart(file);
                    DoneBytes = completedBefore;
                    continue;
                }
                try
                {
                    Cache.AcceptPart(file);
                    ResumedBytes += offset;
                    DownloadedFiles++;
                    DoneBytes = completedBefore + file.size;
                    Progress = TotalBytes == 0 ? 1f : Mathf.Clamp01((float)DoneBytes / TotalBytes);
                    yield break;
                }
                catch (Exception error)
                {
                    Cache.ResetPart(file);
                    Fail(error);
                    yield break;
                }
            }
            Error = "Chưa tải xong dữ liệu. Hãy thử lại.";
        }

        private ContentManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 8 * 1024 * 1024) throw new InvalidDataException("Invalid manifest size.");
            ContentManifest manifest = JsonUtility.FromJson<ContentManifest>(json);
            if (manifest == null) throw new InvalidDataException("Missing manifest.");
            manifest.Validate(Cache.Baseline.platform, Cache.Baseline.buildCode);
            return manifest;
        }

        private void ValidateConfigs(ContentManifest manifest)
        {
            Dictionary<string, ContentFile> index = manifest.Index();
            var files = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames.Concat(new[] { GameConfig.AccountsFile, "login-scene.json" }))
            {
                if (!index.TryGetValue("config/" + name, out ContentFile file)) throw new InvalidDataException("A required config is missing.");
                files[name] = Cache.NeedsPayload(file) ? ContentAssets.ReadText(Cache.BlobPath(file)) : originals[name];
            }
            GameConfig.Parse(files);
            GameConfig.ParseAccounts(files[GameConfig.AccountsFile]);
            LoginSceneConfig.Parse(files["login-scene.json"]);
            var shared = files.Where(pair => pair.Key != "login-scene.json").ToDictionary(pair => pair.Key, pair => pair.Value);
            if (ContentRules.Fingerprint(shared) != manifest.rulesHash) throw new InvalidDataException("Rules fingerprint does not match the payloads.");
        }

        private static string SnapshotFingerprint(ContentManifest manifest) => ContentHash.Text(manifest.rulesHash + "|" + string.Join("|", manifest.files.OrderBy(file => file.key, StringComparer.Ordinal).Select(file => file.Fingerprint())));
        private void Fail(Exception error)
        {
            Debug.LogWarning("Content update rejected: " + error.Message);
            Error = "Dữ liệu tải về chưa hợp lệ hoặc chưa ghi được. Hãy thử lại; bản đang dùng vẫn được giữ nguyên.";
        }
    }
}
