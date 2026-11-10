using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DaKiem.Client.Net
{
    /// <summary>
    /// Appends each measured round trip to net-latency.csv in the game's own folder, for the tunnel
    /// test. The web build has no folder to read back, so it writes to the browser console instead.
    /// </summary>
    public static class NetLog
    {
        private const string FileName = "net-latency.csv";
        private const string Header = "time,device,platform,server,player,channel,rtt_ms,reconnect_tries";

        public static void Write(OnlineClient client, double roundTripMs)
        {
            string line = string.Join(
                ",",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                SystemInfo.deviceModel.Replace(',', ' '),
                Application.platform,
                client.Url,
                client.PlayerId.ToString(CultureInfo.InvariantCulture),
                client.ChannelId.ToString(CultureInfo.InvariantCulture),
                roundTripMs.ToString("0", CultureInfo.InvariantCulture),
                client.Reconnects.ToString(CultureInfo.InvariantCulture));

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[net-latency] {line}");
#else
            string path = Path.Combine(Application.persistentDataPath, FileName);

            try
            {
                string header = File.Exists(path) ? string.Empty : Header + "\n";
                File.AppendAllText(path, header + line + "\n");
            }
            catch (IOException error)
            {
                Debug.LogError($"Cannot write {FileName}: {error.Message}");
            }
#endif
        }
    }
}
