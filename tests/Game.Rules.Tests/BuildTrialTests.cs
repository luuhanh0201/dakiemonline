using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.BuildTest;
using Xunit;

namespace Game.Rules.Tests
{
    /// <summary>The bot and the scoring of tools/Game.BuildTest, which measures the builds for T040.</summary>
    public class BuildTrialTests
    {
        private readonly GameConfig config = TestConfig.Load();
        private readonly ProgressionConfig prices;
        private readonly BuildTestConfig test;

        public BuildTrialTests()
        {
            prices = config.ParseProgression(Text(GameConfig.ProgressionFile));
            test = config.ParseBuildTest(Text(GameConfig.BuildTestFile), prices);
        }

        private static string Text(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", file));

        private GameConfig With(CharacterStat stat, int steps)
        {
            var plan = new BuildPlan { Id = "build.test", StatSteps = new Dictionary<CharacterStat, int> { [stat] = steps } };
            return config.WithCharacter(Progression.Apply(config.Character, prices, plan));
        }

        private static Trial TrialOf(bool higherIsBetter, double minLeadPercent, params (string Id, int Group, double Value)[] rows)
        {
            return new Trial(
                "t",
                "t",
                string.Empty,
                Array.Empty<string>(),
                rows.Select(row => new TrialRow(row.Id, Array.Empty<string>(), row.Group, row.Value)).ToList(),
                higherIsBetter,
                minLeadPercent);
        }

        private BuildSet FirstSet => test.Sets[0];

        [Fact]
        public void TheSameBuildsAlwaysGiveTheSameReport()
        {
            string Run() => Report.Write(test, test.Sets.Select(set => Trials.RunSet(config, prices, set, test.Trials)).ToList(), "date");

            string first = Run();

            Assert.Equal(first, Run());
            Assert.Contains("Cọc gỗ", first);
            Assert.All(test.Sets, set => Assert.Contains($"## Bộ: {set.Name}", first));
            Assert.All(test.Sets.SelectMany(set => set.Builds), build => Assert.Contains(build.Name, first));
        }

        [Fact]
        public void EveryTrialHasARowForEveryBuildOfItsSet()
        {
            foreach (BuildSet set in test.Sets)
            {
                SetResult result = Trials.RunSet(config, prices, set, test.Trials);

                Assert.Equal(
                    new[] { "burst", "dummy", "endurance", "endure", "boss_stand", "boss_dodge", "duel_full", "duel_empty" },
                    result.Trials.Select(trial => trial.Id));
                Assert.All(result.Trials, trial => Assert.Equal(set.Builds.Select(build => build.Id), trial.Rows.Select(row => row.BuildId)));
                Assert.All(result.Trials, trial => Assert.All(trial.Rows, row => Assert.Equal(trial.Columns.Count, row.Cells.Count)));
                Assert.Equal(set.Builds.Select(build => build.Id), result.Verdict.TrialsLed.Keys);
            }
        }

        [Fact]
        public void MoreHealthAndDefenceStandLongerInTheBlows()
        {
            EndureResult plain = Bot.Endure(config, test.Trials);
            EndureResult tough = Bot.Endure(TestConfig.LoadWith(GameConfig.CharacterFile, "\"defense\": 0", "\"defense\": 100"), test.Trials);
            EndureResult frail = Bot.Endure(TestConfig.LoadWith(GameConfig.CharacterFile, "\"maxHp\": 1000", "\"maxHp\": 300"), test.Trials);

            Assert.False(plain.Survived);
            Assert.True(plain.PotionsUsed > 0, "the bot drinks while it stands");
            Assert.True(tough.Seconds > plain.Seconds);
            Assert.True(tough.HitsTaken > plain.HitsTaken);
            Assert.True(frail.Seconds < plain.Seconds);
        }

        [Fact]
        public void MoreAttackDealsMoreToTheDummy()
        {
            DummyResult plain = Bot.HitDummy(config, test.Trials, 30d, 0m);
            DummyResult strong = Bot.HitDummy(With(CharacterStat.Attack, 20), test.Trials, 30d, 0m);

            Assert.True(plain.TotalDamage > 0);
            Assert.True(strong.TotalDamage > plain.TotalDamage);
            Assert.Equal(plain.TotalDamage / 30d, plain.DamagePerSecond, 6);
            Assert.Equal(plain.TotalDamage, plain.DamageByTick.Sum());
        }

        [Fact]
        public void DummyDefenceAndAnEmptyEnergyBarBothCutDamage()
        {
            DummyResult plain = Bot.HitDummy(config, test.Trials, 10d, 0m);
            DummyResult armoured = Bot.HitDummy(config, test.Trials, 10d, 100m);
            DummyResult drained = Bot.HitDummy(config, test.Trials, 10d, 0m, startWithEnergy: false);

            Assert.InRange(armoured.TotalDamage, plain.TotalDamage * 0.49d, plain.TotalDamage * 0.51d);
            Assert.True(drained.TotalDamage < plain.TotalDamage);
            Assert.Null(drained.EnergyShortAtSec);
        }

        [Fact]
        public void SecondsToDealCountsUpToTheTickThatCrossesTheAmount()
        {
            var result = new DummyResult(60, 0d, null, new[] { 0, 10, 0, 50 });

            Assert.Equal(2 * Ticks.DeltaSeconds, result.SecondsToDeal(10)!.Value, 9);
            Assert.Equal(4 * Ticks.DeltaSeconds, result.SecondsToDeal(11)!.Value, 9);
            Assert.Null(result.SecondsToDeal(61));
        }

        [Fact]
        public void DodgingBotBeatsTheBossAndTakesFewerHitsThanTheStandingOne()
        {
            FightResult dodging = Bot.FightDodging(config, test.Trials);
            FightResult standing = Bot.FightStanding(config, test.Trials);

            Assert.True(dodging.Won);
            Assert.Equal(0, dodging.BossHpLeft);
            Assert.True(dodging.HitsTaken < standing.HitsTaken);
            Assert.True(standing.Seconds <= test.Trials.FightMaxSec);
        }

        [Fact]
        public void TougherCharacterLastsLongerStandingInTheBlows()
        {
            GameConfig frail = TestConfig.LoadWith(GameConfig.CharacterFile, "\"maxHp\": 1000", "\"maxHp\": 300");
            GameConfig tough = TestConfig.LoadWith(GameConfig.CharacterFile, "\"defense\": 0", "\"defense\": 200");

            FightResult frailFight = Bot.FightStanding(frail, test.Trials);
            FightResult toughFight = Bot.FightStanding(tough, test.Trials);

            Assert.False(frailFight.Won);
            Assert.True(toughFight.Won);
            Assert.True(toughFight.PlayerHpLeft > 0);
        }

        [Fact]
        public void ABuildLeadsOnlyWhenAheadByTheMargin()
        {
            Trial clear = TrialOf(true, 5d, ("a", 0, 100d), ("b", 0, 106d), ("c", 0, 90d));
            Trial close = TrialOf(true, 5d, ("a", 0, 100d), ("b", 0, 104d));
            Trial level = TrialOf(true, 0d, ("a", 0, 100d), ("b", 0, 100d));

            Assert.Equal("b", clear.LeaderId);
            Assert.Equal(6d, clear.LeadPercent, 9);
            Assert.Null(close.LeaderId);
            Assert.Equal(4d, close.LeadPercent, 9);
            Assert.Null(level.LeaderId);
            Assert.Equal(0d, level.LeadPercent);
        }

        [Fact]
        public void LowerIsBetterMeasuresTheLeadAgainstTheRunnerUp()
        {
            Trial times = TrialOf(false, 5d, ("slow", 0, 40d), ("fast", 0, 36d));

            Assert.Equal("fast", times.LeaderId);
            Assert.Equal(10d, times.LeadPercent, 9);
        }

        [Fact]
        public void ABetterGroupLeadsWhateverTheValues()
        {
            Trial fights = TrialOf(false, 5d, ("lost", 1, 10d), ("won", 0, 90d), ("alsoLost", 1, 500d));
            Trial twoWon = TrialOf(false, 5d, ("lost", 1, 10d), ("won", 0, 30d), ("alsoWon", 0, 30.5d));

            Assert.Equal("won", fights.LeaderId);
            Assert.True(double.IsPositiveInfinity(fights.LeadPercent));
            Assert.Null(twoWon.LeaderId);
            Assert.Equal(new[] { "won", "alsoWon", "lost" }, twoWon.Ranked.Select(row => row.BuildId));
        }

        [Fact]
        public void AnyLeadOverNothingIsClear()
        {
            Assert.Equal("a", TrialOf(true, 0d, ("a", 0, 1d), ("b", 0, 0d)).LeaderId);
        }

        [Fact]
        public void EachBuildMustLeadSomethingAndNoneMayLeadEverything()
        {
            IReadOnlyList<BuiltCharacter> builds = Trials.Build(config, prices, FirstSet);
            string[] ids = builds.Select(build => build.Plan.Id).ToArray();
            Trial LedBy(int winner) => TrialOf(true, 5d, ids.Select((id, index) => (id, 0, index == winner ? 2d : 1d)).ToArray());

            Verdict shared = Trials.Judge(builds, new[] { LedBy(0), LedBy(1), LedBy(2) }, test.Trials);
            Verdict oneLeft = Trials.Judge(builds, new[] { LedBy(0), LedBy(1), LedBy(1) }, test.Trials);
            Verdict sweep = Trials.Judge(builds, new[] { LedBy(0), LedBy(0) }, test.Trials);

            Assert.True(shared.Passed);
            Assert.Empty(shared.Reasons);
            Assert.False(oneLeft.Passed);
            Assert.Contains(builds[2].Plan.Name, Assert.Single(oneLeft.Reasons));
            Assert.Equal(0, oneLeft.TrialsLed[ids[2]]);
            Assert.False(sweep.Passed);
            Assert.Contains(sweep.Reasons, reason => reason.Contains("mọi bài"));
        }
    }
}
