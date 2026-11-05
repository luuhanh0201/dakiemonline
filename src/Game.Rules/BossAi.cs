using System;
using System.Collections.Generic;

namespace Game.Rules
{
    public enum BossPhase
    {
        Approach,
        Warning,
        Striking,
        Rest,
    }

    public enum BossEventKind
    {
        WarningStarted,
        PlayerHit,
    }

    public readonly struct BossEvent
    {
        public BossEvent(BossEventKind kind, BossAttackConfig attack, int damage = 0)
        {
            Kind = kind;
            Attack = attack;
            Damage = damage;
        }

        public BossEventKind Kind { get; }
        public BossAttackConfig Attack { get; }
        public int Damage { get; }
    }

    public sealed class BossState
    {
        public BossState(BossConfig config, double x)
        {
            Hp = config.MaxHp;
            X = x;
        }

        public int Hp { get; internal set; }

        /// <summary>Horizontal centre of the body. The boss stays on the ground.</summary>
        public double X { get; internal set; }

        public int Facing { get; internal set; } = -1;
        public BossPhase Phase { get; internal set; } = BossPhase.Approach;
        public bool IsDead => Hp == 0;

        internal int CycleIndex { get; set; }
        internal int PhaseTicksLeft { get; set; }
        internal double LockedX { get; set; }
        internal bool PlayerWasHit { get; set; }
        internal int SlowTicksLeft { get; set; }
        internal double SlowPercent { get; set; }
    }

    public static class BossAi
    {
        private const double GroundY = 0d;

        public static double StartX(BossConfig config, double playerX) => playerX + config.StartDistance;

        public static BossAttackConfig CurrentAttack(BossState boss, BossConfig config)
        {
            return config.Cycle[boss.CycleIndex];
        }

        /// <summary>What the player's skills aim at.</summary>
        public static CombatTarget TargetOf(BossState boss, BossConfig config)
        {
            double half = config.Width / 2d;
            return new CombatTarget(new CombatBox(boss.X - half, GroundY, boss.X + half, GroundY + config.Height), config.Defense);
        }

        /// <summary>The whole area of the coming attack, shown in red, or null when none is coming.</summary>
        public static CombatBox? WarningZone(BossState boss, BossConfig config)
        {
            if (boss.Phase != BossPhase.Warning)
            {
                return null;
            }

            BossAttackConfig attack = CurrentAttack(boss, config);

            switch (attack.Shape)
            {
                case BossAttackShape.BothSides:
                    return new CombatBox(boss.X - attack.Width, GroundY, boss.X + attack.Width, GroundY + attack.Height);
                case BossAttackShape.Column:
                    return ColumnBox(boss, attack);
                default:
                    return SideBox(boss, attack, boss.Facing);
            }
        }

        /// <summary>Takes the damage and slow of the skill hits in this tick's combat events.</summary>
        public static void ApplyHits(BossState boss, IReadOnlyList<CombatEvent> events)
        {
            foreach (CombatEvent item in events)
            {
                if (item.Kind != CombatEventKind.SkillHit)
                {
                    continue;
                }

                boss.Hp = Math.Max(0, boss.Hp - item.Amount);

                if (item.Slow != null)
                {
                    boss.SlowPercent = item.Slow.Percent;
                    boss.SlowTicksLeft = Ticks.FromSeconds(item.Slow.DurationSec);
                }
            }
        }

        public static void Step(
            BossState boss,
            MovementState player,
            ResourceState resources,
            GameConfig config,
            CollisionMap map,
            List<BossEvent> events)
        {
            if (boss.IsDead || resources.IsDead)
            {
                return;
            }

            boss.SlowTicksLeft = Math.Max(0, boss.SlowTicksLeft - 1);

            switch (boss.Phase)
            {
                case BossPhase.Approach:
                    Approach(boss, player, config, map, events);
                    break;
                case BossPhase.Warning:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        Strike(boss, player, resources, config, events);
                    }

                    break;
                case BossPhase.Striking:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        BossAttackConfig attack = CurrentAttack(boss, config.Boss);
                        HitPlayer(boss, player, resources, config, attack, SideBox(boss, attack, -boss.Facing), events);
                        BeginRest(boss, config);
                    }

                    break;
                case BossPhase.Rest:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        boss.Phase = BossPhase.Approach;
                        Approach(boss, player, config, map, events);
                    }

