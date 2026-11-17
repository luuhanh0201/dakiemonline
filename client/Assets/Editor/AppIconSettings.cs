using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>Uses the supplied artwork for native app icons and web shortcuts without repainting it.</summary>
    public static class AppIconSettings
    {
        private const string Folder = "Assets/Branding";
        private const string Source = Folder + "/AppIcon.png";
        private const string WebFolder = "Assets/WebGLTemplates/DaKiem/TemplateData";

        public static void Apply()
        {
            Texture2D master = Load(Source);
            if (master.width != master.height) throw new BuildFailedException("The app icon must be square.");
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(WebFolder);
            Texture2D foreground = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[1024 * 1024];
            // Android masks a 72dp window from a 108dp layer. Keep the full BETA badge inside that window.
            const int artSize = 512;
            const int inset = (1024 - artSize) / 2;
            for (int y = 0; y < artSize; y++)
                for (int x = 0; x < artSize; x++)
                    pixels[(y + inset) * 1024 + x + inset] = master.GetPixelBilinear((x + 0.5f) / artSize, (y + 0.5f) / artSize);
            foreground.SetPixels(pixels);
            foreground.Apply();
            File.WriteAllBytes(Folder + "/AdaptiveForeground.png", foreground.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(foreground);
            var background = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            background.SetPixels(Enumerable.Repeat((Color)new Color32(0x14, 0x1B, 0x3A, 0xFF), 1024 * 1024).ToArray());
            background.Apply();
            File.WriteAllBytes(Folder + "/AdaptiveBackground.png", background.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(background);
            foreach (var item in new[] { (32, "favicon-32.png"), (180, "apple-touch-icon.png"), (192, "icon-192.png"), (512, "icon-512.png") })
                SaveScaled(master, item.Item1, WebFolder + "/" + item.Item2);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Texture2D adaptiveBack = Load(Folder + "/AdaptiveBackground.png");
            Texture2D adaptiveFront = Load(Folder + "/AdaptiveForeground.png");
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { master }, IconKind.Any);
            int[] standaloneSizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone, IconKind.Any);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, Enumerable.Repeat(master, standaloneSizes.Length).ToArray(), IconKind.Any);
            SetAndroid(AndroidPlatformIconKind.Adaptive, adaptiveBack, adaptiveFront);
            AssetDatabase.SaveAssets();
            WriteAudit(master, adaptiveBack, adaptiveFront);
            Debug.Log("App icon configured: supplied BETA artwork; default, Windows, Android adaptive and web icons.");
        }

        private static void SetAndroid(PlatformIconKind kind, params Texture2D[] textures)
        {
            PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (PlatformIcon slot in slots) slot.SetTextures(textures);
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
        }

        private static Texture2D Load(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing app icon: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void SaveScaled(Texture2D master, int size, string path)
        {
            var scaled = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    scaled.SetPixel(x, y, master.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size));
            scaled.Apply();
            File.WriteAllBytes(path, scaled.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(scaled);
        }

        private static void WriteAudit(Texture2D master, Texture2D background, Texture2D foreground)
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/icon-update"));
            Directory.CreateDirectory(output);
            Texture2D[] desktop = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Standalone, IconKind.Any);
            if (desktop.Length == 0 || desktop.Any(icon => icon != master)) throw new BuildFailedException("Windows icon slots are incomplete.");
            PlatformIcon[] adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
            if (adaptive.Length == 0 || adaptive.Any(icon => icon.GetTexture(0) != background || icon.GetTexture(1) != foreground)) throw new BuildFailedException("Android adaptive icon layers are incomplete.");
            if (foreground.GetPixel(0, 0).a != 0f || foreground.GetPixel(512, 512).a < 0.99f) throw new BuildFailedException("Android icon padding is invalid.");
            var preview = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = (x + 0.5f) / 256f;
                    float v = (y + 0.5f) / 256f;
                    Color value = Color.clear;
                    if (Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) <= 0.5f)
                    {
                        float layerU = 1f / 6f + u * 2f / 3f;
                        float layerV = 1f / 6f + v * 2f / 3f;
                        Color top = foreground.GetPixelBilinear(layerU, layerV);
                        value = Color.Lerp(background.GetPixelBilinear(layerU, layerV), top, top.a);
                        value.a = 1f;
                    }
                    preview.SetPixel(x, y, value);
                }
            preview.Apply();
            File.WriteAllBytes(Path.Combine(output, "android-circle-preview.png"), preview.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(preview);
            File.WriteAllText(Path.Combine(output, "icon-settings.txt"), $"PASS Windows: {desktop.Length} icon slots.\nPASS Android: {adaptive.Length} adaptive slots with background and padded foreground.\nPASS Foreground alpha and padding.\nSource: {Source}, {master.width}x{master.height}.\n");
        }
    }
}
