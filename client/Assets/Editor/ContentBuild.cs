using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DaKiem.Client.Accounts;
using DaKiem.Client.Content;
using Game.Net;
using Game.Rules;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>Exports imported sprite pixels/metadata and JSON, with hashes for independent in-game releases.</summary>
    public static class ContentBuild
    {
        private static readonly string[] ResourceFolders = { "Assets/Resources/Character", "Assets/Resources/UI", "Assets/Resources/World" };
        [Serializable] private sealed class SourceCache { public SourceRecord[] records = Array.Empty<SourceRecord>(); }
        [Serializable] private sealed class SourceRecord { public string path; public string dependency; public ContentFile file; }

        public static void Run()
        {
            Export(false);
            EditorApplication.Exit(0);
        }
        public static void ExportBaseline() => Export(true);

        private static void Export(bool writeBaseline)
        {
            string platform = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "android" : "windows";
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("Content export currently targets Android and Windows.");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(repo, "client/Builds/ContentSources", platform);
            Directory.CreateDirectory(Path.Combine(output, "blobs"));
            string cachePath = Path.Combine(output, "source-cache.json");
            var previous = new Dictionary<string, SourceRecord>(StringComparer.Ordinal);
            if (File.Exists(cachePath))
            {
                try { foreach (var record in JsonUtility.FromJson<SourceCache>(File.ReadAllText(cachePath)).records) previous[record.path] = record; }
                catch (Exception) { previous.Clear(); }
            }
            var release = JsonUtility.FromJson<AndroidReleaseInfo>(File.ReadAllText(Path.Combine(repo, "config/android-release.json")));
            var manifest = new ContentManifest { platform = platform, version = 0, buildCode = release.versionCode, minClientCode = 5 };
            var entries = new List<ContentFile>();
            var records = new List<SourceRecord>();
            string[] assetPaths = AssetDatabase.GetAllAssetPaths().Where(path => ResourceFolders.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal))
                && new[] { ".png", ".jpg", ".jpeg", ".json", ".txt", ".bytes" }.Contains(Path.GetExtension(path).ToLowerInvariant())).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            int completed = 0;
            foreach (string path in assetPaths)
            {
                string dependency = "sprite-png-v1:" + AssetDatabase.GetAssetDependencyHash(path);
                ContentFile file = null;
                if (previous.TryGetValue(path, out SourceRecord known) && known.dependency == dependency && File.Exists(Path.Combine(output, "blobs", known.file.sha256 + ".bin"))) file = known.file;
                else
                {
                    Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray();
                    string resource = path.Substring("Assets/Resources/".Length);
                    resource = resource.Substring(0, resource.Length - Path.GetExtension(resource).Length);
                    if (sprites.Length > 0) file = ExportImage(path, resource, sprites, output);
                    else
                    {
                        TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                        if (text != null)
                        {
                            byte[] bytes = new UTF8Encoding(false).GetBytes(text.text);
                            file = new ContentFile { key = "resources/" + resource, resource = resource, kind = "text", sha256 = WriteBlob(output, bytes), size = bytes.Length };
                        }
                    }
                }
                if (file != null) { entries.Add(file); records.Add(new SourceRecord { path = path, dependency = dependency, file = file }); }
                if (++completed % 50 == 0) Debug.Log($"CONTENT_EXPORT {platform}: {completed}/{assetPaths.Length}");
            }
            var configs = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames.Concat(new[] { GameConfig.AccountsFile, "login-scene.json" }))
            {
                string text = File.ReadAllText(Path.Combine(repo, "config", name));
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                entries.Add(new ContentFile { key = "config/" + name, kind = "config", sha256 = WriteBlob(output, bytes), size = bytes.Length });
                configs.Add(name, text);
            }
            GameConfig.Parse(configs);
            GameConfig.ParseAccounts(configs[GameConfig.AccountsFile]);
            LoginSceneConfig.Parse(configs["login-scene.json"]);
            manifest.rulesHash = ContentRules.Fingerprint(configs.Where(pair => pair.Key != "login-scene.json").ToDictionary(pair => pair.Key, pair => pair.Value));
            manifest.files = entries.OrderBy(file => file.key, StringComparer.Ordinal).ToArray();
            manifest.Validate(platform, release.versionCode);
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(manifest), new UTF8Encoding(false));
            File.WriteAllText(cachePath, JsonUtility.ToJson(new SourceCache { records = records.ToArray() }), new UTF8Encoding(false));
            if (writeBaseline)
            {
                string baselineFolder = Path.Combine(Application.streamingAssetsPath, "content");
                Directory.CreateDirectory(baselineFolder);
                File.WriteAllText(Path.Combine(baselineFolder, "baseline.json"), JsonUtility.ToJson(manifest), new UTF8Encoding(false));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log($"CONTENT_EXPORT_DONE {platform}: {entries.Count} files; baseline={writeBaseline}; {output}");
        }

        private static ContentFile ExportImage(string path, string resource, Sprite[] sprites, string output)
        {
            Texture2D source = sprites[0].texture;
            Texture2D copy = Readable(source);
            byte[] bytes = copy.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(copy);
            Sprite primary = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var file = new ContentFile
            {
                key = "resources/" + resource, resource = resource, kind = "image", sha256 = WriteBlob(output, bytes), size = bytes.Length,
                width = source.width, height = source.height, filter = (int)source.filterMode, wrap = (int)source.wrapMode,
                mipmaps = source.mipmapCount > 1, mipBias = source.mipMapBias, primary = primary != null ? primary.name : sprites[0].name,
                sprites = sprites.Select(sprite => new ContentSprite
                {
                    name = sprite.name, x = sprite.rect.x, y = sprite.rect.y, width = sprite.rect.width, height = sprite.rect.height,
                    pivotX = sprite.pivot.x / sprite.rect.width, pivotY = sprite.pivot.y / sprite.rect.height, ppu = sprite.pixelsPerUnit,
                    border = new[] { sprite.border.x, sprite.border.y, sprite.border.z, sprite.border.w },
                    vertices = sprite.vertices.SelectMany(vertex => new[] { vertex.x, vertex.y }).ToArray(),
                    triangles = sprite.triangles.Select(index => (int)index).ToArray(),
                }).ToArray(),
            };
            file.Validate();
            return file;
        }

        private static Texture2D Readable(Texture2D source)
        {
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            if (source.isReadable) { copy.SetPixels32(source.GetPixels32()); copy.Apply(); return copy; }
            RenderTexture previous = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = oldSrgb; RenderTexture.ReleaseTemporary(target); }
        }
        private static string WriteBlob(string output, byte[] bytes)
        {
            string hash = ContentHash.Bytes(bytes);
            string path = Path.Combine(output, "blobs", hash + ".bin");
            if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length || ContentHash.File(path) != hash) File.WriteAllBytes(path, bytes);
            return hash;
        }
    }
}
