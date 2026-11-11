using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Imports the account-screen art in Resources/UI as sprites. Frames, buttons and fields stretch
    /// in nine parts, so their corner sizes (in pixels of the cut art) are set here. Background layers
    /// keep compression; fine silhouettes use lossless alpha and trilinear mip sampling.
    /// </summary>
    public sealed class UiArtImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/UI/";

        // left, bottom, right, top
        private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            ["panel"] = new Vector4(150, 150, 150, 300),
            ["title"] = new Vector4(150, 60, 150, 60),
            ["btn_gold"] = new Vector4(130, 100, 130, 100),
            ["btn_gold_pressed"] = new Vector4(130, 100, 130, 100),
            ["btn_gold_disabled"] = new Vector4(130, 100, 130, 100),
            ["btn_blue"] = new Vector4(110, 95, 110, 95),
            ["btn_blue_pressed"] = new Vector4(110, 95, 110, 95),
            ["btn_blue_disabled"] = new Vector4(110, 95, 110, 95),
            ["btn_blue_small"] = new Vector4(80, 70, 80, 70),
            ["field"] = new Vector4(70, 70, 70, 70),
            ["field_focus"] = new Vector4(70, 70, 70, 70),
            ["field_error"] = new Vector4(70, 70, 70, 70),
            ["card"] = new Vector4(70, 70, 70, 70),
            ["card_selected"] = new Vector4(70, 70, 70, 70),
            ["bar_back"] = new Vector4(80, 50, 80, 50),
            ["bar_fill"] = new Vector4(60, 40, 60, 40),
        };

        /// <summary>Raised when these rules change, so Unity imports the art again with them.</summary>
        public override uint GetVersion() => 3;

        internal static bool FineSilhouette(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("lop7") || name.StartsWith("lop8_canh_") ||
                name.StartsWith("icon_") || name.StartsWith("eye_") || name.StartsWith("check_") ||
                name.StartsWith("dot_") || name == "close";
        }

        internal static bool HasKeyResidue(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name == "lop7a_nguoi" || name == "lop6_hoa" || name == "lop3_den";
        }

        // Imported pixels only: preserve every solid/antialiased edge; remove faint export dust
        // more than two pixels away from any meaningful opacity. Source PNGs remain untouched.
        internal static int CleanKeyResidue(Color32[] pixels, int width, int height)
        {
            int removed = 0;
            for (int at = 0; at < pixels.Length; at++)
            {
                byte alpha = pixels[at].a;
                if (alpha == 0 || alpha >= 32) continue;
                int x = at % width, y = at / width;
                bool supported = false;
                for (int dy = -2; dy <= 2 && !supported; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height && pixels[(y + dy) * width + x + dx].a >= 64)
                        { supported = true; break; }
                if (!supported) { pixels[at] = new Color32(0, 0, 0, 0); removed++; }
            }
            return removed;
        }

        private void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(Folder) || !HasKeyResidue(assetPath)) return;
            Color32[] pixels = texture.GetPixels32();
            int removed = CleanKeyResidue(pixels, texture.width, texture.height);
            if (removed == 0) return;
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            Debug.Log($"UI_ALPHA_CLEAN {assetPath}: {removed} isolated faint pixels");
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            // The art is drawn far larger than it shows (a 171 px petal at about 20 px): without
            // smaller copies to sample from, its edges break into steps and flicker as it turns.
            importer.mipmapEnabled = true;
            // Kaiser sharpens the dark outline and can ring around transparent edges; box mips
            // preserve alpha coverage, trilinear avoids visible jumps while parts rotate/breathe.
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // No piece is over 2048 except the logo, so the nine-part borders stay in source pixels.
            importer.maxTextureSize = 2048;
            importer.textureCompression = FineSilhouette(assetPath)
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.CompressedHQ;
            importer.npotScale = TextureImporterNPOTScale.None;

            // Full rectangles: the wind shader reads the texture around each pixel, and sliced images need them.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;

            if (Borders.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out Vector4 border))
            {
                settings.spriteBorder = border;
            }

            importer.SetTextureSettings(settings);
        }
    }
}
