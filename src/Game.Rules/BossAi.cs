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
        public BossEvent(BossEventKind kind, BossAttackConfig attack, int damage = 0, int playerId = 0)
        {
            Kind = kind;
            Attack = attack;
            Damage = damage;
            PlayerId = playerId;
        }

        public BossEventKind Kind { get; }
        public BossAttackConfig Attack { get; }
        public int Damage { get; }

        /// <summary>Who was hit, for <see cref="BossEventKind.PlayerHit"/>. A single-player fight uses 0.</summary>
        public int PlayerId { get; }
    }

    /// <summary>A player the boss can chase and hit.</summary>
    public readonly struct BossTarget
    {
        public BossTarget(int playerId, MovementState movement, ResourceState resources)
        {
            PlayerId = playerId;
            Movement = movement;
            Resources = resources;
        }

        public int PlayerId { get; }
        public MovementState Movement { get; }
        public ResourceState Resources { get; }
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

        /// <summary>Players the current attack already hit; each is hit at most once per attack.</summary>
        internal HashSet<int> PlayersHit { get; } = new HashSet<int>();

        internal int SlowTicksLeft { get; set; }
        internal double SlowPercent { get; set; }

        public static BossState FromSnapshot(BossConfig config, in BossSnapshot snapshot)
        {
            var boss = new BossState(config, snapshot.X);
            boss.Restore(snapshot);
            return boss;
        }

        public BossSnapshot Capture()
        {
            return new BossSnapshot(Hp, X, Facing, Phase, CycleIndex, PhaseTicksLeft, LockedX, SlowTicksLeft, SlowPercent);
        }

        /// <summary>Puts the boss back as captured. Who the current attack already hit is not carried.</summary>
        public void Restore(in BossSnapshot snapshot)
        {
            Hp = snapshot.Hp;
            X = snapshot.X;
            Facing = snapshot.Facing;
            Phase = snapshot.Phase;
            CycleIndex = snapshot.CycleIndex;
            PhaseTicksLeft = snapshot.PhaseTicksLeft;
            LockedX = snapshot.LockedX;
            SlowTicksLeft = snapshot.SlowTicksLeft;
            SlowPercent = snapshot.SlowPercent;
        }
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

        /// <summary>One tick against a single player, as in the offline fight.</summary>
        public static void Step(
            BossState boss,
            MovementState player,
            ResourceState resources,
            GameConfig config,
            CollisionMap map,
            List<BossEvent> events)
        {
            Step(boss, new[] { new BossTarget(0, player, resources) }, config, map, events);
        }

        /// <summary>
        /// One tick against several players. The boss chases the nearest living player and aims
        /// each new attack at them; an attack hits every living player inside it, once each.
        /// With nobody alive it waits where it is.
        /// </summary>
        public static void Step(
            BossState boss,
            IReadOnlyList<BossTarget> targets,
            GameConfig config,
            CollisionMap map,
            List<BossEvent> events)
        {
            BossTarget? nearest = Nearest(boss, targets);

            if (boss.IsDead || nearest == null)
            {
                return;
            }

            boss.SlowTicksLeft = Math.Max(0, boss.SlowTicksLeft - 1);

            switch (boss.Phase)
            {
                case BossPhase.Approach:
                    Approach(boss, nearest.Value.Movement, config, map, events);
                    break;
                case BossPhase.Warning:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        Strike(boss, targets, config, events);
                    }

                    break;
                case BossPhase.Striking:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        BossAttackConfig attack = CurrentAttack(boss, config.Boss);
                        HitPlayers(boss, targets, config, attack, SideBox(boss, attack, -boss.Facing), events);
                        BeginRest(boss, config);
                    }

                    break;
                case BossPhase.Rest:
                    if (--boss.PhaseTicksLeft == 0)
                    {
                        boss.Phase = BossPhase.Approach;
                        Approach(boss, nearest.Value.Movement, config, map, events);
                    }

                    break;
            }
        }

        /// <summary>The living player closest along the ground; on a tie, the lowest id.</summary>
        private static BossTarget? Nearest(BossState boss, IReadOnlyList<BossTarget> targets)
        {
            BossTarget? best = null;
            double bestDistance = double.MaxValue;

            foreach (BossTarget target in targets)
            {
                if (target.Resources.IsDead)
                {
                    continue;
                }

                double distance = Math.Abs(target.Movement.X - boss.X);

                if (distance < bestDistance || (distance == bestDistance && target.PlayerId < best!.Value.PlayerId))
                {
                    best = target;
                    bestDistance = distance;
                }
            }

            return best;
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
            boss.PlayersHit.Clear();
            events.Add(new BossEvent(BossEventKind.WarningStarted, attack));
        }

        private static void Strike(
            BossState boss,
            IReadOnlyList<BossTarget> targets,
            GameConfig config,
            List<BossEvent> events)
        {
            BossAttackConfig attack = CurrentAttack(boss, config.Boss);

            if (attack.Shape == BossAttackShape.Column)
            {
                HitPlayers(boss, targets, config, attack, ColumnBox(boss, attack), events);
                BeginRest(boss, config);
                return;
            }

            HitPlayers(boss, targets, config, attack, SideBox(boss, attack, boss.Facing), events);

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

        private static void HitPlayers(
            BossState boss,
            IReadOnlyList<BossTarget> targets,
            GameConfig config,
            BossAttackConfig attack,
            in CombatBox strike,
            List<BossEvent> events)
        {
            double half = config.Movement.BodyWidth / 2d;

            foreach (BossTarget target in targets)
            {
                MovementState player = target.Movement;
                var body = new CombatBox(player.X - half, player.Y, player.X + half, player.Y + config.Movement.BodyHeight);

                if (target.Resources.IsDead || boss.PlayersHit.Contains(target.PlayerId) || !strike.Overlaps(body))
                {
                    continue;
                }

                boss.PlayersHit.Add(target.PlayerId);
                int damage = SkillDamage.AfterDefense(attack.Damage, config.Character.Defense);
                ResourceRules.TakeDamage(target.Resources, damage);
                events.Add(new BossEvent(BossEventKind.PlayerHit, attack, damage, target.PlayerId));
            }
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
