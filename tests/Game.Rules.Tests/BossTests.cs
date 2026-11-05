using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Tests
{
    public class BossTests
    {
        private const int Precision = 6;
        private const double PlayerX = 5d;
        private const int MaxSkipTicks = 3000;
        private const string Slash = "boss.chem";
        private const string Sweep = "boss.quet";
        private const string Big = "boss.don_lon";

        private readonly GameConfig config = TestConfig.Load();
        private readonly CollisionMap map = TestConfig.TestMap();
        private readonly List<BossEvent> events = new List<BossEvent>();

        private static int Ticks(double seconds) => Game.Rules.Ticks.FromSeconds(seconds);

        private sealed class Fight
        {
            public Fight(GameConfig config, double playerX, double distance)
            {
                Player = new MovementState(playerX, 0d, config.Character.MaxStamina);
                Resources = new ResourceState(config.Character);
                Boss = new BossState(config.Boss, playerX + distance);
            }

            public MovementState Player { get; }
            public ResourceState Resources { get; }
            public BossState Boss { get; }
        }

        private Fight NewFight(double distance = 1.5d, double playerX = PlayerX)
        {
            return new Fight(config, playerX, distance);
        }

        private void Step(Fight fight, int ticks = 1)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                BossAi.Step(fight.Boss, fight.Player, fight.Resources, config, map, events);
            }
        }

        private IEnumerable<string> Started =>
            events.Where(item => item.Kind == BossEventKind.WarningStarted).Select(item => item.Attack.Id);

        private IEnumerable<BossEvent> Hits => events.Where(item => item.Kind == BossEventKind.PlayerHit);

        /// <summary>Runs until that many attacks have started, keeping the player alive meanwhile.</summary>
        private void SkipTo(Fight fight, int attacksStarted)
        {
            for (int tick = 0; tick < MaxSkipTicks && Started.Count() < attacksStarted; tick++)
            {
                ResourceRules.Heal(fight.Resources, config.Character.MaxHp, config.Character);
                Step(fight);
            }

            Assert.Equal(attacksStarted, Started.Count());
        }

        private void Move(Fight fight, double moveX)
        {
            Movement.Step(fight.Player, new MovementIntent(moveX, 0d, false), config.Movement, map);
        }

        private void JumpAndStep(Fight fight, int ticks)
        {
            Movement.Step(fight.Player, new MovementIntent(0d, 1d, false), config.Movement, map);
            Step(fight);

            for (int tick = 1; tick < ticks; tick++)
            {
                Movement.Step(fight.Player, new MovementIntent(0d, 0d, false), config.Movement, map);
                Step(fight);
            }
        }

        [Fact]
        public void Config_MatchesTheSpec()
        {
            BossConfig boss = config.Boss;

            Assert.Equal(5000, boss.MaxHp);
            Assert.Equal(0m, boss.Defense);
            Assert.Equal(2d, boss.Width);
            Assert.Equal(2.5d, boss.Height);
            Assert.Equal(1.5d, boss.MoveSpeed);
            Assert.Equal(new[] { Slash, Slash, Sweep, Slash, Big }, boss.Cycle.Select(attack => attack.Id));
        }

        [Theory]
        [InlineData(Slash, 0.6d, 2.5d, 1.5d, 120, 0.8d)]
        [InlineData(Sweep, 0.8d, 6d, 1d, 250, 1.2d)]
        [InlineData(Big, 1d, 3d, 20d, 400, 2d)]
        public void Config_AttackNumbersMatchTheSpec(string id, double warning, double width, double height, int damage, double rest)
        {
            BossAttackConfig attack = config.Boss.Cycle.First(item => item.Id == id);

            Assert.False(string.IsNullOrWhiteSpace(attack.Name));
            Assert.Equal(warning, attack.WarningSec);
            Assert.Equal(width, attack.Width);
            Assert.Equal(height, attack.Height);
            Assert.Equal(damage, attack.Damage);
            Assert.Equal(rest, attack.RestSec);
        }

        [Fact]
        public void Fight_StartsEightUnitsApart()
        {
            var boss = new BossState(config.Boss, BossAi.StartX(config.Boss, PlayerX));

            Assert.Equal(PlayerX + 8d, boss.X);
            Assert.Equal(5000, boss.Hp);
        }

        [Fact]
        public void Cycle_RepeatsSlashSlashSweepSlashBig()
        {
            Fight fight = NewFight();

            SkipTo(fight, 7);

            Assert.Equal(new[] { Slash, Slash, Sweep, Slash, Big, Slash, Slash }, Started);
        }

        [Fact]
        public void Slash_WaitsBeforeTheHitExactlyAsLongAsTheWarning()
        {
            Fight fight = NewFight();

            Step(fight);
            Assert.Equal(BossPhase.Warning, fight.Boss.Phase);

            Step(fight, Ticks(0.6d) - 1);
            Assert.Empty(Hits);
            Assert.Equal(1000, fight.Resources.Hp);

            Step(fight);
            Assert.Equal(880, fight.Resources.Hp);
            Assert.Equal(120, Hits.Single().Damage);
        }

        [Theory]
        [InlineData(0, 0.6d, 0.8d)]
        [InlineData(2, 0.8d, 1.4d)]
        [InlineData(4, 1d, 2d)]
        public void Attack_RestsTheSpecTimeBeforeTheNextWarning(int attackIndex, double warning, double rest)
        {
            Fight fight = NewFight();
            SkipTo(fight, attackIndex + 1);

            Step(fight, Ticks(warning) + Ticks(rest) - 1);
            Assert.Equal(attackIndex + 1, Started.Count());

            Step(fight);
            Assert.Equal(attackIndex + 2, Started.Count());
        }

        [Theory]
        [InlineData(0, 0.6d, 0.6d)]
        [InlineData(2, 0.8d, 1.1d)]
        [InlineData(4, 1d, 1.5d)]
        public void Rest_IsAQuarterShorterBelowHalfHealth(int attackIndex, double warning, double rest)
        {
            Fight fight = NewFight();
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.Attack, 2501) });
            SkipTo(fight, attackIndex + 1);

            Step(fight, Ticks(warning) + Ticks(rest) - 1);
            Assert.Equal(attackIndex + 1, Started.Count());

            Step(fight);
            Assert.Equal(attackIndex + 2, Started.Count());
        }

        [Fact]
        public void Rest_StaysNormalAtExactlyHalfHealth()
        {
            Fight fight = NewFight();
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.Attack, 2500) });
            SkipTo(fight, 1);

            Step(fight, Ticks(0.6d) + Ticks(0.8d) - 1);

            Assert.Single(Started);
        }

        [Fact]
        public void Boss_WalksInBeforeTheSlash()
        {
            Fight fight = NewFight(8d);

            Step(fight, 30);

            Assert.Empty(Started);
            Assert.Equal(PlayerX + 6.5d, fight.Boss.X, Precision);
            Assert.Equal(-1, fight.Boss.Facing);
        }

        [Fact]
        public void Boss_StartsTheSlashAtTwoUnits()
        {
            Fight fight = NewFight(3.5d);

            Step(fight, Ticks(1d));
            Assert.Empty(Started);

            Step(fight);
            Assert.Equal(new[] { Slash }, Started);
            Assert.Equal(PlayerX + 2d, fight.Boss.X, Precision);
        }

        [Fact]
        public void Boss_FollowsThePlayerToTheLeftAndRight()
        {
            Fight fight = NewFight(-5d, 15d);

            Step(fight);

            Assert.Equal(1, fight.Boss.Facing);
        }

        [Fact]
        public void Boss_StopsAtTheWall()
        {
            Fight fight = NewFight(-1d, 2d);

            BossAi.Step(fight.Boss, new MovementState(-10d, 0d, 100d), fight.Resources, config, map, events);

            Assert.Equal(1d, fight.Boss.X, Precision);
        }

        [Fact]
        public void Sweep_NeedsNoApproach()
        {
            Fight fight = NewFight();
            SkipTo(fight, 2);
            Step(fight, Ticks(0.6d) + Ticks(0.8d) - 1);
            double bossX = fight.Boss.X;
            var far = new MovementState(bossX + 15d, 0d, 100d);

            BossAi.Step(fight.Boss, far, fight.Resources, config, map, events);

            Assert.Equal(Sweep, Started.Last());
            Assert.Equal(bossX, fight.Boss.X, Precision);
        }

        [Fact]
        public void Warning_DoesNotTurnTheBoss()
        {
            Fight fight = NewFight();

            Step(fight);

            for (int tick = 0; tick < Ticks(0.5d); tick++)
            {
                Move(fight, 1d);
                Step(fight);
            }

            Assert.True(fight.Player.X > fight.Boss.X);
            Assert.Equal(-1, fight.Boss.Facing);
            Assert.Empty(Hits);
        }

        [Fact]
        public void Slash_MissesAPlayerWhoRanBehind()
        {
            Fight fight = NewFight();

            Step(fight);

            for (int tick = 0; tick < Ticks(0.6d) + 1; tick++)
            {
                Move(fight, 1d);
                Step(fight);
            }

            Assert.Empty(Hits);
        }

        [Fact]
        public void Slash_ReachesTwoAndAHalfUnits()
        {
            Fight fight = NewFight(2d);
            Step(fight, Ticks(0.6d) + 1);

            Assert.Single(Hits);
        }

        [Fact]
        public void Slash_MissesAPlayerAboveItsHeight()
        {
            Fight fight = NewFight();
            Step(fight);
            var high = new MovementState(PlayerX, 1.6d, 100d);

            for (int tick = 0; tick < Ticks(0.6d) + 1; tick++)
            {
                BossAi.Step(fight.Boss, high, fight.Resources, config, map, events);
            }

            Assert.Empty(Hits);
        }

        [Fact]
        public void Sweep_HitsTheFacingSideFirst()
        {
            Fight fight = NewFight(2d);
            SkipTo(fight, 3);
            Assert.Equal(Sweep, Started.Last());
            int hitsBefore = Hits.Count();

            Step(fight, Ticks(0.8d));

            Assert.Equal(hitsBefore + 1, Hits.Count());
            Assert.Equal(250, Hits.Last().Damage);
        }

        [Fact]
        public void Sweep_HitsTheOtherSideAfterTheDelay()
        {
            Fight fight = NewFight(2d);
            SkipTo(fight, 3);
            int hitsBefore = Hits.Count();
            var behind = new MovementState(fight.Boss.X + 5d, 0d, 100d);

            for (int tick = 0; tick < Ticks(0.8d) + Ticks(0.2d) - 1; tick++)
            {
                BossAi.Step(fight.Boss, behind, fight.Resources, config, map, events);
            }

            Assert.Equal(hitsBefore, Hits.Count());

            BossAi.Step(fight.Boss, behind, fight.Resources, config, map, events);

            Assert.Equal(hitsBefore + 1, Hits.Count());
        }

        [Fact]
        public void Sweep_HitsAtMostOnce()
        {
            Fight fight = NewFight(0d);
            SkipTo(fight, 3);
            int hitsBefore = Hits.Count();

            Step(fight, Ticks(0.8d) + Ticks(0.2d));

            Assert.Equal(hitsBefore + 1, Hits.Count());
        }

        [Fact]
        public void Sweep_MissesAPlayerWhoJumped()
        {
            Fight fight = NewFight(2d);
            SkipTo(fight, 3);
            int hitsBefore = Hits.Count();

            Step(fight, Ticks(0.8d) - Ticks(0.3d));
            JumpAndStep(fight, Ticks(0.3d) + Ticks(0.2d));

            Assert.Equal(hitsBefore, Hits.Count());
        }

        [Fact]
        public void Sweep_ReachesSixUnitsToBothSides()
        {
            Fight fight = NewFight();
            SkipTo(fight, 3);

            CombatBox zone = BossAi.WarningZone(fight.Boss, config.Boss)!.Value;

            Assert.Equal(fight.Boss.X - 6d, zone.MinX, Precision);
            Assert.Equal(fight.Boss.X + 6d, zone.MaxX, Precision);
            Assert.Equal(1d, zone.MaxY, Precision);
        }

        [Fact]
        public void Big_LocksWhereThePlayerStoodWhenTheWarningBegan()
        {
            Fight fight = NewFight();
            SkipTo(fight, 5);
            Assert.Equal(Big, Started.Last());
            int hitsBefore = Hits.Count();
            double locked = fight.Player.X;

            for (int tick = 0; tick < Ticks(1d); tick++)
            {
                Move(fight, 1d);
                Step(fight);
            }

            Assert.True(fight.Player.X - locked > 2d);
            Assert.Equal(hitsBefore, Hits.Count());
        }

        [Fact]
        public void Big_HitsAPlayerWhoStayed()
        {
            Fight fight = NewFight();
            SkipTo(fight, 5);
            int hitsBefore = Hits.Count();

            Step(fight, Ticks(1d));

            Assert.Equal(hitsBefore + 1, Hits.Count());
            Assert.Equal(400, Hits.Last().Damage);
        }

        [Fact]
        public void Big_HitsAPlayerInTheAir()
        {
            Fight fight = NewFight();
            SkipTo(fight, 5);
            int hitsBefore = Hits.Count();
            var high = new MovementState(PlayerX, 2d, 100d);

            for (int tick = 0; tick < Ticks(1d); tick++)
            {
                BossAi.Step(fight.Boss, high, fight.Resources, config, map, events);
            }

            Assert.Equal(hitsBefore + 1, Hits.Count());
        }

        [Fact]
        public void WarningZone_ExistsOnlyDuringTheWarning()
        {
            Fight fight = NewFight();
            Assert.Null(BossAi.WarningZone(fight.Boss, config.Boss));

            Step(fight);
            Assert.NotNull(BossAi.WarningZone(fight.Boss, config.Boss));

            Step(fight, Ticks(0.6d));
            Assert.Null(BossAi.WarningZone(fight.Boss, config.Boss));
        }

        [Fact]
        public void WarningZone_MatchesTheSlash()
        {
            Fight fight = NewFight();

            Step(fight);
            CombatBox zone = BossAi.WarningZone(fight.Boss, config.Boss)!.Value;

            Assert.Equal(fight.Boss.X - 2.5d, zone.MinX, Precision);
            Assert.Equal(fight.Boss.X, zone.MaxX, Precision);
            Assert.Equal(1.5d, zone.MaxY, Precision);
        }

        [Fact]
        public void PlayerDefense_ReducesBossDamage()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.CharacterFile] = files[GameConfig.CharacterFile].Replace("\"defense\": 0", "\"defense\": 100");
            GameConfig tough = GameConfig.Parse(files);
            var resources = new ResourceState(tough.Character);
            var player = new MovementState(PlayerX, 0d, 100d);
            var boss = new BossState(tough.Boss, PlayerX + 1.5d);

            for (int tick = 0; tick < Ticks(0.6d) + 1; tick++)
            {
                BossAi.Step(boss, player, resources, tough, map, events);
            }

            Assert.Equal(60, Hits.Single().Damage);
            Assert.Equal(940, resources.Hp);
        }

        [Fact]
        public void Boss_StopsWhenThePlayerIsDead()
        {
            Fight fight = NewFight();
            ResourceRules.TakeDamage(fight.Resources, 1000);

            Step(fight, 60);

            Assert.Empty(events);
        }

        [Fact]
        public void Boss_StopsWhenItDies()
        {
            Fight fight = NewFight();
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.Attack, 9999) });

            Step(fight, 60);

            Assert.True(fight.Boss.IsDead);
            Assert.Equal(0, fight.Boss.Hp);
            Assert.Empty(events);
        }

        [Fact]
        public void Skills_DamageTheBossThroughCombat()
        {
            var combat = new CombatState(config.Potion);
            var player = new MovementState(PlayerX, 0d, 100d);
            var resources = new ResourceState(config.Character);
            var boss = new BossState(config.Boss, PlayerX + 1.5d);
            var hits = new List<CombatEvent>();
            var move = new MovementIntent(0d, 0d, false);

            for (int tick = 0; tick < Ticks(0.6d); tick++)
            {
                var intent = new CombatIntent(tick == 0 ? SkillSlot.Attack : (SkillSlot?)null);
                Combat.Step(combat, player, resources, move, intent, config, map, BossAi.TargetOf(boss, config.Boss), hits);
                BossAi.ApplyHits(boss, hits);
                hits.Clear();
            }

            Assert.Equal(4940, boss.Hp);
        }

        [Fact]
        public void SongKiem_SlowsOnlyWalking()
        {
            Fight fight = NewFight(8d);
            var slow = new SkillSlow { Percent = 20d, DurationSec = 2d };
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.OptionB, 130, slow) });

            Step(fight, 30);

            Assert.Equal(PlayerX + 8d - 1.2d, fight.Boss.X, Precision);
        }

        [Fact]
        public void SongKiem_SlowEndsAfterTwoSeconds()
        {
            Fight fight = NewFight(12d);
            var slow = new SkillSlow { Percent = 20d, DurationSec = 2d };
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.OptionB, 130, slow) });

            Step(fight, 90);

            Assert.Equal(PlayerX + 12d - 2.4d - 1.5d, fight.Boss.X, 1);
        }

        [Fact]
        public void SongKiem_DoesNotChangeTheWarningOrRest()
        {
            Fight fight = NewFight();
            var slow = new SkillSlow { Percent = 20d, DurationSec = 2d };
            BossAi.ApplyHits(fight.Boss, new[] { new CombatEvent(CombatEventKind.SkillHit, SkillSlot.OptionB, 130, slow) });

            Step(fight, 1 + Ticks(0.6d) + Ticks(0.8d) - 1);
            Assert.Single(Started);

            Step(fight);
            Assert.Equal(2, Started.Count());
        }
    }
}
