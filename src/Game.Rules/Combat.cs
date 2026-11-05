using System;
using System.Collections.Generic;

namespace Game.Rules
{
    public readonly struct CombatIntent
    {
        public CombatIntent(SkillSlot? skillPressed, bool potionPressed = false)
        {
            SkillPressed = skillPressed;
            PotionPressed = potionPressed;
        }

        public SkillSlot? SkillPressed { get; }
        public bool PotionPressed { get; }
    }

    public readonly struct CombatBox
    {
        public CombatBox(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public bool Overlaps(in CombatBox other)
        {
            return MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;
        }
    }

    /// <summary>What a skill can hit. The boss fills this in from T018.</summary>
    public readonly struct CombatTarget
    {
        public CombatTarget(in CombatBox box, decimal defense)
        {
            Box = box;
            Defense = defense;
        }

        public CombatBox Box { get; }
        public decimal Defense { get; }
    }

    public enum CombatEventKind
    {
        SkillStarted,
        SkillHit,
        SkillEnded,
        ProjectileFired,
        SelfHealed,
        PotionUsed,
    }

    public readonly struct CombatEvent
    {
        public CombatEvent(CombatEventKind kind, SkillSlot? slot = null, int amount = 0, SkillSlow? slow = null)
        {
            Kind = kind;
            Slot = slot;
            Amount = amount;
            Slow = slow;
        }

        public CombatEventKind Kind { get; }
        public SkillSlot? Slot { get; }

        /// <summary>Damage dealt or health restored.</summary>
        public int Amount { get; }

        /// <summary>Slow the hit applies to the target, if any.</summary>
        public SkillSlow? Slow { get; }
    }

    public enum CastPhase
    {
        None,
        Windup,
        Active,
        Recovery,
    }

    public sealed class Projectile
    {
        internal Projectile(SkillSlot slot, SkillProjectile config, double x, double y, int direction)
        {
            Slot = slot;
            Config = config;
            X = x;
            Y = y;
            Direction = direction;
        }

        public double X { get; internal set; }

        /// <summary>Height of the projectile's centre.</summary>
        public double Y { get; }

        public int Direction { get; }

        internal SkillSlot Slot { get; }
        internal SkillProjectile Config { get; }
        internal double Travelled { get; set; }
    }

    public sealed class CombatState
    {
        private readonly List<Projectile> projectiles = new List<Projectile>();

        public CombatState(PotionConfig potion)
        {
            PotionsLeft = potion.Charges;
        }

        public SkillConfig? Casting { get; internal set; }
        public int PotionsLeft { get; internal set; }
        public IReadOnlyList<Projectile> Projectiles => projectiles;
        public bool IsAttacking => Casting != null;

        public CastPhase Phase
        {
            get
            {
                if (Casting == null)
                {
                    return CastPhase.None;
                }

                if (CastTicks < WindupTicks)
                {
                    return CastPhase.Windup;
                }

                return CastTicks < ActiveEndTicks ? CastPhase.Active : CastPhase.Recovery;
            }
        }

        internal List<Projectile> ProjectileList => projectiles;
        internal Dictionary<SkillSlot, int> CooldownTicks { get; } = new Dictionary<SkillSlot, int>();
        internal int CastTicks { get; set; }
        internal int WindupTicks { get; set; }
        internal int ActiveEndTicks { get; set; }
        internal int TotalTicks { get; set; }
        internal int[] PhaseEndTicks { get; set; } = Array.Empty<int>();
        internal int Direction { get; set; }
        internal double OriginX { get; set; }
        internal int LastHitPhase { get; set; }
        internal SkillSlot? BufferedSkill { get; set; }
        internal int SkillBufferTicks { get; set; }
        internal int PotionBufferTicks { get; set; }
        internal int PotionCooldownTicks { get; set; }

        public double CooldownSecondsLeft(SkillSlot slot)
        {
            return CooldownTicks.TryGetValue(slot, out int ticks) ? ticks * Ticks.DeltaSeconds : 0d;
        }

        public double PotionCooldownSecondsLeft => PotionCooldownTicks * Ticks.DeltaSeconds;
    }

    /// <summary>One tick of a player: skills, potion, movement and resources, in that order.</summary>
    public static class Combat
    {
        // The fixture fights at level 1; the numbers in GDD 18.1 are level 1 values.
        private const int SkillLevel = 1;
        private const int NoPhase = -1;

        public static void Step(
            CombatState combat,
            MovementState movement,
            ResourceState resources,
            in MovementIntent moveIntent,
            in CombatIntent intent,
            GameConfig config,
            CollisionMap map,
            CombatTarget? target,
            List<CombatEvent> events)
        {
            CoolDown(combat);
            BufferInput(combat, intent, config.Movement);
            TryStartBufferedSkill(combat, movement, resources, config, target, events);
            TryUsePotion(combat, resources, config, events);

            MovementLimits limits = LimitsFor(combat);
            Movement.Step(movement, moveIntent, config.Movement, map, limits);

            if (movement.IsDashing && combat.Phase == CastPhase.Recovery)
            {
                EndCast(combat, events);
            }

            // Read before advancing: the tick a skill ends on is still a tick spent attacking.
            bool attacking = combat.IsAttacking;

            AdvanceCast(combat, movement, resources, config, target, events);
            MoveProjectiles(combat, config, target, events);
            ResourceRules.Step(resources, movement, moveIntent, attacking, config.Character, config.Movement);
        }

        private static void CoolDown(CombatState combat)
        {
            foreach (SkillSlot slot in new List<SkillSlot>(combat.CooldownTicks.Keys))
            {
                combat.CooldownTicks[slot] = Math.Max(0, combat.CooldownTicks[slot] - 1);
            }

            combat.PotionCooldownTicks = Math.Max(0, combat.PotionCooldownTicks - 1);
        }

        private static void BufferInput(CombatState combat, in CombatIntent intent, MovementConfig config)
        {
            int bufferTicks = Ticks.FromSeconds(config.InputBufferSec);

            if (intent.SkillPressed != null)
            {
                combat.BufferedSkill = intent.SkillPressed;
                combat.SkillBufferTicks = bufferTicks;
            }
            else
            {
                combat.SkillBufferTicks = Math.Max(0, combat.SkillBufferTicks - 1);
            }

            combat.PotionBufferTicks = intent.PotionPressed ? bufferTicks : Math.Max(0, combat.PotionBufferTicks - 1);
        }

        private static void TryStartBufferedSkill(
            CombatState combat,
            MovementState movement,
            ResourceState resources,
            GameConfig config,
            CombatTarget? target,
            List<CombatEvent> events)
        {
            if (combat.SkillBufferTicks == 0 || combat.BufferedSkill == null)
            {
                return;
            }

            SkillSlot slot = combat.BufferedSkill.Value;
            SkillConfig skill = config.Skill(slot);

            if (combat.Casting != null
                || movement.IsDashing
                || resources.IsDead
                || combat.CooldownSecondsLeft(slot) > 0d
                || (!movement.Grounded && !skill.AllowedInAir)
                || !ResourceRules.TrySpendEnergy(resources, skill.EnergyCost))
            {
                return;
            }

            combat.CooldownTicks[slot] = Ticks.FromSeconds(skill.CooldownSec);
            combat.SkillBufferTicks = 0;
            BeginCast(combat, movement, skill, target);
            events.Add(new CombatEvent(CombatEventKind.SkillStarted, slot));
        }

        private static void BeginCast(CombatState combat, MovementState movement, SkillConfig skill, CombatTarget? target)
        {
            // Phase ends round from the running total so a skill's full length stays exact at 30 ticks.
            double seconds = skill.WindupSec;
            var phaseEnds = new int[skill.ActivePhases.Count];

            combat.WindupTicks = Ticks.FromSeconds(seconds);

            for (int index = 0; index < phaseEnds.Length; index++)
            {
                seconds += skill.ActivePhases[index].DurationSec;
                phaseEnds[index] = Ticks.FromSeconds(seconds);
            }

            combat.PhaseEndTicks = phaseEnds;
            combat.ActiveEndTicks = phaseEnds.Length > 0 ? phaseEnds[phaseEnds.Length - 1] : combat.WindupTicks;
            combat.TotalTicks = Ticks.FromSeconds(seconds + skill.RecoverySec);
            combat.Casting = skill;
            combat.CastTicks = 0;
            combat.LastHitPhase = NoPhase;
            combat.OriginX = movement.X;
            combat.Direction = AimDirection(movement, skill, target);
            movement.Facing = combat.Direction;
        }

        /// <summary>Turns toward a target in reach; otherwise keeps the direction the character faces.</summary>
        private static int AimDirection(MovementState movement, SkillConfig skill, CombatTarget? target)
        {
            double reach = skill.Projectile?.Range ?? skill.Hitbox?.Width ?? 0d;

            if (target == null || reach == 0d)
            {
                return movement.Facing;
            }

            CombatBox box = target.Value.Box;

            if (movement.X < box.MinX)
            {
                return box.MinX - movement.X <= reach ? 1 : movement.Facing;
            }

            if (movement.X > box.MaxX)
            {
                return movement.X - box.MaxX <= reach ? -1 : movement.Facing;
            }

            return movement.Facing;
        }

        private static void TryUsePotion(
            CombatState combat,
            ResourceState resources,
            GameConfig config,
            List<CombatEvent> events)
        {
            bool skillLocksMovement = combat.Casting != null && combat.Casting.MoveSpeedPercent == 0d;

            if (combat.PotionBufferTicks == 0
                || combat.PotionsLeft == 0
                || combat.PotionCooldownTicks > 0
                || resources.IsDead
                || skillLocksMovement)
            {
                return;
            }

            combat.PotionsLeft--;
            combat.PotionCooldownTicks = Ticks.FromSeconds(config.Potion.CooldownSec);
            combat.PotionBufferTicks = 0;
            ResourceRules.Heal(resources, config.Potion.HealHp, config.Character);
            events.Add(new CombatEvent(CombatEventKind.PotionUsed, amount: config.Potion.HealHp));
        }

        private static MovementLimits LimitsFor(CombatState combat)
        {
            SkillConfig? skill = combat.Casting;

            if (skill == null)
            {
                return MovementLimits.Free;
            }

            CastPhase phase = combat.Phase;
            double speedFactor = skill.MoveSpeedPercent / 100d;
            double forcedDeltaX = 0d;

            if (phase == CastPhase.Active && skill.DashDistance > 0d)
            {
                forcedDeltaX = combat.Direction * skill.DashDistance / (combat.ActiveEndTicks - combat.WindupTicks);
            }

            return new MovementLimits(speedFactor, speedFactor > 0d, phase == CastPhase.Recovery, true, forcedDeltaX);
        }

        private static void AdvanceCast(
            CombatState combat,
            MovementState movement,
            ResourceState resources,
            GameConfig config,
            CombatTarget? target,
            List<CombatEvent> events)
        {
            SkillConfig? skill = combat.Casting;

            if (skill == null)
            {
                return;
            }

            if (combat.CastTicks == combat.WindupTicks)
            {
                Release(combat, movement, resources, skill, config, events);
            }

            if (combat.Phase == CastPhase.Active)
            {
                TryHit(combat, movement, skill, config, target, events);
            }

            combat.CastTicks++;

            if (combat.CastTicks >= combat.TotalTicks)
            {
                EndCast(combat, events);
            }
        }

        /// <summary>Effects that happen the moment the windup ends.</summary>
        private static void Release(
            CombatState combat,
            MovementState movement,
            ResourceState resources,
            SkillConfig skill,
            GameConfig config,
            List<CombatEvent> events)
        {
            if (skill.Heal != null)
            {
                int before = resources.Hp;
                ResourceRules.Heal(resources, skill.Heal, config.Character);
                events.Add(new CombatEvent(CombatEventKind.SelfHealed, skill.Slot, resources.Hp - before));
            }

            if (skill.Projectile != null)
            {
                double centreY = movement.Y + config.Movement.BodyHeight / 2d;
                combat.ProjectileList.Add(new Projectile(skill.Slot, skill.Projectile, movement.X, centreY, combat.Direction));
                events.Add(new CombatEvent(CombatEventKind.ProjectileFired, skill.Slot));
            }
        }

        private static void TryHit(
            CombatState combat,
            MovementState movement,
            SkillConfig skill,
            GameConfig config,
            CombatTarget? target,
            List<CombatEvent> events)
        {
            int phase = Array.FindIndex(combat.PhaseEndTicks, end => combat.CastTicks < end);

            if (target == null || skill.Hitbox == null || phase == combat.LastHitPhase)
            {
                return;
            }

            CombatBox hitbox = HitboxOf(combat, movement, skill);

            if (!hitbox.Overlaps(target.Value.Box))
            {
                return;
            }

            combat.LastHitPhase = phase;
            int damage = SkillDamage.HitDamage(skill, phase, config.Character.Attack, SkillLevel, target.Value.Defense);
            events.Add(new CombatEvent(CombatEventKind.SkillHit, skill.Slot, damage, skill.Slow));
        }

        /// <summary>
        /// The box starts at the body's centre and reaches forward. A lunge instead covers
        /// the path travelled so far, so a wall that stops it also stops what it can hit.
        /// </summary>
        private static CombatBox HitboxOf(CombatState combat, MovementState movement, SkillConfig skill)
        {
            SkillHitbox hitbox = skill.Hitbox!;
            double top = movement.Y + hitbox.Height;

            if (skill.DashDistance > 0d)
            {
                double near = Math.Min(combat.OriginX, movement.X);
                double far = Math.Max(combat.OriginX, movement.X);
                return new CombatBox(near, movement.Y, far, top);
            }

            double tip = movement.X + combat.Direction * hitbox.Width;
            return new CombatBox(Math.Min(movement.X, tip), movement.Y, Math.Max(movement.X, tip), top);
        }

        private static void EndCast(CombatState combat, List<CombatEvent> events)
        {
            events.Add(new CombatEvent(CombatEventKind.SkillEnded, combat.Casting?.Slot));
            combat.Casting = null;
        }

        private static void MoveProjectiles(
            CombatState combat,
            GameConfig config,
            CombatTarget? target,
            List<CombatEvent> events)
        {
            List<Projectile> projectiles = combat.ProjectileList;

            for (int index = projectiles.Count - 1; index >= 0; index--)
            {
                Projectile projectile = projectiles[index];

                if (Fly(projectile, config, target, events))
                {
                    projectiles.RemoveAt(index);
                }
            }
        }

        /// <summary>Returns true when the projectile is used up.</summary>
        private static bool Fly(Projectile projectile, GameConfig config, CombatTarget? target, List<CombatEvent> events)
        {
            SkillProjectile shot = projectile.Config;
            double step = Math.Min(shot.Speed * Ticks.DeltaSeconds, shot.Range - projectile.Travelled);
            double from = projectile.X;
            double to = from + projectile.Direction * step;
            double halfWidth = shot.Width / 2d;
            var path = new CombatBox(
                Math.Min(from, to),
                projectile.Y - halfWidth,
                Math.Max(from, to),
                projectile.Y + halfWidth);

            projectile.X = to;
            projectile.Travelled += step;

            if (target != null && path.Overlaps(target.Value.Box))
            {
                SkillConfig skill = config.Skill(projectile.Slot);
                int damage = SkillDamage.HitDamage(skill, 0, config.Character.Attack, SkillLevel, target.Value.Defense);
                events.Add(new CombatEvent(CombatEventKind.SkillHit, skill.Slot, damage, skill.Slow));
                return true;
            }

            return projectile.Travelled >= shot.Range;
        }
    }
}
