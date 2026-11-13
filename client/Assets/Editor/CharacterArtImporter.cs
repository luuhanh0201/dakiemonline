using UnityEditor;

namespace DaKiem.Client.Editor
{
    /// <summary>Imports character parts and map art as sprites at the scale the game expects.</summary>
    public sealed class CharacterArtImporter : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Resources/Character/";
        private const string WorldFolder = "Assets/Resources/World/";
        private const float WorldPixelsPerUnit = 200f;

        /// <summary>Raised when these rules change, so Unity imports the art again with them.</summary>
        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            bool isWorld = assetPath.StartsWith(WorldFolder);

            if (!isWorld && !assetPath.StartsWith(CharacterFolder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = isWorld ? WorldPixelsPerUnit : CharacterRigScale.PixelsPerUnit;

            // The house and the door effect are drawn larger than they show, so they need smaller copies to sample.
            // The old master is the exception: he is saved at the character's own density and, like the
            // character, drawn without them, so both look equally sharp side by side.
            bool drawnLarge = (assetPath.StartsWith(WorldFolder + "House/") && !assetPath.EndsWith("/master.png"))
                || assetPath.StartsWith(WorldFolder + "Door/");
            importer.mipmapEnabled = drawnLarge;

            if (drawnLarge)
            {
                importer.filterMode = UnityEngine.FilterMode.Trilinear;

                // A little sharper than plain mips, so outlines stay as crisp as the character's, which has none.
                importer.mipMapBias = -0.5f;
                importer.alphaIsTransparency = true;
                importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            }
            else
            {
                importer.filterMode = UnityEngine.FilterMode.Bilinear;
            }

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // Tiny alpha masks used to keep the blade outside the face and headgear.
            importer.isReadable = assetPath.EndsWith("/head.png") || assetPath.Contains("/hat_") || assetPath.Contains("/weapon_");
        }
    }
}
