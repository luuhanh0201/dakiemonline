using UnityEditor;

namespace DaKiem.Client.Editor
{
    /// <summary>Imports character parts as sprites at the scale CharacterView expects.</summary>
    public sealed class CharacterArtImporter : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Resources/Character/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CharacterFolder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = CharacterRigScale.PixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // Tiny alpha masks used to keep the blade outside the face and headgear.
            importer.isReadable = assetPath.EndsWith("/head.png") || assetPath.Contains("/hat_") || assetPath.Contains("/weapon_");
        }
    }
}
