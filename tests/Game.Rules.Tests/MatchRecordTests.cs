using System.Globalization;

namespace Game.Rules.Tests
{
    public class MatchRecordTests
    {
        private const double PlayerX = 5d;
        private const int LongEnoughToLoseTicks = 3600;
        private const string Time = "2026-11-06 09:30:00";

        private readonly GameConfig config = TestConfig.Load();

        private Simulation LostFight()
        {
            var fight = new Simulation(config, PlayerX);
            var still = new MovementIntent(0d, 0d, false);

            for (int tick = 0; tick < LongEnoughToLoseTicks; tick++)
            {
                fight.Step(still, default);
            }

            return fight;
        }

        [Fact]
        public void Header_HasOneColumnPerValue()
        {
            string line = MatchRecord.ToCsvLine(LostFight(), Time, "Redmi", 58);

            Assert.Equal(MatchRecord.CsvHeader.Split(',').Length, line.Split(',').Length);
        }

        [Fact]
        public void LostFight_RecordsEveryNumberOnTheEndScreen()
        {
            Simulation fight = LostFight();
            string seconds = fight.SecondsPlayed.ToString("0.0", CultureInfo.InvariantCulture);

            string line = MatchRecord.ToCsvLine(fight, Time, "Redmi Note 8 Pro", 58);

            Assert.Equal($"{Time},Redmi Note 8 Pro,Thua,{seconds},5,0,Đòn lớn,58", line);
        }

        [Fact]
        public void UnfinishedKiller_LeavesTheColumnEmpty()
        {
            var fight = new Simulation(config, PlayerX);

            string line = MatchRecord.ToCsvLine(fight, Time, "Redmi", 60);

            Assert.Equal(string.Empty, line.Split(',')[6]);
        }

        [Fact]
        public void SecondsUseADotOnAnyPhoneLanguage()
        {
            CultureInfo original = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

                string line = MatchRecord.ToCsvLine(LostFight(), Time, "Redmi", 58);

                Assert.Contains(".", line.Split(',')[3]);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void DeviceNameWithACommaStaysOneColumn()
        {
            string line = MatchRecord.ToCsvLine(LostFight(), Time, "Xiaomi, \"Redmi\"", 58);

            Assert.Contains("\"Xiaomi, \"\"Redmi\"\"\"", line);
        }
    }
}
