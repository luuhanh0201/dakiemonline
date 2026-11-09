using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Tests
{
    public class CombatTests
    {
        private const int Precision = 6;
        private const double StartX = 10d;
        private const double RunTilt = 1d;

        private readonly GameConfig config = TestConfig.Load();
        private readonly CollisionMap map = TestConfig.TestMap();
        private readonly List<CombatEvent> events = new List<CombatEvent>();

        private static CombatTarget BossAt(double minX, double width = 2d, decimal defense = 0m)
        {
            return new CombatTarget(new CombatBox(minX, 0d, minX + width, 2.5d), defense);
        }

        private static int Ticks(double seconds) => Game.Rules.Ticks.FromSeconds(seconds);

        private sealed class Fighter
        {
            public Fighter(GameConfig config, double x = StartX)
            {
                Combat = new CombatState(config.Potion);
                Movement = new MovementState(x, 0d, config.Character.MaxStamina);
                Resources = new ResourceState(config.Character);
            }

            public CombatState Combat { get; }
            public MovementState Movement { get; init; }
            public ResourceState Resources { get; }
        }

        private Fighter NewFighter(double x = StartX) => new Fighter(config, x);

        private void Tick(
            Fighter fighter,
            SkillSlot? press = null,
            bool potion = false,
            bool dash = false,
            double moveX = 0d,
            double moveY = 0d,
            CombatTarget? target = null)
        {
            var moveIntent = new MovementIntent(moveX, moveY, dash);
            var intent = new CombatIntent(press, potion);
            Combat.Step(fighter.Combat, fighter.Movement, fighter.Resources, moveIntent, intent, config, map, target, events);
        }

        private void Wait(Fighter fighter, int ticks, double moveX = 0d, CombatTarget? target = null)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                Tick(fighter, moveX: moveX, target: target);
            }
        }

        private void Cast(Fighter fighter, SkillSlot slot, CombatTarget? target = null)
        {
            Tick(fighter, slot, target: target);
        }

        private void CastToEnd(Fighter fighter, SkillSlot slot, CombatTarget? target = null)
        {
            Cast(fighter, slot, target);
            Wait(fighter, Ticks(TotalSeconds(slot)), target: target);
        }

        private double TotalSeconds(SkillSlot slot)
        {
            SkillConfig skill = config.Skill(slot);
            return skill.WindupSec + skill.ActivePhases.Sum(phase => phase.DurationSec) + skill.RecoverySec;
        }

        private IEnumerable<CombatEvent> Hits => events.Where(item => item.Kind == CombatEventKind.SkillHit);

        private void Jump(Fighter fighter)
        {
            Tick(fighter, moveY: 1d);
            Tick(fighter);
        }

        [Theory]
        [InlineData(SkillSlot.Attack, 0.45d)]
        [InlineData(SkillSlot.Ranged, 0.4d)]
        [InlineData(SkillSlot.Support, 0.5d)]
        [InlineData(SkillSlot.OptionA, 0.6d)]
        [InlineData(SkillSlot.OptionB, 0.8d)]
        [InlineData(SkillSlot.Ultimate, 1.5d)]
        public void Skill_LastsExactlyAsLongAsTheSpec(SkillSlot slot, double seconds)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);
            Wait(fighter, Ticks(seconds) - 2);
            Assert.NotNull(fighter.Combat.Casting);

            Wait(fighter, 1);
            Assert.Null(fighter.Combat.Casting);
        }

        [Theory]
        [InlineData(SkillSlot.Attack, 0.15d, 0.25d)]
        [InlineData(SkillSlot.OptionA, 0.1d, 0.3d)]
        [InlineData(SkillSlot.OptionB, 0.25d, 0.55d)]
        [InlineData(SkillSlot.Ultimate, 0.2d, 1.2d)]
        public void Skill_PhasesChangeAtTheSpecTimes(SkillSlot slot, double windupEnd, double activeEnd)
        {
            Fighter fighter = NewFighter();
            var phases = new List<CastPhase>();

            Cast(fighter, slot);
            phases.Add(fighter.Combat.Phase);

            while (fighter.Combat.Casting != null)
            {
                Tick(fighter);
                phases.Add(fighter.Combat.Phase);
            }

            // Entry n is the phase after n + 1 ticks have run.
            int firstActive = phases.IndexOf(CastPhase.Active) + 1;
            int firstRecovery = phases.IndexOf(CastPhase.Recovery) + 1;

            Assert.Equal(CastPhase.Windup, phases[0]);
            Assert.Equal(Ticks(windupEnd), firstActive);
            Assert.Equal(Ticks(activeEnd), firstRecovery);
        }

        [Theory]
        [InlineData(SkillSlot.Attack, 0, 0d)]
        [InlineData(SkillSlot.Ranged, 8, 4d)]
        [InlineData(SkillSlot.Support, 25, 20d)]
        [InlineData(SkillSlot.OptionA, 12, 7d)]
        [InlineData(SkillSlot.OptionB, 15, 10d)]
        [InlineData(SkillSlot.Ultimate, 35, 35d)]
        public void Skill_SpendsEnergyAndStartsCooldownWhenItStarts(SkillSlot slot, int energy, double cooldown)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);

            double regenerated = config.Character.EnergyRegenPerSec * Game.Rules.Ticks.DeltaSeconds;
            Assert.Equal(CastPhase.Windup, fighter.Combat.Phase);
            Assert.Equal(Math.Min(100d, 100d - energy + regenerated), fighter.Resources.Energy, Precision);
            Assert.Equal(cooldown, fighter.Combat.CooldownSecondsLeft(slot), Precision);
        }

        [Fact]
        public void Skill_IsRefusedDuringCooldown()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.Ranged);
            Wait(fighter, 30);
            double energy = fighter.Resources.Energy;

            Cast(fighter, SkillSlot.Ranged);

            Assert.Null(fighter.Combat.Casting);
            Assert.True(fighter.Resources.Energy >= energy);
        }

        [Fact]
        public void Skill_WorksAgainWhenCooldownEnds()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Ranged);
            Wait(fighter, Ticks(4d) - 1);

            Cast(fighter, SkillSlot.Ranged);

            Assert.NotNull(fighter.Combat.Casting);
        }

        [Fact]
        public void Skill_IsRefusedWithoutEnoughEnergy()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TrySpendEnergy(fighter.Resources, 95);

            Cast(fighter, SkillSlot.Ranged);

            Assert.Null(fighter.Combat.Casting);
            Assert.Equal(0d, fighter.Combat.CooldownSecondsLeft(SkillSlot.Ranged));
        }

        [Fact]
        public void Skill_IsRefusedWhileAnotherSkillRuns()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Ultimate);
            Wait(fighter, Ticks(1d));
            Cast(fighter, SkillSlot.OptionA);

            Assert.Equal(SkillSlot.Ultimate, fighter.Combat.Casting!.Slot);
        }

        [Fact]
        public void Skill_PressedJustBeforeTheLastOneEndsStillStarts()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack);
            Wait(fighter, Ticks(0.45d) - 1 - 3);
            Cast(fighter, SkillSlot.Ranged);
            Assert.Equal(SkillSlot.Attack, fighter.Combat.Casting!.Slot);

            Wait(fighter, 3);

            Assert.Equal(SkillSlot.Ranged, fighter.Combat.Casting!.Slot);
        }

        [Fact]
        public void Skill_PressedTooEarlyIsForgotten()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Ultimate);
            Cast(fighter, SkillSlot.Ranged);
            Wait(fighter, Ticks(1.5d));

            Assert.Null(fighter.Combat.Casting);
        }

        [Fact]
        public void Skill_DeadCharacterCannotCast()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 1000);

            Cast(fighter, SkillSlot.Attack);

            Assert.Null(fighter.Combat.Casting);
        }

        [Fact]
        public void BasicAttack_HitsOnceForSixtyWhenTheActivePhaseOpens()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            Cast(fighter, SkillSlot.Attack, boss);
            Wait(fighter, Ticks(0.15d) - 1, target: boss);
            Assert.Empty(Hits);

            Wait(fighter, 1, target: boss);
            Assert.Equal(60, Hits.Single().Amount);

            Wait(fighter, Ticks(0.45d), target: boss);
            Assert.Single(Hits);
        }

        [Fact]
        public void Combo_ThirdHitDealsMoreThenTheChainStartsAgain()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            for (int press = 0; press < 4; press++)
            {
                CastToEnd(fighter, SkillSlot.Attack, boss);
            }

            Assert.Equal(new[] { 60, 60, 78, 60 }, Hits.Select(hit => hit.Amount));
        }

        [Fact]
        public void Combo_ReportsItsPlaceWhileCasting()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack);
            Assert.Equal(0, fighter.Combat.ComboStep);
            Wait(fighter, Ticks(TotalSeconds(SkillSlot.Attack)));

            Cast(fighter, SkillSlot.Attack);
            Assert.Equal(1, fighter.Combat.ComboStep);
        }

        [Fact]
        public void Combo_PauseLongerThanTheWindowRestartsTheChain()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            CastToEnd(fighter, SkillSlot.Attack, boss);
            CastToEnd(fighter, SkillSlot.Attack, boss);
            Wait(fighter, Ticks(config.Skill(SkillSlot.Attack).Combo!.WindowSec) + 1);
            CastToEnd(fighter, SkillSlot.Attack, boss);

            Assert.Equal(new[] { 60, 60, 60 }, Hits.Select(hit => hit.Amount));
        }

        [Fact]
        public void Combo_PressInsideTheWindowContinuesTheChain()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            CastToEnd(fighter, SkillSlot.Attack, boss);
            CastToEnd(fighter, SkillSlot.Attack, boss);
            Wait(fighter, Ticks(config.Skill(SkillSlot.Attack).Combo!.WindowSec) / 2);
            CastToEnd(fighter, SkillSlot.Attack, boss);

            Assert.Equal(new[] { 60, 60, 78 }, Hits.Select(hit => hit.Amount));
        }

        [Fact]
        public void Combo_AnotherSkillRestartsTheChain()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            CastToEnd(fighter, SkillSlot.Attack, boss);
            CastToEnd(fighter, SkillSlot.Attack, boss);
            CastToEnd(fighter, SkillSlot.Ranged);
            events.Clear();
            CastToEnd(fighter, SkillSlot.Attack, boss);

            Assert.Equal(60, Hits.Single(hit => hit.Slot == SkillSlot.Attack).Amount);
        }

        [Fact]
        public void BasicAttack_MissesBeyondItsReach()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.Attack, BossAt(StartX + 1.6d));

            Assert.Empty(Hits);
        }

        [Fact]
        public void BasicAttack_MissesATargetAboveItsHeight()
        {
            Fighter fighter = NewFighter();
            var above = new CombatTarget(new CombatBox(StartX, 1.1d, StartX + 1d, 3d), 0m);

            CastToEnd(fighter, SkillSlot.Attack, above);

            Assert.Empty(Hits);
        }

        [Fact]
        public void Defense_ReducesHitDamage()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.Attack, BossAt(StartX + 1d, defense: 100m));

            Assert.Equal(30, Hits.Single().Amount);
        }

        [Fact]
        public void Ultimate_HitsThreeTimesTwentyFiveTwentyFiveFifty()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.Ultimate, BossAt(StartX + 1d));

            Assert.Equal(new[] { 150, 150, 300 }, Hits.Select(hit => hit.Amount));
        }

        [Fact]
        public void Ultimate_ReportsWhichHitIsPlaying()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Ultimate);
            Wait(fighter, Ticks(0.2d));
            Assert.Equal(3, fighter.Combat.ActiveHitCount);
            Assert.Equal(0, fighter.Combat.ActiveHitIndex);

            Wait(fighter, Ticks(0.3d));
            Assert.Equal(1, fighter.Combat.ActiveHitIndex);
            Assert.InRange(fighter.Combat.ActiveHitProgress, 0.1d, 0.2d);

            Wait(fighter, Ticks(0.3d) + Ticks(0.2d));
            Assert.Equal(2, fighter.Combat.ActiveHitIndex);
            Assert.InRange(fighter.Combat.ActiveHitProgress, 0.5d, 0.6d);
        }

        [Theory]
        [InlineData(0.5d, 0)]
        [InlineData(0.8d, 1)]
        [InlineData(1.2d, 2)]
        public void Ultimate_HitNamesItsOwnPhaseEvenOnThatPhasesLastTick(double phaseEndSeconds, int expectedHit)
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            Cast(fighter, SkillSlot.Ultimate);
            Wait(fighter, Ticks(phaseEndSeconds) - 2);
            Wait(fighter, 1, target: boss);

            Assert.Equal(expectedHit, Hits.Single().HitIndex);
        }

        [Fact]
        public void Ultimate_HitsOnlyInPhasesTheTargetWasInside()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            Cast(fighter, SkillSlot.Ultimate);
            Wait(fighter, Ticks(0.2d) + Ticks(0.3d) - 1);
            Wait(fighter, Ticks(1.5d), target: boss);

            Assert.Equal(new[] { 150, 300 }, Hits.Select(hit => hit.Amount));
        }

        [Fact]
        public void SongKiem_HitsWithDamageAndSlow()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.OptionB, BossAt(StartX + 3.5d));

            CombatEvent hit = Hits.Single();
            Assert.Equal(130, hit.Amount);
            Assert.Equal(20d, hit.Slow!.Percent);
            Assert.Equal(2d, hit.Slow.DurationSec);
        }

        [Fact]
        public void KiemKhi_FiresWhenWindupEndsAndHitsOnArrival()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 3d);

            Cast(fighter, SkillSlot.Ranged, boss);
            Wait(fighter, Ticks(0.2d) - 1, target: boss);
            Assert.Empty(fighter.Combat.Projectiles);

            Wait(fighter, 1, target: boss);
            Assert.Single(fighter.Combat.Projectiles);

            Wait(fighter, Ticks(0.3d) - 2, target: boss);
            Assert.Empty(Hits);

            Wait(fighter, 2, target: boss);
            Assert.Equal(160, Hits.Single().Amount);
            Assert.Empty(fighter.Combat.Projectiles);
        }

        [Fact]
        public void KiemKhi_StopsAtItsRange()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 6.1d);

            Cast(fighter, SkillSlot.Ranged);
            Wait(fighter, Ticks(0.2d) + Ticks(0.6d), target: boss);

            Assert.Empty(Hits);
            Assert.Empty(fighter.Combat.Projectiles);
        }

        [Fact]
        public void KiemKhi_KeepsFlyingAfterTheSkillEnds()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 5d);

            Cast(fighter, SkillSlot.Ranged, boss);
            Wait(fighter, Ticks(0.4d), target: boss);
            Assert.Null(fighter.Combat.Casting);
            Assert.Single(fighter.Combat.Projectiles);

            Wait(fighter, Ticks(0.5d), target: boss);
            Assert.Single(Hits);
        }

        [Fact]
        public void HoiMau_HealsTwoHundredWhenWindupEnds()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 500);

            Cast(fighter, SkillSlot.Support);
            Wait(fighter, Ticks(0.3d) - 1);
            Assert.Equal(500, fighter.Resources.Hp);

            Wait(fighter, 1);
            Assert.Equal(700, fighter.Resources.Hp);
            Assert.Equal(200, events.Single(item => item.Kind == CombatEventKind.SelfHealed).Amount);
        }

        [Fact]
        public void ChemTien_LungesThreeUnitsAndHitsAlongThePath()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 2.5d);

            CastToEnd(fighter, SkillSlot.OptionA, boss);

            Assert.Equal(StartX + 3d, fighter.Movement.X, Precision);
            Assert.Equal(160, Hits.Single().Amount);
        }

        [Fact]
        public void ChemTien_StopsAtAWall()
        {
            Fighter fighter = NewFighter(28.5d);

            CastToEnd(fighter, SkillSlot.OptionA);

            Assert.Equal(29.75d, fighter.Movement.X, Precision);
        }

        [Fact]
        public void ChemTien_IgnoresTheJoystick()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.OptionA);
            Wait(fighter, Ticks(0.6d) - 1, moveX: -RunTilt);

            Assert.Equal(StartX + 3d, fighter.Movement.X, Precision);
        }

        [Theory]
        [InlineData(SkillSlot.Attack)]
        [InlineData(SkillSlot.Ranged)]
        public void WalkingSkill_KeepsSixtyPercentSpeed(SkillSlot slot)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);
            double start = fighter.Movement.X;
            Wait(fighter, 6, RunTilt);

            Assert.Equal(start + 6 * 4d * 0.6d * Game.Rules.Ticks.DeltaSeconds, fighter.Movement.X, Precision);
        }

        [Theory]
        [InlineData(SkillSlot.Support)]
        [InlineData(SkillSlot.OptionB)]
        [InlineData(SkillSlot.Ultimate)]
        public void RootingSkill_StopsMovementAndJumping(SkillSlot slot)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);
            Tick(fighter, moveX: RunTilt, moveY: 1d);
            Wait(fighter, 6, RunTilt);

            Assert.Equal(StartX, fighter.Movement.X, Precision);
            Assert.True(fighter.Movement.Grounded);
        }

        [Fact]
        public void Movement_ReturnsToFullSpeedAfterTheSkill()
        {
            Fighter fighter = NewFighter();

            CastToEnd(fighter, SkillSlot.Attack);
            double start = fighter.Movement.X;
            Wait(fighter, 6, RunTilt);

            Assert.Equal(start + 6 * 4d * Game.Rules.Ticks.DeltaSeconds, fighter.Movement.X, Precision);
        }

        [Theory]
        [InlineData(SkillSlot.Attack)]
        [InlineData(SkillSlot.Ranged)]
        public void AirSkill_WorksInTheAir(SkillSlot slot)
        {
            Fighter fighter = NewFighter();
            Jump(fighter);

            Cast(fighter, slot);

            Assert.NotNull(fighter.Combat.Casting);
        }

        [Theory]
        [InlineData(SkillSlot.Support)]
        [InlineData(SkillSlot.OptionA)]
        [InlineData(SkillSlot.OptionB)]
        [InlineData(SkillSlot.Ultimate)]
        public void GroundSkill_IsRefusedInTheAir(SkillSlot slot)
        {
            Fighter fighter = NewFighter();
            Jump(fighter);

            Cast(fighter, slot);

            Assert.Null(fighter.Combat.Casting);
        }

        [Theory]
        [InlineData(SkillSlot.Attack, 0.25d)]
        [InlineData(SkillSlot.Ultimate, 1.2d)]
        [InlineData(SkillSlot.Support, 0.3d)]
        public void Dash_CancelsRecovery(SkillSlot slot, double recoveryStart)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);
            Wait(fighter, Ticks(recoveryStart));
            Assert.Equal(CastPhase.Recovery, fighter.Combat.Phase);

            Tick(fighter, dash: true);

            Assert.True(fighter.Movement.IsDashing);
            Assert.Null(fighter.Combat.Casting);
            Assert.Equal(75d, fighter.Movement.Stamina, Precision);
        }

        [Theory]
        [InlineData(SkillSlot.Ultimate, CastPhase.Windup, 1)]
        [InlineData(SkillSlot.Ultimate, CastPhase.Active, 12)]
        public void Dash_CannotCancelWindupOrActive(SkillSlot slot, CastPhase phase, int waitTicks)
        {
            Fighter fighter = NewFighter();

            Cast(fighter, slot);
            Wait(fighter, waitTicks);
            Assert.Equal(phase, fighter.Combat.Phase);

            Tick(fighter, dash: true);

            Assert.False(fighter.Movement.IsDashing);
            Assert.Equal(slot, fighter.Combat.Casting!.Slot);
        }

        [Fact]
        public void Dash_PressedJustBeforeRecoveryStartsThere()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack);
            Wait(fighter, Ticks(0.25d) - 3);
            Tick(fighter, dash: true);
            Assert.False(fighter.Movement.IsDashing);

            Wait(fighter, 3);

            Assert.True(fighter.Movement.IsDashing);
            Assert.Null(fighter.Combat.Casting);
        }

        [Fact]
        public void Skill_CannotStartDuringADash()
        {
            Fighter fighter = NewFighter();

            Tick(fighter, dash: true);
            Cast(fighter, SkillSlot.Attack);

            Assert.Null(fighter.Combat.Casting);
        }

        [Fact]
        public void Aim_TurnsTowardATargetInReach()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX - 3d);

            Cast(fighter, SkillSlot.Attack, boss);

            Assert.Equal(-1, fighter.Movement.Facing);
        }

        [Fact]
        public void Aim_KeepsFacingWhenTheTargetIsOutOfReach()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX - 4d);

            Cast(fighter, SkillSlot.Attack, boss);

            Assert.Equal(1, fighter.Movement.Facing);
        }

        [Fact]
        public void Aim_RangedReachIsItsRange()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX - 7d);

            Cast(fighter, SkillSlot.Ranged, boss);

            Assert.Equal(-1, fighter.Movement.Facing);
        }

        [Fact]
        public void Aim_HeldDirectionBeatsATargetInReach()
        {
            Fighter fighter = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            Tick(fighter, SkillSlot.Attack, moveX: -RunTilt, target: boss);

            Assert.Equal(-1, fighter.Movement.Facing);
        }

        [Fact]
        public void Aim_TurningDuringWindupTakesTheBlowAround()
        {
            Fighter fighter = NewFighter();
            CombatTarget behind = BossAt(StartX - 3d);

            Cast(fighter, SkillSlot.Attack);
            Wait(fighter, Ticks(0.15d) - 1, -RunTilt);
            Wait(fighter, 1, -RunTilt, behind);

            Assert.Equal(-1, fighter.Movement.Facing);
            Assert.Equal(60, Hits.Single().Amount);
        }

        [Fact]
        public void Aim_TheBlowItselfCannotTurn()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack);
            Wait(fighter, Ticks(0.15d) + 1);
            Assert.Equal(CastPhase.Active, fighter.Combat.Phase);

            Wait(fighter, 1, -RunTilt);

            Assert.Equal(1, fighter.Movement.Facing);
        }

        [Fact]
        public void Aim_RecoveryTurnsAndTheNextHitFollows()
        {
            Fighter fighter = NewFighter();
            CombatTarget behind = BossAt(StartX - 3d);

            Cast(fighter, SkillSlot.Attack);
            Wait(fighter, Ticks(0.25d) + 1);
            Assert.Equal(CastPhase.Recovery, fighter.Combat.Phase);

            Wait(fighter, 1, -RunTilt);
            Assert.Equal(-1, fighter.Movement.Facing);

            Wait(fighter, Ticks(0.2d), -RunTilt);
            Tick(fighter, SkillSlot.Attack, moveX: -RunTilt, target: behind);
            Wait(fighter, Ticks(0.15d) + 1, -RunTilt, behind);

            Assert.Equal(1, fighter.Combat.ComboStep);
            Assert.Equal(60, Hits.Single().Amount);
        }

        [Fact]
        public void Aim_RootedSkillCannotTurnOnceStarted()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.OptionA);
            Wait(fighter, 3, -RunTilt);

            Assert.Equal(1, fighter.Movement.Facing);
        }

        [Fact]
        public void Stamina_RegeneratesFivePerSecondWhileRunningAndAttacking()
        {
            var fighter = new Fighter(config) { Movement = new MovementState(StartX, 0d, 50d) };

            for (int tick = 0; tick < Ticks(0.45d); tick++)
            {
                Tick(fighter, tick == 0 ? SkillSlot.Attack : (SkillSlot?)null, moveX: RunTilt);
            }

            Assert.Equal(50d + 5d * Ticks(0.45d) * Game.Rules.Ticks.DeltaSeconds, fighter.Movement.Stamina, Precision);
        }

        [Fact]
        public void Ready_IsFalseDuringCooldown()
        {
            Fighter fighter = NewFighter();
            SkillConfig skill = config.Skill(SkillSlot.Ranged);
            Assert.True(Combat.IsSkillReady(fighter.Combat, fighter.Resources, skill));

            Cast(fighter, SkillSlot.Ranged);

            Assert.False(Combat.IsSkillReady(fighter.Combat, fighter.Resources, skill));
        }

        [Fact]
        public void Ready_IsFalseWithoutEnoughEnergy()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TrySpendEnergy(fighter.Resources, 95);

            Assert.False(Combat.IsSkillReady(fighter.Combat, fighter.Resources, config.Skill(SkillSlot.Ranged)));
            Assert.True(Combat.IsSkillReady(fighter.Combat, fighter.Resources, config.Skill(SkillSlot.Attack)));
        }

        [Fact]
        public void PotionReady_FollowsWaitAndBottles()
        {
            Fighter fighter = NewFighter();
            Assert.True(Combat.IsPotionReady(fighter.Combat));

            Tick(fighter, potion: true);
            Assert.False(Combat.IsPotionReady(fighter.Combat));

            Wait(fighter, Ticks(5d));
            Assert.True(Combat.IsPotionReady(fighter.Combat));
        }

        [Fact]
        public void ActiveHitbox_ShowsOnlyDuringTheActivePhase()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack);
            Assert.Null(Combat.ActiveHitbox(fighter.Combat, fighter.Movement));

            Wait(fighter, Ticks(0.15d) - 1);
            CombatBox box = Combat.ActiveHitbox(fighter.Combat, fighter.Movement)!.Value;

            Assert.Equal(StartX, box.MinX, Precision);
            Assert.Equal(StartX + 1.5d, box.MaxX, Precision);
            Assert.Equal(1d, box.MaxY, Precision);

            Wait(fighter, Ticks(0.1d));
            Assert.Null(Combat.ActiveHitbox(fighter.Combat, fighter.Movement));
        }

        [Fact]
        public void ActiveHitbox_FollowsTheAim()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Attack, BossAt(StartX - 3d));
            Wait(fighter, Ticks(0.15d) - 1);
            CombatBox box = Combat.ActiveHitbox(fighter.Combat, fighter.Movement)!.Value;

            Assert.Equal(StartX - 1.5d, box.MinX, Precision);
            Assert.Equal(StartX, box.MaxX, Precision);
        }

        [Fact]
        public void ActiveHitbox_IsEmptyForSkillsWithoutOne()
        {
            Fighter fighter = NewFighter();

            Cast(fighter, SkillSlot.Ranged);
            Wait(fighter, Ticks(0.2d));

            Assert.Null(Combat.ActiveHitbox(fighter.Combat, fighter.Movement));
        }

        [Fact]
        public void SameIntents_GiveTheSameResult()
        {
            Fighter first = NewFighter();
            Fighter second = NewFighter();
            CombatTarget boss = BossAt(StartX + 1d);

            foreach (Fighter fighter in new[] { first, second })
            {
                Cast(fighter, SkillSlot.Ultimate, boss);
                Wait(fighter, 20, RunTilt, boss);
                Tick(fighter, dash: true, target: boss);
                Wait(fighter, 30, RunTilt, boss);
            }

            Assert.Equal(first.Movement.X, second.Movement.X);
            Assert.Equal(first.Resources.Energy, second.Resources.Energy);
            Assert.Equal(first.Movement.Stamina, second.Movement.Stamina);
        }

        [Fact]
        public void Potion_RestoresThreeHundredAtOnce()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 500);

            Tick(fighter, potion: true);

            Assert.Equal(800, fighter.Resources.Hp);
            Assert.Equal(4, fighter.Combat.PotionsLeft);
            Assert.Equal(300, events.Single(item => item.Kind == CombatEventKind.PotionUsed).Amount);
        }

        [Fact]
        public void Potion_StopsAtMaximumHealth()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 100);

            Tick(fighter, potion: true);

            Assert.Equal(1000, fighter.Resources.Hp);
        }

        [Fact]
        public void Potion_WaitsFiveSecondsBetweenUses()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 900);

            Tick(fighter, potion: true);
            Wait(fighter, Ticks(5d) - 2);
            Tick(fighter, potion: true);
            Assert.Equal(400, fighter.Resources.Hp);

            Wait(fighter, Ticks(0.05d));
            Assert.Equal(700, fighter.Resources.Hp);
            Assert.Equal(3, fighter.Combat.PotionsLeft);
        }

        [Fact]
        public void Potion_RunsOutAfterFiveBottles()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 999);

            for (int bottle = 0; bottle < 6; bottle++)
            {
                Tick(fighter, potion: true);
                Wait(fighter, Ticks(5d));
                ResourceRules.TakeDamage(fighter.Resources, 300);
            }

            Assert.Equal(0, fighter.Combat.PotionsLeft);
            Assert.Equal(5, events.Count(item => item.Kind == CombatEventKind.PotionUsed));
        }

        [Theory]
        [InlineData(SkillSlot.Attack)]
        [InlineData(SkillSlot.Ranged)]
        public void Potion_WorksDuringASkillThatAllowsMovement(SkillSlot slot)
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 500);

            Cast(fighter, slot);
            Tick(fighter, potion: true);

            Assert.Equal(800, fighter.Resources.Hp);
            Assert.NotNull(fighter.Combat.Casting);
        }

        [Theory]
        [InlineData(SkillSlot.Support)]
        [InlineData(SkillSlot.OptionA)]
        [InlineData(SkillSlot.OptionB)]
        [InlineData(SkillSlot.Ultimate)]
        public void Potion_IsRefusedDuringASkillThatLocksMovement(SkillSlot slot)
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 900);

            Cast(fighter, slot);
            Tick(fighter, potion: true);

            Assert.Equal(5, fighter.Combat.PotionsLeft);
        }

        [Fact]
        public void Potion_PressedJustBeforeALockingSkillEndsIsUsed()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 900);

            Cast(fighter, SkillSlot.Ultimate);
            Wait(fighter, Ticks(1.5d) - 1 - 3);
            Tick(fighter, potion: true);
            Assert.Equal(5, fighter.Combat.PotionsLeft);

            Wait(fighter, 3);

            Assert.Equal(4, fighter.Combat.PotionsLeft);
        }

        [Fact]
        public void Potion_DoesNotReviveTheDead()
        {
            Fighter fighter = NewFighter();
            ResourceRules.TakeDamage(fighter.Resources, 1000);

            Tick(fighter, potion: true);

            Assert.Equal(0, fighter.Resources.Hp);
            Assert.Equal(5, fighter.Combat.PotionsLeft);
        }
    }
}
