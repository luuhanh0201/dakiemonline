using System;
using System.IO;
using System.Linq;
using System.Text;
using DaKiem.Client.Accounts;
using DaKiem.Client.Content;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    public static class ContentSmokeFixtures
    {
        [Serializable] public sealed class Metadata
        {
            public string imageKey, imageResource, firstImageHash, secondImageHash, slowHash;
            public int expectedPetals;
        }
        private static ContentManifest Clone(ContentManifest source) => JsonUtility.FromJson<ContentManifest>(JsonUtility.ToJson(source));
        public static void Export()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/content-update/fixture"));
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/ContentSources/windows"));
            Directory.CreateDirectory(Path.Combine(root, "content/blobs"));
            Directory.CreateDirectory(Path.Combine(root, "content/windows"));
            var baseline = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(Path.Combine(source, "manifest.json")));
            string key = "resources/World/Village/Far/mountains";
            ContentFile originalImage = baseline.files.First(file => file.key == key);
            byte[] originalBytes = File.ReadAllBytes(Path.Combine(source, "blobs", originalImage.sha256 + ".bin"));
            byte[] firstBytes = ChangeInvisiblePixel(originalBytes, 1);
            byte[] secondBytes = ChangeInvisiblePixel(originalBytes, 2);
            var first = Clone(baseline); first.version = 1;
            ContentFile firstImage = first.files.First(file => file.key == key);
            firstImage.sha256 = WriteBlob(root, firstBytes); firstImage.size = firstBytes.Length;
            ContentFile button = first.files.First(file => file.key == "resources/UI/btn_gold");
            byte[] buttonBytes = ChangeInvisiblePixel(File.ReadAllBytes(Path.Combine(source, "blobs", button.sha256 + ".bin")), 1);
            button.sha256 = WriteBlob(root, buttonBytes); button.size = buttonBytes.Length;
            var scene = LoginSceneConfig.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "../../config/login-scene.json")));
            scene.petalCount++;
            byte[] sceneBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(scene));
            ContentFile firstConfig = first.files.First(file => file.key == "config/login-scene.json");
            firstConfig.sha256 = WriteBlob(root, sceneBytes); firstConfig.size = sceneBytes.Length;
            WriteManifest(root, "good.json", first);
            var second = Clone(first); second.version = 2;
            ContentFile secondImage = second.files.First(file => file.key == key);
            secondImage.sha256 = WriteBlob(root, secondBytes); secondImage.size = secondBytes.Length;
            File.WriteAllBytes(Path.Combine(root, "correct-second-image.bin"), secondBytes);
            WriteManifest(root, "second.json", second);
            var third = Clone(second); third.version = 3;
            byte[] slow = Encoding.UTF8.GetBytes(new string('A', 8 * 1024 * 1024));
            string slowHash = WriteBlob(root, slow);
            third.files = third.files.Concat(new[] { new ContentFile { key = "resources/UI/ContentSmoke/resume", resource = "UI/ContentSmoke/resume", kind = "text", sha256 = slowHash, size = slow.Length } }).ToArray();
            WriteManifest(root, "slow.json", third);
            var invalid = Clone(third); invalid.version = 4;
            invalid.files[0].key = "resources/../../account.token";
            WriteManifest(root, "invalid.json", invalid);
            File.WriteAllText(Path.Combine(root, "metadata.json"), JsonUtility.ToJson(new Metadata
            {
                imageKey = key, imageResource = originalImage.resource, firstImageHash = firstImage.sha256, secondImageHash = secondImage.sha256,
                slowHash = slowHash, expectedPetals = scene.petalCount,
            }));
            File.Copy(Path.Combine(root, "good.json"), Path.Combine(root, "content/windows/current.json"), true);
            Debug.Log("CONTENT_SMOKE_FIXTURES " + root);
        }
        private static byte[] ChangeInvisiblePixel(byte[] png, byte change)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(texture, png, false);
            Color32[] colors = texture.GetPixels32();
            int index = Array.FindIndex(colors, color => color.a == 0);
            if (index < 0) index = 0;
            Color32 pixel = colors[index]; pixel.r ^= change; colors[index] = pixel;
            texture.SetPixels32(colors); texture.Apply();
            byte[] result = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return result;
        }
        private static string WriteBlob(string root, byte[] bytes)
        {
            string hash = ContentHash.Bytes(bytes);
            File.WriteAllBytes(Path.Combine(root, "content/blobs", hash + ".bin"), bytes);
            return hash;
        }
        private static void WriteManifest(string root, string name, ContentManifest value) => File.WriteAllText(Path.Combine(root, name), JsonUtility.ToJson(value), new UTF8Encoding(false));
    }
}
