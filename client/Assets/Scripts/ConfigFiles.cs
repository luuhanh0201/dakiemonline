using System.Collections;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;
using UnityEngine.Networking;

namespace DaKiem.Client
{
    /// <summary>Fetches the config text; checking it is the job of GameConfig in Game.Rules.</summary>
    public static class ConfigFiles
    {
        /// <summary>A file that cannot be read is left out, so GameConfig reports it as missing.</summary>
        public static IEnumerator LoadAll(Dictionary<string, string> files)
        {
            foreach (string fileName in GameConfig.FileNames)
            {
                string path = $"{Application.streamingAssetsPath}/config/{fileName}";
                string url = path.Contains("://") ? path : $"file://{path}";

                using UnityWebRequest request = UnityWebRequest.Get(url);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    files[fileName] = request.downloadHandler.text;
                }
                else
                {
                    Debug.LogError($"Cannot read {fileName}: {request.error}");
                }
            }
        }
    }
}
