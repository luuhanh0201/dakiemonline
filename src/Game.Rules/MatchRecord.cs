using System.Globalization;

namespace Game.Rules
{
    /// <summary>One finished fight as a CSV line, for the player test table in SPEC-0.5 step 7.</summary>
    public static class MatchRecord
    {
        public const string CsvHeader =
            "thoi_diem,thiet_bi,ket_qua,thoi_gian_giay,so_don_bi_trung,thuoc_da_dung,don_khien_thua,fps_thap_nhat";

        public static string ToCsvLine(Simulation fight, string finishedAt, string device, int lowestFps)
        {
            string result = fight.Result == MatchResult.Won ? "Thang" : "Thua";
            string seconds = fight.SecondsPlayed.ToString("0.0", CultureInfo.InvariantCulture);
            string killedBy = fight.KilledBy?.Name ?? string.Empty;

            return string.Join(
                ",",
                Escape(finishedAt),
                Escape(device),
                result,
                seconds,
                fight.HitsTaken.ToString(CultureInfo.InvariantCulture),
                fight.PotionsUsed.ToString(CultureInfo.InvariantCulture),
                Escape(killedBy),
                lowestFps.ToString(CultureInfo.InvariantCulture));
        }

        private static string Escape(string value)
        {
            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }
    }
}
