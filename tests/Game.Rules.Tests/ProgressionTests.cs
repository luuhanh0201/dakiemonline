using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Game.Rules.Tests
{
    public class ProgressionTests
    {
        private static readonly MovementIntent Idle = new MovementIntent(0d, 0d, false);

        private readonly GameConfig config = TestConfig.Load();

        private static string Text(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", file));

        private ProgressionConfig Prices() => config.ParseProgression(Text(GameConfig.ProgressionFile));

        private BuildTestConfig BuildTest() => config.ParseBuildTest(Text(GameConfig.BuildTestFile), Prices());

        private BuildPlan Build(string id) => BuildTest().Sets.SelectMany(set => set.Builds).Single(build => build.Id == id);

        private GameConfig Playing(BuildPlan plan) => config.WithCharacter(Progression.Apply(config.Character, Prices(), plan));

        private static BuildPlan Plan(Dictionary<CharacterStat, int>? steps = null, Dictionary<SkillSlot, int>? levels = null)
        {
            return new BuildPlan
            {
                Id = "build.test",
                StatSteps = steps ?? new Dictionary<CharacterStat, int>(),
                SkillLevels = levels ?? new Dictionary<SkillSlot, int>(),
            };
        }

        /// <summary>Casts one skill at a target right in front of the player and returns what happened.</summary>
        private static List<CombatEvent> Cast(GameConfig playing, SkillSlot slot, ResourceState? resources = null, CombatState? combat = null)
        {
            var movement = new MovementState(5d, 0d, playing.Character.MaxStamina);
            combat ??= new CombatState(playing.Potion);
            resources ??= new ResourceState(playing.Character);
            var map = new CollisionMap(playing.MapBoxes);
            var target = new CombatTarget(new CombatBox(5.5d, 0d, 7.5d, 2.5d), 0m);
            var events = new List<CombatEvent>();

            Combat.Step(combat, movement, resources, Idle, new CombatIntent(slot), playing, map, target, events);

            for (int tick = 0; tick < Ticks.PerSecond * 2; tick++)
            {
                Combat.Step(combat, movement, resources, Idle, new CombatIntent(null), playing, map, target, events);
            }

            return events;
        }

        [Theory]
        [InlineData(1, 200)]
        [InlineData(2, 220)]
        [InlineData(3, 240)]
        [InlineData(4, 260)]
        public void StepCostFollowsTheAttackExampleInTheDesign(int step, int expected)
        {
            Assert.Equal(expected, Progression.StepCost(200, 20, step));
        }

        [Theory]
        [InlineData(200, 20, 0, 0)]
        [InlineData(200, 20, 1, 200)]
        [InlineData(200, 20, 4, 920)]
        [InlineData(400, 200, 9, 10800)]
        [InlineData(600, 300, 9, 16200)]
        public void BuyingSeveralStepsAddsUpEachStepsOwnPrice(int first, int increase, int steps, int expected)
        {
            int oneByOne = Enumerable.Range(1, steps).Sum(step => Progression.StepCost(first, increase, step));

            Assert.Equal(expected, Progression.TotalCost(first, increase, steps));
            Assert.Equal(oneByOne, Progression.TotalCost(first, increase, steps));
        }

        [Fact]
        public void BadStepCountsAreRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.StepCost(200, 20, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.TotalCost(200, 20, -1));
        }

        [Fact]
        public void PriceListMatchesTheApprovedTables()
        {
            ProgressionConfig prices = Prices();

            Assert.Equal(ConfigStatus.Thu, prices.Status);
            Assert.Equal(new[] { 1, 10, 1, 2 }, new[] { CharacterStat.Attack, CharacterStat.MaxHp, CharacterStat.Defense, CharacterStat.MaxEnergy }.Select(stat => prices.Stat(stat).PerStep));
            Assert.All(prices.Stats, price => Assert.Equal((200, 20), (price.FirstCost, price.CostIncrease)));
            Assert.Equal(5, prices.Skills.Count);
            Assert.All(prices.Skills, price => Assert.Equal(10, price.MaxLevel));
            Assert.Equal((400, 200), (prices.Skill(SkillSlot.Ranged)!.FirstCost, prices.Skill(SkillSlot.Ranged)!.CostIncrease));
            Assert.Equal((600, 300), (prices.Skill(SkillSlot.Ultimate)!.FirstCost, prices.Skill(SkillSlot.Ultimate)!.CostIncrease));
            Assert.Null(prices.Skill(SkillSlot.Attack));
        }

        [Theory]
        [InlineData(SkillSlot.Ranged, 10)]
        [InlineData(SkillSlot.Support, 10)]
        [InlineData(SkillSlot.OptionA, 10)]
        [InlineData(SkillSlot.OptionB, 10)]
        [InlineData(SkillSlot.Ultimate, 8)]
        [InlineData(SkillSlot.Attack, 0)]
        public void SkillsGainTheApprovedPercentPerLevel(SkillSlot slot, int percent)
        {
            Assert.Equal(percent, config.Skill(slot).LevelPercent);
        }

        [Theory]
        [InlineData("build.chi_so_80", 15840, 4000, 80)]
        [InlineData("build.can_bang_50", 10100, 9700, 50)]
        [InlineData("build.ky_nang_80", 3600, 16300, 20)]
        [InlineData("build.tn50_chi_so_80", 41180, 8700, 80)]
        [InlineData("build.tn50_can_bang_50", 25560, 24400, 50)]
        [InlineData("build.tn50_ky_nang_80", 10420, 39400, 20)]
        public void RatioBuildsSpendTheirBudgetInTheNamedShares(string id, int statCost, int skillCost, int statSharePercent)
        {
            ProgressionConfig prices = Prices();
            BuildPlan build = Build(id);

            Assert.Equal(statCost, Progression.StatCost(prices, build));
            Assert.Equal(skillCost, Progression.SkillCost(prices, build));
            Assert.InRange(100d * statCost / (statCost + skillCost), statSharePercent - 4d, statSharePercent + 4d);
        }

        [Fact]
        public void EveryBuildOfASetSpendsNearlyTheWholeBudget()
        {
            ProgressionConfig prices = Prices();
            BuildTestConfig test = BuildTest();

            Assert.Equal(new[] { 20000, 20000, 50000, 50000 }, test.Sets.Select(set => set.BudgetTn));

            foreach (BuildSet set in test.Sets)
            {
                Assert.Equal(3, set.Builds.Count);
                Assert.All(set.Builds, build => Assert.InRange(Progression.Cost(prices, build), set.BudgetTn * 0.975d, set.BudgetTn));
            }
        }

        [Theory]
        [InlineData("build.tn50_don_cong", 161, 1050, 0, 1)]
        [InlineData("build.tn50_don_trau", 103, 1410, 41, 1)]
        [InlineData("build.tn50_don_ky_nang", 103, 1000, 0, 10)]
        public void OneDirectionBuildsPutNearlyEverythingInOnePlace(string id, int attack, int maxHp, int defense, int rangedLevel)
        {
            CharacterConfig built = Progression.Apply(config.Character, Prices(), Build(id));

            Assert.Equal(attack, built.Attack);
            Assert.Equal(maxHp, built.MaxHp);
            Assert.Equal(defense, built.Defense);
            Assert.Equal(rangedLevel, built.LevelOf(SkillSlot.Ranged));
        }

        [Fact]
        public void ApplyAddsStatStepsAndSkillLevels()
        {
            CharacterConfig built = Progression.Apply(config.Character, Prices(), Build("build.chi_so_80"));

            Assert.Equal(120m, built.Attack);
            Assert.Equal(1120, built.MaxHp);
            Assert.Equal(8m, built.Defense);
            Assert.Equal(116, built.MaxEnergy);
            Assert.Equal(config.Character.MaxStamina, built.MaxStamina);
            Assert.Equal(3, built.LevelOf(SkillSlot.Ranged));
            Assert.Equal(2, built.LevelOf(SkillSlot.Ultimate));
            Assert.Equal(1, built.LevelOf(SkillSlot.Attack));
            Assert.Equal(100m, config.Character.Attack);
        }

        [Fact]
        public void AnEmptyPlanLeavesTheCharacterAsItWas()
        {
            CharacterConfig built = Progression.Apply(config.Character, Prices(), Plan());

            Assert.Equal(config.Character.Attack, built.Attack);
            Assert.Equal(config.Character.MaxHp, built.MaxHp);
            Assert.All(Enum.GetValues(typeof(SkillSlot)).Cast<SkillSlot>(), slot => Assert.Equal(1, built.LevelOf(slot)));
            Assert.Equal(0, Progression.Cost(Prices(), Plan()));
        }

        [Fact]
        public void LevelsOutsideASkillsRangeAreRefused()
        {
            ProgressionConfig prices = Prices();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => Progression.Apply(config.Character, prices, Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Ranged] = 11 })));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Progression.Apply(config.Character, prices, Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Ranged] = 0 })));
            Assert.Throws<ArgumentException>(
                () => Progression.Cost(prices, Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Attack] = 2 })));
        }

        [Theory]
        [InlineData(1, 160)]
        [InlineData(5, 224)]
        [InlineData(10, 304)]
        public void SkillLevelScalesTheDamageOfAHit(int level, int expected)
        {
            GameConfig playing = Playing(Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Ranged] = level }));

            CombatEvent hit = Cast(playing, SkillSlot.Ranged).Single(item => item.Kind == CombatEventKind.SkillHit);

            Assert.Equal(expected, hit.Amount);
        }

        [Fact]
        public void AttackStepsAndSkillLevelMultiplyTogether()
        {
            GameConfig playing = Playing(Plan(
                new Dictionary<CharacterStat, int> { [CharacterStat.Attack] = 20 },
                new Dictionary<SkillSlot, int> { [SkillSlot.Ranged] = 3 }));

            CombatEvent hit = Cast(playing, SkillSlot.Ranged).Single(item => item.Kind == CombatEventKind.SkillHit);

            // (40 + 120 × 1.2) × [1 + 10% × 2] = 184 × 1.2
            Assert.Equal(220, hit.Amount);
        }

        [Fact]
        public void BasicAttackGrowsWithAttackOnly()
        {
            GameConfig playing = Playing(Plan(new Dictionary<CharacterStat, int> { [CharacterStat.Attack] = 20 }));

            CombatEvent hit = Cast(playing, SkillSlot.Attack).First(item => item.Kind == CombatEventKind.SkillHit);

            Assert.Equal(72, hit.Amount);
        }

        [Fact]
        public void SkillLevelChangesNeitherEnergyCostNorCooldown()
        {
            GameConfig playing = Playing(Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Ranged] = 6 }));
            var resources = new ResourceState(playing.Character);
            var combat = new CombatState(playing.Potion);
            var movement = new MovementState(5d, 0d, playing.Character.MaxStamina);
            var events = new List<CombatEvent>();
            SkillConfig skill = playing.Skill(SkillSlot.Ranged);

            Combat.Step(combat, movement, resources, Idle, new CombatIntent(SkillSlot.Ranged), playing, new CollisionMap(playing.MapBoxes), null, events);

            double regen = playing.Character.EnergyRegenPerSec * Ticks.DeltaSeconds;
            Assert.Equal(playing.Character.MaxEnergy - skill.EnergyCost + regen, resources.Energy, 6);
            Assert.Equal(skill.CooldownSec, combat.CooldownSecondsLeft(SkillSlot.Ranged), 6);
        }

        [Theory]
        [InlineData(1, 1000, 200)]
        [InlineData(2, 1000, 220)]
        [InlineData(5, 1000, 280)]
        [InlineData(2, 1120, 233)]
        public void HealScalesWithLevelOnBothItsParts(int level, int maxHp, int expected)
        {
            SkillConfig heal = config.Skill(SkillSlot.Support);

            Assert.Equal(expected, ResourceRules.HealAmount(heal.Heal!, heal.LevelPercent, level, maxHp));
        }

        [Fact]
        public void CastingTheHealUsesItsLevel()
        {
            GameConfig playing = Playing(Plan(levels: new Dictionary<SkillSlot, int> { [SkillSlot.Support] = 5 }));
            var resources = new ResourceState(playing.Character);
            ResourceRules.TakeDamage(resources, 600);

            CombatEvent healed = Cast(playing, SkillSlot.Support, resources).Single(item => item.Kind == CombatEventKind.SelfHealed);

            Assert.Equal(280, healed.Amount);
            Assert.Equal(680, resources.Hp);
        }

        [Fact]
        public void TougherBuildTakesLessFromTheBossAndHasMoreHealth()
        {
            GameConfig playing = Playing(Build("build.chi_so_80"));
            var fight = new Simulation(playing, 5d);

            Assert.Equal(1120, fight.Resources.Hp);
            Assert.Equal(111, SkillDamage.AfterDefense(config.Boss.Cycle[0].Damage, playing.Character.Defense));
        }

        [Theory]
        [InlineData(GameConfig.ProgressionFile, "\"id\": \"thuy.kiem_khi\"", "\"id\": \"thuy.khong_co\"", "unknown skill")]
        [InlineData(GameConfig.ProgressionFile, "\"id\": \"thuy.kiem_khi\"", "\"id\": \"thuy.danh_thuong\"", "basic attack")]
        [InlineData(GameConfig.ProgressionFile, "\"id\": \"thuy.hoi_mau\"", "\"id\": \"thuy.kiem_khi\"", "duplicate")]
        [InlineData(GameConfig.ProgressionFile, "{ \"id\": \"defense\", \"perStep\": 1, \"firstCost\": 200, \"costIncrease\": 20 },", "", "no price for defense")]
        [InlineData(GameConfig.ProgressionFile, "\"maxLevel\": 10, \"firstCost\": 600", "\"maxLevel\": 1, \"firstCost\": 600", "at least 2")]
        [InlineData(GameConfig.ProgressionFile, "\"perStep\": 10", "\"perStep\": 0", "greater than 0")]
        [InlineData(GameConfig.BuildTestFile, "\"budgetTn\": 20000", "\"budgetTn\": 19000", "over the budget")]
        [InlineData(GameConfig.BuildTestFile, "{ \"id\": \"thuy.kiem_khi\", \"level\": 6 }", "{ \"id\": \"thuy.kiem_khi\", \"level\": 11 }", "from 1 to 10")]
        [InlineData(GameConfig.BuildTestFile, "\"id\": \"build.can_bang_50\"", "\"id\": \"build.chi_so_80\"", "duplicate")]
        [InlineData(GameConfig.BuildTestFile, "\"id\": \"set.tn50_ty_le\"", "\"id\": \"set.tn20_ty_le\"", "duplicate")]
        [InlineData(GameConfig.BuildTestFile, "\"id\": \"set.tn50_ty_le\"", "\"id\": \"build.chi_so_80\"", "duplicate")]
        [InlineData(GameConfig.BuildTestFile, "\"budgetTn\": 50000", "\"budgetTn\": 0", "greater than 0")]
        [InlineData(GameConfig.BuildTestFile, "{ \"id\": \"attack\", \"steps\": 7 }", "{ \"id\": \"luck\", \"steps\": 7 }", "not one of")]
        [InlineData(GameConfig.BuildTestFile, "\"potionBelowHpPercent\": 40", "\"potionBelowHpPercent\": 140", "from 0 to 100")]
        [InlineData(GameConfig.BuildTestFile, "\"dummyDefense\": 0,", "\"dummyDefense\": 0, \"extra\": 1,", "unknown")]
        [InlineData(GameConfig.BuildTestFile, "\"minLeadPercent\": 5", "\"minLeadPercent\": 500", "from 0 to 100")]
        [InlineData(GameConfig.BuildTestFile, "\"id\": \"endurance\"", "\"id\": \"burst\"", "duplicate")]
        [InlineData(GameConfig.BuildTestFile, "\"seconds\": 180", "\"seconds\": 0", "greater than 0")]
        public void BadFilesAreRejected(string file, string find, string replace, string problem)
        {
            string progression = Text(GameConfig.ProgressionFile);
            string buildTest = Text(GameConfig.BuildTestFile);
            Assert.Contains(find, file == GameConfig.ProgressionFile ? progression : buildTest);

            InvalidConfigException error = Assert.Throws<InvalidConfigException>(() =>
            {
                if (file == GameConfig.ProgressionFile)
                {
                    config.ParseProgression(progression.Replace(find, replace));
                }
                else
                {
                    config.ParseBuildTest(buildTest.Replace(find, replace), config.ParseProgression(progression));
                }
            });

            Assert.Equal(file, error.File);
            Assert.Contains(problem, error.Message);
        }

        [Fact]
        public void PricedSkillWithoutLevelPercentIsRejected()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.SkillsFile] = files[GameConfig.SkillsFile].Replace("\"levelPercent\": 8,", string.Empty);
            GameConfig flat = GameConfig.Parse(files);

            InvalidConfigException error = Assert.Throws<InvalidConfigException>(() => flat.ParseProgression(Text(GameConfig.ProgressionFile)));

            Assert.Contains("levelPercent", error.Message);
        }
    }
}
