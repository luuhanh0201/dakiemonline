#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaKiem.Client.Accounts;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Content
{
    /// <summary>Opt-in native integration check against a disposable local HTTP fixture.</summary>
    public sealed class ContentUpdateSmoke : MonoBehaviour
    {
        public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-content-ui-smoke") >= 0;
        [Serializable] private sealed class Metadata
        {
            public string imageKey, imageResource, firstImageHash, secondImageHash, slowHash;
            public int expectedPetals;
        }
        private readonly List<string> checks = new List<string>();
        private string output, fixture;
        private Uri origin;
        private ContentManifest baseline;
        private Dictionary<string, string> originals;
        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            return args[Array.IndexOf(args, name) + 1];
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Enabled) new GameObject(nameof(ContentUpdateSmoke)).AddComponent<ContentUpdateSmoke>();
        }
        private IEnumerator Start()
        {
            output = Argument("-content-ui-smoke"); fixture = Argument("-content-fixture");
            origin = new Uri(Argument("-content-origin"));
            Directory.CreateDirectory(output);
            var stack = new Stack<IEnumerator>(); stack.Push(Check());
            string failure = null;
            while (stack.Count > 0)
            {
                object next = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception error) { failure = error.ToString(); break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            checks.Add(failure == null ? "RESULT PASS" : "RESULT FAIL: " + failure);
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            Application.Quit(failure == null ? 0 : 1);
        }
        private void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add("PASS " + description);
        }
        private ContentCache Cache(string name) => new ContentCache(Path.Combine(output, name), baseline);
        private ContentUpdateSession Session(ContentCache cache) => new ContentUpdateSession(cache, originals, origin);
        private void Publish(string name) => File.Copy(Path.Combine(fixture, name), Path.Combine(fixture, "content/windows/current.json"), true);
        private void SeedPart(ContentCache cache, string hash)
        {
            ContentFile file = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(Path.Combine(fixture, "good.json"))).files.First(value => value.sha256 == hash);
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixture, "content/blobs", hash + ".bin"));
            File.WriteAllBytes(cache.PartPath(file), bytes.Take(bytes.Length / 3).ToArray());
        }
        private IEnumerator Check()
        {
            baseline = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "content/baseline.json")));
            originals = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames.Concat(new[] { GameConfig.AccountsFile, "login-scene.json" })) yield return ConfigFiles.LoadOriginal(name, originals);
            Metadata metadata = JsonUtility.FromJson<Metadata>(File.ReadAllText(Path.Combine(fixture, "metadata.json")));
            Publish("good.json");
            ContentCache cache = Cache("main");
            SeedPart(cache, metadata.firstImageHash);
            ContentUpdateSession session = Session(cache);
            yield return ContentUpdater.ShowSession(session);
            Require(session.Success && session.Current.version == 1 && session.DownloadedFiles == 3 && session.ResumedBytes > 0, "actual update UI downloads only three changed files and resumes a partial image");
            Sprite native = Resources.Load<Sprite>(metadata.imageResource);
            Sprite patched = ContentAssets.Load<Sprite>(metadata.imageResource);
            bool sameMesh = native.vertices.Length == patched.vertices.Length && native.vertices.Zip(patched.vertices, (a, b) => Vector2.Distance(a, b) < 0.002f / native.pixelsPerUnit).All(value => value);
            Require(native != patched && native.rect == patched.rect && native.pivot == patched.pivot && native.pixelsPerUnit == patched.pixelsPerUnit && sameMesh, "patched sprite preserves rect, pivot, scale and tight mesh within 0.002 pixels");
            Sprite nativeButton = Resources.Load<Sprite>("UI/btn_gold");
            Sprite patchedButton = ContentAssets.Load<Sprite>("UI/btn_gold");
            Require(nativeButton != patchedButton && nativeButton.border == patchedButton.border, "patched UI button preserves nine-slice borders");
            Color32[] before = ReadPixels(native.texture), after = ReadPixels(patched.texture);
            double delta = 0;
            for (int i = 0; i < before.Length; i++) delta += Math.Abs(before[i].r - after[i].r) + Math.Abs(before[i].g - after[i].g) + Math.Abs(before[i].b - after[i].b) + Math.Abs(before[i].a - after[i].a);
            delta /= before.Length * 4;
            Require(delta < 2, "patch image retains original colors (mean byte difference " + delta.ToString("0.000") + ")");
            var config = new Dictionary<string, string>();
            yield return ConfigFiles.Load("login-scene.json", config);
            Require(LoginSceneConfig.Parse(config["login-scene.json"]).petalCount == metadata.expectedPetals, "game config reader uses downloaded data");

            session = Session(cache); yield return session.Run(_ => { });
            Require(session.Success && session.Current.version == 1 && session.DownloadedFiles == 0, "relaunch uses verified cache without downloading payloads again");

            ContentCache ignoredRange = Cache("ignored-range"); SeedPart(ignoredRange, metadata.firstImageHash);
            File.WriteAllText(Path.Combine(fixture, "ignore-range.flag"), "test");
            session = Session(ignoredRange); yield return session.Run(_ => { });
            File.Delete(Path.Combine(fixture, "ignore-range.flag"));
            Require(session.Success && session.Current.version == 1 && session.ResumedBytes == 0, "server ignoring Range triggers a clean full-file retry");

            Publish("second.json");
            string secondPath = Path.Combine(fixture, "content/blobs", metadata.secondImageHash + ".bin");
            byte[] correct = File.ReadAllBytes(Path.Combine(fixture, "correct-second-image.bin"));
            byte[] corrupt = (byte[])correct.Clone(); corrupt[corrupt.Length / 2] ^= 1; File.WriteAllBytes(secondPath, corrupt);
            session = Session(cache); yield return session.Run(_ => { });
            Require(!session.Success && session.Error != null && session.Current.version == 1 && Session(cache).Current.version == 1, "SHA mismatch keeps the previous active version across relaunch");
            File.WriteAllBytes(secondPath, correct);
            yield return session.Run(_ => { });
            Require(session.Success && session.Current.version == 2, "retry accepts repaired payload and commits atomically");

            Publish("slow.json");
            session = Session(cache);
            yield return session.Run(_ => { if (session.DoneBytes > 131072) session.Cancel(); });
            Require(session.Cancelled && session.Current.version == 2, "interrupted transfer preserves the active snapshot");
            yield return session.Run(_ => { });
            Require(session.Success && session.Current.version == 3 && session.ResumedBytes > 0, "interrupted real HTTP transfer resumes and verifies successfully");

            File.Delete(Path.Combine(fixture, "content/windows/current.json"));
            session = Session(cache); yield return session.Run(_ => { });
            Require(session.Unavailable && session.Current.version == 3, "offline manifest retains the last verified data");
            Publish("invalid.json");
            session = Session(cache); yield return session.Run(_ => { });
            Require(session.Error != null && session.Current.version == 3, "invalid manifest paths never activate");
            File.WriteAllText(Path.Combine(output, "main/current.json"), "corrupt");
            session = Session(cache);
            Require(session.Current.version == 2, "corrupt current manifest falls back to previous verified snapshot");
            Publish("good.json");
            ContentAssets.Configure(null, null);
        }
        private static Color32[] ReadPixels(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, target); RenderTexture.active = target;
            var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); read.Apply();
            Color32[] result = read.GetPixels32(); Destroy(read);
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            return result;
        }
    }
}
#endif
