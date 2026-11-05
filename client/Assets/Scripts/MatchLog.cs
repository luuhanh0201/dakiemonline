using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Appends each finished fight to a CSV in the game's own folder on the phone.</summary>
    public static class MatchLog
    {
        private const string FileName = "match-results.csv";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        // The byte order mark lets Excel read the Vietnamese attack names.
        private static readonly Encoding FileEncoding = new UTF8Encoding(true);

        public static void Write(Simulation fight, int lowestFps)
        {
            string path = Path.Combine(Application.persistentDataPath, FileName);
            string time = DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
            string line = MatchRecord.ToCsvLine(fight, time, SystemInfo.deviceModel, lowestFps);

            try
            {
                string header = File.Exists(path) ? string.Empty : MatchRecord.CsvHeader + "\n";
                File.AppendAllText(path, header + line + "\n", FileEncoding);
            }
            catch (IOException error)
            {
                // A lost record must not stop the player from seeing the end screen.
                Debug.LogError($"Cannot write {FileName}: {error.Message}");
            }
        }
    }
}
