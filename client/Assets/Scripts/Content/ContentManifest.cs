using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DaKiem.Client.Content
{
    [Serializable]
    public sealed class ContentManifest
    {
        public int format = 1;
        public string contract = "dakiem-resources-v1";
        public string platform = "";
        public int version;
        public int buildCode;
        public int minClientCode = 5;
        public int maxClientCode;
        public string notes = "";
        public string rulesHash = "";
        public ContentFile[] files = Array.Empty<ContentFile>();

        public void Validate(string expectedPlatform, int clientCode)
        {
            if (format != 1 || contract != "dakiem-resources-v1" || platform != expectedPlatform || version < 0)
                throw new InvalidDataException("Unsupported content manifest.");
            if (minClientCode <= 0 || clientCode < minClientCode || (maxClientCode > 0 && clientCode > maxClientCode))
                throw new InvalidDataException("This content requires a different application version.");
            if (!ContentHash.IsHash(rulesHash) || files == null || files.Length > 5000)
                throw new InvalidDataException("Invalid content manifest.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ContentFile file in files)
            {
                if (file == null) throw new InvalidDataException("Missing content entry.");
                file.Validate();
                if (!keys.Add(file.key)) throw new InvalidDataException("Duplicate content key.");
                total += file.size;
            }
            if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Content is too large.");
        }

        public Dictionary<string, ContentFile> Index()
        {
            var index = new Dictionary<string, ContentFile>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentFile file in files) index.Add(file.key, file);
            return index;
        }
    }

    [Serializable]
    public sealed class ContentFile
    {
        public string key = "";
        public string kind = "";
        public string resource = "";
        public string sha256 = "";
        public long size;
        public int width;
        public int height;
        public int filter = 1;
        public int wrap = 1;
        public bool mipmaps;
        public float mipBias;
        public string primary = "";
        public ContentSprite[] sprites = Array.Empty<ContentSprite>();

        public void Validate()
        {
            if (!SafeKey(key) || !ContentHash.IsHash(sha256) || size <= 0 || size > 64L * 1024 * 1024)
                throw new InvalidDataException("Invalid content entry.");
            if (kind == "config")
            {
                if (!key.StartsWith("config/", StringComparison.Ordinal) || key.Substring(7).Contains("/") || !key.EndsWith(".json", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid configuration path.");
            }
            else if (kind == "image" || kind == "text")
            {
                if (!SafeKey(resource) || key != "resources/" + resource) throw new InvalidDataException("Invalid resource path.");
            }
            else throw new InvalidDataException("Unsupported content kind.");
            if (kind != "image") return;
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || filter < 0 || filter > 2 || wrap < 0 || wrap > 3
                || !Finite(mipBias) || sprites == null || sprites.Length == 0 || sprites.Length > 1000)
                throw new InvalidDataException("Invalid image metadata.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentSprite sprite in sprites)
            {
                if (sprite == null || string.IsNullOrEmpty(sprite.name) || !names.Add(sprite.name)) throw new InvalidDataException("Invalid sprite name.");
                sprite.Validate(width, height);
            }
            if (!names.Contains(primary)) throw new InvalidDataException("Primary sprite is missing.");
        }

        public string Fingerprint()
        {
            var text = new StringBuilder();
            Add(text, key); Add(text, kind); Add(text, resource); Add(text, sha256); Add(text, size);
            Add(text, width); Add(text, height); Add(text, filter); Add(text, wrap); Add(text, mipmaps); Add(text, mipBias); Add(text, primary);
            Add(text, sprites?.Length ?? 0);
            if (sprites != null) foreach (ContentSprite sprite in sprites)
            {
                Add(text, sprite.name); Add(text, sprite.x); Add(text, sprite.y); Add(text, sprite.width); Add(text, sprite.height);
                Add(text, sprite.pivotX); Add(text, sprite.pivotY); Add(text, sprite.ppu);
                foreach (float number in sprite.border) Add(text, number);
                Add(text, sprite.vertices.Length); foreach (float number in sprite.vertices) Add(text, number);
                Add(text, sprite.triangles.Length); foreach (int number in sprite.triangles) Add(text, number);
            }
            return ContentHash.Text(text.ToString());
        }

        private static void Add(StringBuilder text, object value)
        {
            string part = value is float number ? number.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            text.Append(part.Length).Append(':').Append(part).Append(';');
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 1000000f;
        private static bool SafeKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 512 || key.StartsWith("/", StringComparison.Ordinal) || key.Contains("\\") || key.Contains(":") || key.Contains("\0")) return false;
            foreach (string part in key.Split('/')) if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }
    }

    [Serializable]
    public sealed class ContentSprite
    {
        public string name = "";
        public float x, y, width, height, pivotX, pivotY;
        public float ppu = 100f;
        public float[] border = new float[4];
        public float[] vertices = Array.Empty<float>();
        public int[] triangles = Array.Empty<int>();

        public void Validate(int textureWidth, int textureHeight)
        {
            foreach (float value in new[] { x, y, width, height, pivotX, pivotY, ppu })
                if (!ContentFile.Finite(value)) throw new InvalidDataException("Invalid sprite number.");
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > textureWidth + 0.01f || y + height > textureHeight + 0.01f || ppu <= 0)
                throw new InvalidDataException("Sprite is outside the texture.");
            if (border == null || border.Length != 4 || vertices == null || triangles == null || vertices.Length % 2 != 0
                || vertices.Length > 131070 || triangles.Length % 3 != 0 || triangles.Length > 196605)
                throw new InvalidDataException("Invalid sprite geometry.");
            foreach (float value in border) if (!ContentFile.Finite(value) || value < 0) throw new InvalidDataException("Invalid sprite border.");
            foreach (float value in vertices) if (!ContentFile.Finite(value)) throw new InvalidDataException("Invalid sprite vertex.");
            for (int i = 0; i < vertices.Length; i += 2)
            {
                float pixelX = vertices[i] * ppu + pivotX * width;
                float pixelY = vertices[i + 1] * ppu + pivotY * height;
                if (pixelX < -0.02f || pixelY < -0.02f || pixelX > width + 0.02f || pixelY > height + 0.02f)
                    throw new InvalidDataException("Sprite geometry exceeds its rectangle.");
            }
            foreach (int index in triangles) if (index < 0 || index >= vertices.Length / 2) throw new InvalidDataException("Invalid sprite triangle.");
        }
    }

    public static class ContentHash
    {
        public static bool IsHash(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$");
        public static string Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
        public static string Bytes(byte[] value) { using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(value)); }
        public static string File(string path) { using (var stream = System.IO.File.OpenRead(path)) using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(stream)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
