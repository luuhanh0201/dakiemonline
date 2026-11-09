using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;

namespace Game.Rules.Tests
{
    public class SimulationTests
    {
        private const double PlayerX = 5d;
        private const int TwoMinutesTicks = 3600;

        private static readonly SkillSlot[] Rotation =
        {
            SkillSlot.Ultimate,
            SkillSlot.OptionB,
            SkillSlot.Ranged,
            SkillSlot.Attack,
        };

        private readonly GameConfig config = TestConfig.Load();
        private readonly ITestOutputHelper output;

        public SimulationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly MovementIntent Still = new MovementIntent(0d, 0d, false);

        private Simulation NewFight() => new Simulation(config, PlayerX);

        private static void Idle(Simulation fight, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                fight.Step(Still, default);
            }
        }

        private SkillSlot? NextSkill(Simulation fight)
        {
            foreach (SkillSlot slot in Rotation)
            {
                if (Combat.IsSkillReady(fight.Combat, fight.Resources, config.Skill(slot)))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>Stands next to the boss and casts everything as soon as it is ready.</summary>
        private void PlayWithoutDodging(Simulation fight)
        {
            for (int tick = 0; tick < TwoMinutesTicks && fight.Result == MatchResult.Playing; tick++)
            {
                double distance = fight.Boss.X - fight.Player.X;
                double moveX = System.Math.Abs(distance) > 1.4d ? System.Math.Sign(distance) : 0d;
                bool hurt = fight.Resources.Hp < 500;
                var intent = new CombatIntent(fight.Combat.IsAttacking ? null : NextSkill(fight), hurt);

                fight.Step(new MovementIntent(moveX, 0d, false), intent);
                fight.ClearEvents();
            }
        }

        [Fact]
        public void HeldBoss_NeitherMovesNorAttacks()
        {
            Simulation fight = NewFight();
            fight.BossHeld = true;
            double startX = fight.Boss.X;
            var walk = new MovementIntent(1d, 0d, false);

            for (int tick = 0; tick < Ticks.FromSeconds(30d); tick++)
            {
                fight.Step(walk, default);
            }

            Assert.Equal(startX, fight.Boss.X);
            Assert.Equal(BossPhase.Approach, fight.Boss.Phase);
            Assert.Equal(0, fight.HitsTaken);
            Assert.Equal(MatchResult.Playing, fight.Result);
        }

        [Fact]
        public void HeldBoss_CanStillBeHitAndMovesAgainWhenReleased()
        {
            Simulation fight = NewFight();
            fight.BossHeld = true;

            while (fight.Boss.X - fight.Player.X > 1d)
            {
                fight.Step(new MovementIntent(1d, 0d, false), default);
            }

            int fullHp = fight.Boss.Hp;

            for (int tick = 0; tick < Ticks.FromSeconds(1d); tick++)
            {
                fight.Step(default, new CombatIntent(tick == 0 ? SkillSlot.Attack : (SkillSlot?)null));
            }

            Assert.True(fight.Boss.Hp < fullHp);

            fight.BossHeld = false;
            fight.Step(default, default);
            Assert.Equal(BossPhase.Warning, fight.Boss.Phase);
        }

        [Fact]
        public void Fight_StartsEightUnitsApartAndUndecided()
        {
            Simulation fight = NewFight();

            Assert.Equal(8d, fight.Boss.X - fight.Player.X);
            Assert.Equal(MatchResult.Playing, fight.Result);
            Assert.Equal(0d, fight.SecondsPlayed);
        }

        [Fact]
        public void Fight_CountsTime()
        {
            Simulation fight = NewFight();

            Idle(fight, 90);

            Assert.Equal(3d, fight.SecondsPlayed, 6);
        }

        [Fact]
        public void Fight_IsLostWhenThePlayerDiesAndNamesTheAttack()
        {
            Simulation fight = NewFight();

            Idle(fight, TwoMinutesTicks);

            Assert.Equal(MatchResult.Lost, fight.Result);
            Assert.Equal(0, fight.Resources.Hp);
            Assert.Equal("boss.don_lon", fight.KilledBy!.Id);
            Assert.Equal(5, fight.HitsTaken);
        }

        [Fact]
        public void Fight_StopsWhenItEnds()
        {
            Simulation fight = NewFight();
            Idle(fight, TwoMinutesTicks);
            int ticks = fight.TicksPlayed;

            Idle(fight, 30);

            Assert.Equal(ticks, fight.TicksPlayed);
        }

        [Fact]
        public void Fight_CountsPotionsUsed()
        {
            Simulation fight = NewFight();

            fight.Step(Still, new CombatIntent(null, true));

            Assert.Equal(1, fight.PotionsUsed);
        }

        [Fact]
        public void Fight_KeepsEventsUntilCleared()
        {
            Simulation fight = NewFight();

            fight.Step(Still, new CombatIntent(SkillSlot.Attack));
            fight.Step(Still, default);
            Assert.Contains(fight.CombatEvents, item => item.Kind == CombatEventKind.SkillStarted);

            fight.ClearEvents();

            Assert.Empty(fight.CombatEvents);
        }

        [Fact]
        public void Fight_RepeatsExactlyFromTheSameIntents()
        {
            Simulation first = NewFight();
            Simulation second = NewFight();

            PlayWithoutDodging(first);
            PlayWithoutDodging(second);

            Assert.Equal(first.Result, second.Result);
            Assert.Equal(first.TicksPlayed, second.TicksPlayed);
            Assert.Equal(first.Resources.Hp, second.Resources.Hp);
            Assert.Equal(first.Boss.Hp, second.Boss.Hp);
        }

        [Fact]
        public void Fight_IsLostWhenThePlayerNeverDodges()
        {
            Simulation fight = NewFight();

            PlayWithoutDodging(fight);

            output.WriteLine(Summary(fight));
            Assert.Equal(MatchResult.Lost, fight.Result);
            Assert.True(fight.Boss.Hp > 0);
        }

        [Fact]
        public void Fight_DodgingPlayerWinsInThirtyToSixtySeconds()
        {
            Simulation fight = NewFight();

            PlayDodging(fight);

            output.WriteLine(Summary(fight));
            Assert.Equal(MatchResult.Won, fight.Result);
            Assert.Null(fight.KilledBy);
            Assert.InRange(fight.SecondsPlayed, 30d, 60d);
        }

        private static string Summary(Simulation fight)
        {
            return $"{fight.Result} {fight.SecondsPlayed:0.0}s boss {fight.Boss.Hp} hp {fight.Resources.Hp} hits {fight.HitsTaken} potions {fight.PotionsUsed}";
        }

        /// <summary>Attacks during the boss's rest and uses the intended dodge for each warning.</summary>
        private void PlayDodging(Simulation fight)
        {
            int warningTicks = 0;

            for (int tick = 0; tick < TwoMinutesTicks && fight.Result == MatchResult.Playing; tick++)
            {
                CombatBox? zone = BossAi.WarningZone(fight.Boss, config.Boss);
                warningTicks = zone == null ? 0 : warningTicks + 1;

                MovementIntent move = zone == null
                    ? ChaseBoss(fight)
                    : Dodge(fight, zone.Value, warningTicks);
                bool heal = fight.Resources.Hp < 700;
                SkillSlot? skill = fight.Combat.IsAttacking ? null : ChooseSkill(fight, zone == null);

                fight.Step(move, new CombatIntent(skill, heal));
                fight.ClearEvents();
            }
        }

        private static MovementIntent ChaseBoss(Simulation fight)
        {
            double distance = fight.Boss.X - fight.Player.X;
            return new MovementIntent(System.Math.Abs(distance) > 1.4d ? System.Math.Sign(distance) : 0d, 0d, false);
        }

        private MovementIntent Dodge(Simulation fight, in CombatBox zone, int warningTicks)
        {
            BossAttackConfig attack = BossAi.CurrentAttack(fight.Boss, config.Boss);

            if (attack.Shape == BossAttackShape.BothSides)
            {
                int ticksLeft = Ticks.FromSeconds(attack.WarningSec) - warningTicks;
                return new MovementIntent(0d, ticksLeft == 8 ? 1d : 0d, false);
            }

            double middle = (zone.MinX + zone.MaxX) / 2d;
            double away = fight.Player.X >= middle ? 1d : -1d;

            bool blockedByWall = (away > 0d && fight.Player.X > 28d) || (away < 0d && fight.Player.X < 2d);
            return new MovementIntent(blockedByWall ? -away : away, 0d, false);
        }

        private SkillSlot? ChooseSkill(Simulation fight, bool bossIsResting)
        {
            foreach (SkillSlot slot in Rotation)
            {
                SkillConfig skill = config.Skill(slot);

                if ((bossIsResting || skill.MoveSpeedPercent > 0d) && Combat.IsSkillReady(fight.Combat, fight.Resources, skill))
                {
                    return slot;
                }
            }

            return null;
        }
    }
}
