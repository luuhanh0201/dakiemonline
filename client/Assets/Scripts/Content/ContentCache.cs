using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaKiem.Client.Content
{
    /// <summary>Immutable verified payloads and an atomic current/previous manifest. Never touches account storage.</summary>
    public sealed class ContentCache
    {
        public string Root { get; }
        public ContentManifest Baseline { get; }
        private readonly Dictionary<string, ContentFile> baseline;
        public ContentCache(string root, ContentManifest builtIn)
        {
            Root = Path.GetFullPath(root);
            Baseline = builtIn;
            baseline = builtIn.Index();
            Directory.CreateDirectory(Path.Combine(Root, "blobs"));
        }
        public string BlobPath(ContentFile file) => PayloadPath(file.sha256, ".bin");
        public string PartPath(ContentFile file) => PayloadPath(file.sha256, ".part");
        public FileStream AcquireLease() => new FileStream(Path.Combine(Root, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        private string PayloadPath(string hash, string extension)
        {
            if (!ContentHash.IsHash(hash)) throw new InvalidDataException("Invalid cache hash.");
            return Path.Combine(Root, "blobs", hash + extension);
        }
        public bool NeedsPayload(ContentFile file) => !baseline.TryGetValue(file.key, out ContentFile native) || native.Fingerprint() != file.Fingerprint();
        public bool HasPayload(ContentFile file)
        {
            string path = BlobPath(file);
            return System.IO.File.Exists(path) && new FileInfo(path).Length == file.size && ContentHash.File(path) == file.sha256;
        }
        public bool Complete(ContentManifest manifest)
        {
            foreach (ContentFile file in manifest.files) if (NeedsPayload(file) && !HasPayload(file)) return false;
            return true;
        }
        public void AcceptPart(ContentFile file)
        {
            string part = PartPath(file);
            if (!System.IO.File.Exists(part) || new FileInfo(part).Length != file.size || ContentHash.File(part) != file.sha256)
                throw new InvalidDataException("Downloaded content failed its checksum.");
            string final = BlobPath(file);
            if (System.IO.File.Exists(final)) System.IO.File.Delete(final);
            System.IO.File.Move(part, final);
        }
        public void ResetPart(ContentFile file)
        {
            string path = PartPath(file);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        public IEnumerable<string> ReadSnapshots()
        {
            foreach (string name in new[] { "current.json", "previous.json" })
            {
                string text = null;
                try { string path = Path.Combine(Root, name); if (System.IO.File.Exists(path)) text = System.IO.File.ReadAllText(path, Encoding.UTF8); }
                catch (IOException) { }
                if (text != null) yield return text;
            }
        }
        public void Commit(ContentManifest manifest, string json)
        {
            manifest.Validate(Baseline.platform, Baseline.buildCode);
            if (!Complete(manifest)) throw new InvalidDataException("Cannot activate an incomplete content release.");
            string current = Path.Combine(Root, "current.json");
            string pending = Path.Combine(Root, "current.tmp");
            System.IO.File.WriteAllText(pending, json, new UTF8Encoding(false));
            if (System.IO.File.Exists(current)) System.IO.File.Replace(pending, current, Path.Combine(Root, "previous.json"));
            else System.IO.File.Move(pending, current);
        }

        public void RepairCurrent(string verifiedJson)
        {
            string current = Path.Combine(Root, "current.json");
            string pending = Path.Combine(Root, "recovered.tmp");
            System.IO.File.WriteAllText(pending, verifiedJson, new UTF8Encoding(false));
            if (System.IO.File.Exists(current)) System.IO.File.Replace(pending, current, null);
            else System.IO.File.Move(pending, current);
        }
    }
}
