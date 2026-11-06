using UnityEditor;

namespace DaKiem.Client.Editor
{
    /// <summary>Imports character parts and map art as sprites at the scale the game expects.</summary>
    public sealed class CharacterArtImporter : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Resources/Character/";
        private const string WorldFolder = "Assets/Resources/World/";
        private const float WorldPixelsPerUnit = 200f;

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
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // Tiny alpha masks used to keep the blade outside the face and headgear.
            importer.isReadable = assetPath.EndsWith("/head.png") || assetPath.Contains("/hat_") || assetPath.Contains("/weapon_");
        }
    }
}
