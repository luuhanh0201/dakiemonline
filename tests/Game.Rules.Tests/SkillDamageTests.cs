using System;
using Game.Rules;
using Xunit;

namespace Game.Rules.Tests
{
    public class SkillDamageTests
    {
        [Theory]
        [InlineData(1, 160)]
        [InlineData(5, 224)]
        public void BeforeDefense_MatchesDesignExample(int level, int expected)
        {
            var damage = SkillDamage.BeforeDefense(40m, 100m, 1.2m, 10m, level);

            Assert.Equal(expected, damage);
        }

        [Fact]
        public void BeforeDefense_ScalesTheAttackPartWithLevel()
        {
            var levelOne = SkillDamage.BeforeDefense(40m, 200m, 1.2m, 10m, 1);
            var levelFive = SkillDamage.BeforeDefense(40m, 200m, 1.2m, 10m, 5);

            Assert.Equal(280m, levelOne);
            Assert.Equal(392m, levelFive);
        }

        [Fact]
        public void BeforeDefense_RejectsLevelBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SkillDamage.BeforeDefense(40m, 100m, 1.2m, 10m, 0));
        }

        [Theory]
        [InlineData(0, 160)]
        [InlineData(100, 80)]
        [InlineData(300, 40)]
        [InlineData(200, 53)]
        public void AfterDefense_AppliesFormulaAndRoundsDown(int defense, int expected)
        {
            Assert.Equal(expected, SkillDamage.AfterDefense(160m, defense));
        }

        [Fact]
        public void AfterDefense_RejectsNegativeDefense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SkillDamage.AfterDefense(160m, -1m));
        }
    }
}
