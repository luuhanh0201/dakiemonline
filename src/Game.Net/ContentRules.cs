using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Game.Net
{
    /// <summary>Identifies the exact rule/config snapshot understood by both client prediction and server.</summary>
    public static class ContentRules
    {
        public static string Fingerprint(IReadOnlyDictionary<string, string> files)
        {
            var input = new StringBuilder();
            foreach (var pair in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                input.Append(pair.Key.Length).Append(':').Append(pair.Key).Append(':').Append(pair.Value.Length).Append(':').Append(pair.Value).Append(';');
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input.ToString()))).Replace("-", "").ToLowerInvariant();
        }
    }
}