                    break;
            }
        }

        private static void Approach(
            BossState boss,
            MovementState player,
            GameConfig config,
            CollisionMap map,
            List<BossEvent> events)
        {
            BossAttackConfig attack = CurrentAttack(boss, config.Boss);
            double distance = Math.Abs(player.X - boss.X);

            if (distance <= attack.ApproachRange || attack.ApproachRange == 0d)
            {
                BeginWarning(boss, player, attack, events);
                return;
            }

            boss.Facing = Math.Sign(player.X - boss.X);

            double slowFactor = boss.SlowTicksLeft > 0 ? 1d - boss.SlowPercent / 100d : 1d;
            double step = config.Boss.MoveSpeed * slowFactor * Ticks.DeltaSeconds;
            double deltaX = boss.Facing * Math.Min(step, distance - attack.ApproachRange);
            boss.X = map.SweepX(boss.X, GroundY, deltaX, config.Boss.Width, config.Boss.Height);
        }

        private static void BeginWarning(BossState boss, MovementState player, BossAttackConfig attack, List<BossEvent> events)
        {
            if (player.X != boss.X)
            {
                boss.Facing = Math.Sign(player.X - boss.X);
            }

            boss.Phase = BossPhase.Warning;
            boss.PhaseTicksLeft = Ticks.FromSeconds(attack.WarningSec);
            boss.LockedX = player.X;
            boss.PlayerWasHit = false;
            events.Add(new BossEvent(BossEventKind.WarningStarted, attack));
        }

        private static void Strike(
            BossState boss,
            MovementState player,
            ResourceState resources,
            GameConfig config,
            List<BossEvent> events)
        {
            BossAttackConfig attack = CurrentAttack(boss, config.Boss);

            if (attack.Shape == BossAttackShape.Column)
            {
                HitPlayer(boss, player, resources, config, attack, ColumnBox(boss, attack), events);
                BeginRest(boss, config);
                return;
            }

            HitPlayer(boss, player, resources, config, attack, SideBox(boss, attack, boss.Facing), events);

            if (attack.Shape == BossAttackShape.BothSides)
            {
                boss.Phase = BossPhase.Striking;
                boss.PhaseTicksLeft = Ticks.FromSeconds(attack.SideDelaySec);
            }
            else
            {
                BeginRest(boss, config);
            }
        }

        private static void HitPlayer(
            BossState boss,
            MovementState player,
            ResourceState resources,
            GameConfig config,
            BossAttackConfig attack,
            in CombatBox strike,
            List<BossEvent> events)
        {
            double half = config.Movement.BodyWidth / 2d;
            var body = new CombatBox(player.X - half, player.Y, player.X + half, player.Y + config.Movement.BodyHeight);

            if (boss.PlayerWasHit || !strike.Overlaps(body))
            {
                return;
            }

            boss.PlayerWasHit = true;
            int damage = SkillDamage.AfterDefense(attack.Damage, config.Character.Defense);
            ResourceRules.TakeDamage(resources, damage);
            events.Add(new BossEvent(BossEventKind.PlayerHit, attack, damage));
        }

        private static void BeginRest(BossState boss, GameConfig config)
        {
            BossConfig bossConfig = config.Boss;
            BossAttackConfig attack = CurrentAttack(boss, bossConfig);
            bool enraged = boss.Hp * 100d < bossConfig.MaxHp * bossConfig.EnrageHpPercent;
            double seconds = enraged ? attack.RestSec * bossConfig.EnrageRestPercent / 100d : attack.RestSec;

            boss.Phase = BossPhase.Rest;
            boss.PhaseTicksLeft = Math.Max(1, Ticks.FromSeconds(seconds));
            boss.CycleIndex = (boss.CycleIndex + 1) % bossConfig.Cycle.Count;
        }

        private static CombatBox SideBox(BossState boss, BossAttackConfig attack, int direction)
        {
            double tip = boss.X + direction * attack.Width;
            return new CombatBox(Math.Min(boss.X, tip), GroundY, Math.Max(boss.X, tip), GroundY + attack.Height);
        }

        private static CombatBox ColumnBox(BossState boss, BossAttackConfig attack)
        {
            double half = attack.Width / 2d;
            return new CombatBox(boss.LockedX - half, GroundY, boss.LockedX + half, GroundY + attack.Height);
        }
    }
}
