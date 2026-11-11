using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    public static class UiEdgeReview
    {
        public static void Audit(string output)
        {
            var report = new StringBuilder("asset,width,height,opaque,partial,weak_alpha,unsupported_weak,small_islands,compression,filter,mip_filter\n");
            foreach (string path in Directory.GetFiles("Assets/Resources/UI", "*.png", SearchOption.AllDirectories))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(File.ReadAllBytes(path));
                Color32[] pixels = texture.GetPixels32();
                int w = texture.width, h = texture.height;
                int opaque = 0, partial = 0, weak = 0, unsupported = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte a = pixels[i].a;
                    if (a == 255) opaque++;
                    else if (a > 0) partial++;
                    if (a == 0 || a >= 32) continue;
                    weak++;
                    bool supported = false;
                    int x = i % w, y = i / w;
                    for (int dy = -2; dy <= 2 && !supported; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h && pixels[(y + dy) * w + x + dx].a >= 64) { supported = true; break; }
                    if (!supported) unsupported++;
                }
                int islands = SmallIslands(pixels, w, h);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                report.AppendLine($"{path.Replace('\\', '/')},{w},{h},{opaque},{partial},{weak},{unsupported},{islands},{importer.textureCompression},{importer.filterMode},{importer.mipmapFilter}");
                UnityEngine.Object.DestroyImmediate(texture);
            }
            File.WriteAllText(Path.Combine(output, "edge-audit.csv"), report.ToString());
            VerifyImportedAlpha(output);
        }

        private static void VerifyImportedAlpha(string output)
        {
            var report = new StringBuilder();
            int checkedAssets = 0;
            long alphaSamples = 0;
            foreach (string file in Directory.GetFiles("Assets/Resources/UI", "*.png", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (!UiArtImporter.FineSilhouette(path)) continue;
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                // Keep the audit usable for the compressed baseline as well.
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) continue;
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.LoadImage(File.ReadAllBytes(path));
                Color32[] expected = source.GetPixels32();
                Color32[] original = (Color32[])expected.Clone();
                int cleaned = UiArtImporter.HasKeyResidue(path) ? UiArtImporter.CleanKeyResidue(expected, source.width, source.height) : 0;
                for (int i = 0; i < expected.Length; i++)
                    if (original[i].a >= 32 && !original[i].Equals(expected[i]))
                        throw new InvalidOperationException($"Edge cleanup changes visible artwork: {path}");
                if (imported.width != source.width || imported.height != source.height)
                    throw new InvalidOperationException($"Fine silhouette was resized during import: {path}");
                var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(imported, target);
                RenderTexture.active = target;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); copy.Apply();
                Color32[] actual = copy.GetPixels32();
                int maxError = 0;
                for (int i = 0; i < actual.Length; i++) maxError = Mathf.Max(maxError, Mathf.Abs(actual[i].a - expected[i].a));
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(copy);
                UnityEngine.Object.DestroyImmediate(source);
                if (maxError > 1) throw new InvalidOperationException($"Imported alpha error {maxError}/255: {path}");
                checkedAssets++; alphaSamples += actual.Length;
                report.AppendLine($"PASS {path}: native dimensions, max alpha error {maxError}/255, cleaned {cleaned} faint isolated pixels; visible silhouette unchanged.");
            }
            report.AppendLine($"TOTAL: {checkedAssets} lossless silhouettes, {alphaSamples} alpha samples checked.");
            File.WriteAllText(Path.Combine(output, "imported-alpha-check.txt"), report.ToString());
        }

        private static int SmallIslands(Color32[] pixels, int width, int height)
        {
            var seen = new bool[pixels.Length];
            var queue = new Queue<int>();
            int small = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (seen[i] || pixels[i].a < 32) continue;
                seen[i] = true; queue.Enqueue(i);
                int count = 0;
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue(); count++;
                    int x = at % width, y = at / width;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int next = ny * width + nx;
                            if (seen[next] || pixels[next].a < 32) continue;
                            seen[next] = true; queue.Enqueue(next);
                        }
                }
                if (count <= 6) small++;
            }
            return small;
        }
    }
}
