using System.Collections.Generic;
using Xunit;

namespace Game.Rules.Tests
{
    /// <summary>Fighting a pinned target on its own, and the NPCs a touch talks with.</summary>
    public class AutoFightTests
    {
        private readonly GameConfig config = TestConfig.Load();

        private SkillConfig Plain => config.Skill(SkillSlot.Attack);
        private SkillConfig Ranged => config.Skill(SkillSlot.Ranged);
        private AutoFightConfig Auto => config.AutoFight;

        [Fact]
        public void OnlyTheQuickRangedSkillCanStandInForThePlainAttack()
        {
            Assert.True(AutoFight.CanStandIn(Ranged, Auto));
            Assert.False(AutoFight.CanStandIn(Plain, Auto));
            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.OptionA), Auto));
            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.OptionB), Auto));
            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.Support), Auto));
            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.Ultimate), Auto));
        }

        [Fact]
        public void HealingAndTheUltimateNeverStandInEvenWhenQuick()
        {
            var quick = new AutoFightConfig { MaxCooldownSec = 1000d, RangedStandOff = 5d };

            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.Support), quick));
            Assert.False(AutoFight.CanStandIn(config.Skill(SkillSlot.Ultimate), quick));
            Assert.True(AutoFight.CanStandIn(config.Skill(SkillSlot.OptionA), quick));
        }

        [Fact]
        public void AFarTargetIsWalkedTowardWithoutAttacking()
        {
            AutoStep right = AutoFight.Decide(0d, 1, 8d, Plain, null, false, Auto);
            AutoStep left = AutoFight.Decide(8d, 1, 0d, Plain, null, false, Auto);

            Assert.Equal(1d, right.MoveX);
            Assert.Null(right.Skill);
            Assert.Equal(-1d, left.MoveX);
            Assert.Null(left.Skill);
        }

        [Fact]
        public void InReachAndFacingTheTargetThePlainAttackIsPressed()
        {
            AutoStep step = AutoFight.Decide(0d, 1, 0.5d, Plain, null, false, Auto);

            Assert.Equal(0d, step.MoveX);
            Assert.Equal(SkillSlot.Attack, step.Skill);
        }

        [Fact]
        public void FacingAwayTheFighterTurnsBeforeAttacking()
        {
            AutoStep step = AutoFight.Decide(0d, -1, 0.5d, Plain, null, false, Auto);

            Assert.Equal(AutoFight.TurnTilt, step.MoveX);
            Assert.Null(step.Skill);
        }

        [Fact]
        public void AReadyChosenRangedSkillIsCastFromItsStandOff()
        {
            AutoStep step = AutoFight.Decide(0d, 1, 4.5d, Plain, Ranged, true, Auto);

            Assert.Equal(0d, step.MoveX);
            Assert.Equal(SkillSlot.Ranged, step.Skill);
        }

        [Fact]
        public void WhileTheChosenSkillCoolsDownThePlainAttackIsUsedFromClose()
        {
            AutoStep far = AutoFight.Decide(0d, 1, 4.5d, Plain, Ranged, false, Auto);
            AutoStep near = AutoFight.Decide(0d, 1, 0.5d, Plain, Ranged, false, Auto);

            Assert.Equal(1d, far.MoveX);
            Assert.Null(far.Skill);
            Assert.Equal(SkillSlot.Attack, near.Skill);
        }

        [Fact]
        public void TheVillageAndTheHouseHaveTheirNpcs()
        {
            MapConfig village = config.Maps[config.MapIndex("map.village")];
            MapConfig house = config.Maps[config.MapIndex("map.house")];

            Assert.Equal(new[] { "npc.lam", "npc.vu" }, new[] { village.Npcs[0].Id, village.Npcs[1].Id });
            Assert.Equal("Sư phụ", house.Npcs[0].Name);
            Assert.Empty(config.Maps[config.MapIndex("map.test")].Npcs);
        }

        [Fact]
        public void AnNpcWithoutAGreetingIsRefused()
        {
            Assert.Throws<InvalidConfigException>(() => TestConfig.LoadWith(
                GameConfig.HouseFile,
                "\"greeting\": \"Ra sân tập đánh người rơm cho quen tay đã, con.\"",
                "\"greeting\": \" \""));
        }

        [Fact]
        public void TheSkillsFileMustSayHowAutoFightingWorks()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.SkillsFile] = files[GameConfig.SkillsFile].Replace("\"autoFight\"", "\"autoFightOff\"");

            Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }
    }
}
