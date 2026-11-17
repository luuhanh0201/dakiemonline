using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Net;
using Game.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Content
{
    /// <summary>Loads verified content overrides; unchanged and non-patchable assets stay in Resources.</summary>
    public static class ContentAssets
    {
        private static ContentCache cache;
        private static Dictionary<string, ContentFile> current;
        private static Dictionary<string, ContentFile> baseline;
        private static readonly Dictionary<string, Sprite[]> sprites = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, TextAsset> texts = new Dictionary<string, TextAsset>(StringComparer.OrdinalIgnoreCase);
        public static int Version { get; private set; }
        public static string LocalRulesHash { get; private set; } = "";

        public static void Configure(ContentCache store, ContentManifest manifest)
        {
            foreach (Sprite sprite in sprites.Values.SelectMany(value => value)) Destroy(sprite);
            foreach (Texture2D texture in textures.Values) Destroy(texture);
            foreach (TextAsset text in texts.Values) Destroy(text);
            sprites.Clear(); textures.Clear(); texts.Clear();
            cache = store;
            baseline = store?.Baseline.Index();
            current = manifest?.Index();
            Version = manifest?.version ?? 0;
        }

        public static void SetLocalRules(IReadOnlyDictionary<string, string> files)
        {
            var shared = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames) shared.Add(name, files[name]);
            shared.Add(GameConfig.AccountsFile, files[GameConfig.AccountsFile]);
            LocalRulesHash = ContentRules.Fingerprint(shared);
        }

        public static T Load<T>(string path) where T : Object
        {
            string key = "resources/" + path;
            if (current != null && current.TryGetValue(key, out ContentFile file))
            {
                if (!cache.NeedsPayload(file)) return Resources.Load<T>(path);
                if (typeof(T) == typeof(Sprite) && file.kind == "image")
                    return LoadSprites(file).First(sprite => sprite.name == file.primary) as T;
                if (typeof(T) == typeof(Texture2D) && file.kind == "image") return LoadTexture(file) as T;
                if (typeof(T) == typeof(TextAsset) && file.kind == "text")
                {
                    if (!texts.TryGetValue(path, out TextAsset asset))
                        texts[path] = asset = new TextAsset(ReadText(cache.BlobPath(file))) { name = Path.GetFileName(path) };
                    return asset as T;
                }
            }
            else if (current != null && baseline.ContainsKey(key) && (typeof(T) == typeof(Sprite) || typeof(T) == typeof(TextAsset) || typeof(T) == typeof(Texture2D))) return null;
            return Resources.Load<T>(path);
        }

        public static T[] LoadAll<T>(string path) where T : Object
        {
            if (current == null || (typeof(T) != typeof(Sprite) && typeof(T) != typeof(TextAsset))) return Resources.LoadAll<T>(path);
            string kind = typeof(T) == typeof(Sprite) ? "image" : "text";
            bool Matches(ContentFile file) => file.kind == kind && (file.resource.Equals(path, StringComparison.OrdinalIgnoreCase) || file.resource.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
            var entries = current.Values.Where(Matches).OrderBy(file => file.resource, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0 && !baseline.Values.Any(Matches)) return Resources.LoadAll<T>(path);
            var result = new List<T>();
            foreach (ContentFile file in entries)
            {
                if (!cache.NeedsPayload(file)) result.AddRange(Resources.LoadAll<T>(file.resource));
                else if (typeof(T) == typeof(Sprite)) result.AddRange(LoadSprites(file).Cast<T>());
                else result.Add(Load<T>(file.resource));
            }
            return result.ToArray();
        }

        public static bool TryReadConfig(string name, out string text)
        {
            text = null;
            if (current == null || !current.TryGetValue("config/" + name, out ContentFile file) || !cache.NeedsPayload(file)) return false;
            text = ReadText(cache.BlobPath(file));
            return true;
        }

        public static string ReadText(string path) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');

        internal static Texture2D DecodeTexture(ContentFile file, string path)
        {
            if (file.width > SystemInfo.maxTextureSize || file.height > SystemInfo.maxTextureSize) throw new InvalidDataException("Texture exceeds this device's capabilities.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, file.mipmaps);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false) || texture.width != file.width || texture.height != file.height)
            {
                Destroy(texture);
                throw new InvalidDataException("Invalid image payload.");
            }
            texture.filterMode = (FilterMode)file.filter;
            texture.wrapMode = (TextureWrapMode)file.wrap;
            texture.mipMapBias = file.mipBias;
            if (file.mipmaps) texture.Apply(true, false);
            return texture;
        }

        private static Texture2D LoadTexture(ContentFile file)
        {
            string fingerprint = file.Fingerprint();
            if (!textures.TryGetValue(fingerprint, out Texture2D texture)) textures[fingerprint] = texture = DecodeTexture(file, cache.BlobPath(file));
            return texture;
        }
        private static Sprite[] LoadSprites(ContentFile file)
        {
            if (sprites.TryGetValue(file.resource, out Sprite[] found)) return found;
            Texture2D texture = LoadTexture(file);
            return sprites[file.resource] = CreateSprites(file, texture);
        }
        internal static Sprite[] CreateSprites(ContentFile file, Texture2D texture)
        {
            var result = new List<Sprite>();
            foreach (ContentSprite spec in file.sprites)
            {
                var sprite = Sprite.Create(texture, new Rect(spec.x, spec.y, spec.width, spec.height), new Vector2(spec.pivotX, spec.pivotY), spec.ppu, 0, SpriteMeshType.FullRect,
                    new Vector4(spec.border[0], spec.border[1], spec.border[2], spec.border[3]));
                sprite.name = spec.name;
                if (spec.vertices.Length > 0)
                {
                    var vertices = new Vector2[spec.vertices.Length / 2];
                    // Sprite.vertices is in local units; OverrideGeometry takes rect-space pixels
                    // and applies pivot/PPU itself. Clamp only floating point error at the edges.
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector2(
                        Mathf.Clamp(spec.vertices[i * 2] * spec.ppu + spec.pivotX * spec.width, 0, spec.width),
                        Mathf.Clamp(spec.vertices[i * 2 + 1] * spec.ppu + spec.pivotY * spec.height, 0, spec.height));
                    sprite.OverrideGeometry(vertices, spec.triangles.Select(index => checked((ushort)index)).ToArray());
                }
                result.Add(sprite);
            }
            return result.ToArray();
        }
        private static void Destroy(Object item)
        {
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }
    }
}
