using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Tests
{
    public class ResourceTests
    {
        private const int Precision = 6;
        private const double RunTilt = 1d;
        private const double WalkTilt = 0.3d;
        private const double DashCost = 25d;
        private const double SwipeUp = 1d;

        private readonly GameConfig config = TestConfig.Load();
        private readonly CollisionMap map = TestConfig.TestMap();

        private CharacterConfig Character => config.Character;

        private SkillConfig Skill(string id) => config.Skills.Single(skill => skill.Id == id);

        private static int Ticks(double seconds) => Game.Rules.Ticks.FromSeconds(seconds);

        private void Run(ResourceState resources, MovementState movement, int ticks, double moveX = 0d, bool attacking = false)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                var intent = new MovementIntent(moveX, 0d, false);
                Movement.Step(movement, intent, config.Movement, map);
                ResourceRules.Step(resources, movement, intent, attacking, Character, config.Movement);
            }
        }

        private void Dash(ResourceState resources, MovementState movement)
        {
            var intent = new MovementIntent(0d, 0d, true);
            Movement.Step(movement, intent, config.Movement, map);
            ResourceRules.Step(resources, movement, intent, false, Character, config.Movement);
        }

        private (ResourceState Resources, MovementState Movement) NewState(double stamina = 50d)
        {
            return (new ResourceState(Character), new MovementState(5d, 0d, stamina));
        }

        [Fact]
        public void StartsWithFullHealthAndEnergy()
        {
            var resources = new ResourceState(Character);

            Assert.Equal(1000, resources.Hp);
            Assert.Equal(100d, resources.Energy);
            Assert.False(resources.IsDead);
        }

        [Fact]
        public void Energy_RegeneratesFourPerSecond()
        {
            var (resources, movement) = NewState();
            ResourceRules.TrySpendEnergy(resources, 50);

            Run(resources, movement, Ticks(2d));

            Assert.Equal(58d, resources.Energy, Precision);
        }

        [Fact]
        public void Energy_StopsAtMaximum()
        {
            var (resources, movement) = NewState();
            ResourceRules.TrySpendEnergy(resources, 1);

            Run(resources, movement, Ticks(2d));

            Assert.Equal(100d, resources.Energy, Precision);
        }

        [Fact]
        public void Energy_IsNotSpentWhenTooLow()
        {
            var (resources, _) = NewState();
            ResourceRules.TrySpendEnergy(resources, 95);

            Assert.False(ResourceRules.TrySpendEnergy(resources, 8));
            Assert.Equal(5d, resources.Energy, Precision);
        }

        [Fact]
        public void Energy_RegeneratedUpToTheCostIsEnough()
        {
            var (resources, movement) = NewState();
            ResourceRules.TrySpendEnergy(resources, 100);

            Run(resources, movement, Ticks(2d));

            Assert.True(ResourceRules.TrySpendEnergy(resources, 8));
            Assert.Equal(0d, resources.Energy, Precision);
        }

        [Theory]
        [InlineData(0d, false, 20d)]
        [InlineData(WalkTilt, false, 10d)]
        [InlineData(RunTilt, false, 0d)]
        [InlineData(0d, true, 5d)]
        [InlineData(WalkTilt, true, 5d)]
        [InlineData(RunTilt, true, 5d)]
        public void Stamina_RegeneratesByActivity(double moveX, bool attacking, double perSecond)
        {
            var (resources, movement) = NewState();

            Run(resources, movement, Ticks(1d), moveX, attacking);

            Assert.Equal(50d + perSecond, movement.Stamina, Precision);
        }

        [Fact]
        public void Stamina_DoesNotRegenerateInTheAir()
        {
            var (resources, movement) = NewState();
            var swipe = new MovementIntent(0d, SwipeUp, false);
            Movement.Step(movement, swipe, config.Movement, map);
            ResourceRules.Step(resources, movement, swipe, false, Character, config.Movement);

            Run(resources, movement, Ticks(0.3d));

            Assert.False(movement.Grounded);
            Assert.Equal(50d, movement.Stamina, Precision);
        }

        [Fact]
        public void Stamina_DoesNotRegenerateInTheAirWhileAttacking()
        {
            var (resources, movement) = NewState();
            var swipe = new MovementIntent(0d, SwipeUp, false);
            Movement.Step(movement, swipe, config.Movement, map);
            ResourceRules.Step(resources, movement, swipe, true, Character, config.Movement);

            Run(resources, movement, Ticks(0.3d), attacking: true);

            Assert.Equal(50d, movement.Stamina, Precision);
        }

        [Fact]
        public void Stamina_WaitsOneSecondAfterADash()
        {
            var (resources, movement) = NewState();

            Dash(resources, movement);
            Run(resources, movement, Ticks(1d) - 1);

            Assert.Equal(25d, movement.Stamina, Precision);

            Run(resources, movement, 1);

            Assert.Equal(25d + 20d * Game.Rules.Ticks.DeltaSeconds, movement.Stamina, Precision);
        }

        [Fact]
        public void Stamina_StopsAtMaximum()
        {
            var (resources, movement) = NewState(99d);

            Run(resources, movement, Ticks(1d));

            Assert.Equal(100d, movement.Stamina, Precision);
        }

        [Fact]
        public void Damage_ReducesHealthAndStopsAtZero()
        {
            var resources = new ResourceState(Character);

            ResourceRules.TakeDamage(resources, 400);
            Assert.Equal(600, resources.Hp);

            ResourceRules.TakeDamage(resources, 900);
            Assert.Equal(0, resources.Hp);
            Assert.True(resources.IsDead);
        }

        [Fact]
        public void Damage_CannotBeNegative()
        {
            var resources = new ResourceState(Character);

            Assert.Throws<ArgumentOutOfRangeException>(() => ResourceRules.TakeDamage(resources, -1));
        }

        [Fact]
        public void Heal_RestoresFixedPartPlusTenPercentOfMaxHealth()
        {
            var resources = new ResourceState(Character);
            ResourceRules.TakeDamage(resources, 500);

            ResourceRules.Heal(resources, Skill("thuy.hoi_mau").Heal!, Character);

            Assert.Equal(700, resources.Hp);
        }

        [Fact]
        public void Heal_StopsAtMaximum()
        {
            var resources = new ResourceState(Character);
            ResourceRules.TakeDamage(resources, 100);

            ResourceRules.Heal(resources, Skill("thuy.hoi_mau").Heal!, Character);

            Assert.Equal(1000, resources.Hp);
        }

        [Theory]
        [InlineData("thuy.danh_thuong", 0, 60, 30, 15)]
        [InlineData("thuy.kiem_khi", 0, 160, 80, 40)]
        [InlineData("thuy.chem_tien", 0, 160, 80, 40)]
        [InlineData("thuy.song_kiem", 0, 130, 65, 32)]
        [InlineData("thuy.tuyet_ky", 0, 150, 75, 37)]
        [InlineData("thuy.tuyet_ky", 2, 300, 150, 75)]
        public void HitDamage_AppliesDefenseAndRoundsDown(string id, int phase, int noDefense, int defense100, int defense300)
        {
            Assert.Equal(noDefense, SkillDamage.HitDamage(Skill(id), phase, Character.Attack, 1, 0m));
            Assert.Equal(defense100, SkillDamage.HitDamage(Skill(id), phase, Character.Attack, 1, 100m));
            Assert.Equal(defense300, SkillDamage.HitDamage(Skill(id), phase, Character.Attack, 1, 300m));
        }

        [Fact]
        public void HitDamage_UsesAttackAndSkillLevel()
        {
            Assert.Equal(224, SkillDamage.HitDamage(Skill("thuy.kiem_khi"), 0, 100m, 5, 0m));
            Assert.Equal(392, SkillDamage.HitDamage(Skill("thuy.kiem_khi"), 0, 200m, 5, 0m));
        }

        [Fact]
        public void HitDamage_BasicAttackIgnoresSkillLevel()
        {
            SkillConfig skill = Skill("thuy.danh_thuong");

            Assert.Equal(
                SkillDamage.HitDamage(skill, 0, 100m, 1, 0m),
                SkillDamage.HitDamage(skill, 0, 100m, 9, 0m));
        }

        [Fact]
        public void Design_BasicAttackIsAboutOneHundredDamagePerSecond()
        {
            SkillConfig skill = Skill("thuy.danh_thuong");
            double cycle = skill.WindupSec + skill.ActivePhases.Sum(phase => phase.DurationSec) + skill.RecoverySec;

            double perSecond = SkillDamage.HitDamage(skill, 0, Character.Attack, 1, 0m) / cycle;

            Assert.Equal(100d, perSecond, Precision);
        }

        [Fact]
        public void Design_FourDamagingSkillsAreAboutNinetyThreeDamagePerSecond()
        {
            string[] ids = { "thuy.kiem_khi", "thuy.chem_tien", "thuy.song_kiem", "thuy.tuyet_ky" };

            double perSecond = ids.Sum(id => TotalDamage(Skill(id)) / Skill(id).CooldownSec);

            Assert.InRange(perSecond, 92.5d, 93.5d);
        }

        [Fact]
        public void Design_UltimateEqualsSixSecondsOfBasicAttack()
        {
            Assert.Equal(600d, TotalDamage(Skill("thuy.tuyet_ky")));
        }

        [Fact]
        public void Design_EnergyLastsAboutTwentyNineSeconds()
        {
            IEnumerable<SkillConfig> skills = config.Skills.Where(skill => skill.CooldownSec > 0d);

            double spentPerSecond = skills.Sum(skill => skill.EnergyCost / skill.CooldownSec);
            double lasts = Character.MaxEnergy / (spentPerSecond - Character.EnergyRegenPerSec);

            Assert.InRange(spentPerSecond, 7.4d, 7.6d);
            Assert.InRange(lasts, 28d, 30d);
        }

        [Fact]
        public void Design_DashEveryFiveSecondsHoldsForASixtySecondFight()
        {
            var (resources, movement) = NewState(100d);
            int dashes = 0;

            for (int second = 0; second < 60; second += 5)
            {
                Assert.True(movement.Stamina >= DashCost, $"Out of stamina at {second} seconds.");
                Dash(resources, movement);
                dashes++;
                Run(resources, movement, Ticks(5d) - 1, attacking: true);
            }

            Assert.Equal(12, dashes);
        }

        private double TotalDamage(SkillConfig skill)
        {
            int hits = Math.Max(1, skill.ActivePhases.Count);

            return Enumerable.Range(0, hits).Sum(phase => SkillDamage.HitDamage(skill, phase, Character.Attack, 1, 0m));
        }
    }
}
