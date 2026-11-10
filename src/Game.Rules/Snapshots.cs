using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    /// <summary>A projectile in flight, as a snapshot carries it.</summary>
    public readonly struct ProjectileSnapshot : IEquatable<ProjectileSnapshot>
    {
        public ProjectileSnapshot(SkillSlot slot, double x, double y, int direction, double travelled)
        {
            Slot = slot;
            X = x;
            Y = y;
            Direction = direction;
            Travelled = travelled;
        }

        public SkillSlot Slot { get; }
        public double X { get; }
        public double Y { get; }
        public int Direction { get; }
        public double Travelled { get; }

        public bool Equals(ProjectileSnapshot other)
        {
            return Slot == other.Slot && X == other.X && Y == other.Y && Direction == other.Direction && Travelled == other.Travelled;
        }

        public override bool Equals(object? obj) => obj is ProjectileSnapshot other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() ^ (int)Slot;
    }

    /// <summary>
    /// Everything about a player's skills the next tick depends on. Cast timings are not carried:
    /// they follow from the skill and are rebuilt by <see cref="CombatState.Restore"/>.
    /// </summary>
    public sealed class CombatSnapshot : IEquatable<CombatSnapshot>
    {
        public static readonly int SlotCount = Enum.GetValues(typeof(SkillSlot)).Length;

        public SkillSlot? Casting { get; set; }
        public int CastTicks { get; set; }
        public int ComboStep { get; set; }
        public int Direction { get; set; }
        public double OriginX { get; set; }
        public int LastHitPhase { get; set; }
        public int PotionsLeft { get; set; }

        /// <summary>Ticks left on each slot's cooldown, indexed by <see cref="SkillSlot"/>.</summary>
        public int[] Cooldowns { get; set; } = new int[SlotCount];

        public SkillSlot? BufferedSkill { get; set; }
        public int SkillBufferTicks { get; set; }
        public int PotionBufferTicks { get; set; }
        public int PotionCooldownTicks { get; set; }
        public int ComboWindowTicks { get; set; }
        public IReadOnlyList<ProjectileSnapshot> Projectiles { get; set; } = Array.Empty<ProjectileSnapshot>();

        public bool Equals(CombatSnapshot? other)
        {
            return other != null
                && Casting == other.Casting
                && CastTicks == other.CastTicks
                && ComboStep == other.ComboStep
                && Direction == other.Direction
                && OriginX == other.OriginX
                && LastHitPhase == other.LastHitPhase
                && PotionsLeft == other.PotionsLeft
                && Cooldowns.SequenceEqual(other.Cooldowns)
                && BufferedSkill == other.BufferedSkill
                && SkillBufferTicks == other.SkillBufferTicks
                && PotionBufferTicks == other.PotionBufferTicks
                && PotionCooldownTicks == other.PotionCooldownTicks
                && ComboWindowTicks == other.ComboWindowTicks
                && Projectiles.SequenceEqual(other.Projectiles);
        }

        public override bool Equals(object? obj) => Equals(obj as CombatSnapshot);

        public override int GetHashCode() => CastTicks ^ (PotionsLeft << 8);
    }

    public readonly struct ResourceSnapshot : IEquatable<ResourceSnapshot>
    {
        public ResourceSnapshot(int hp, double energy)
        {
            Hp = hp;
            Energy = energy;
        }

        public int Hp { get; }
        public double Energy { get; }

        public bool Equals(ResourceSnapshot other) => Hp == other.Hp && Energy == other.Energy;

        public override bool Equals(object? obj) => obj is ResourceSnapshot other && Equals(other);

        public override int GetHashCode() => Hp ^ Energy.GetHashCode();
    }

    /// <summary>Everything about one player in a room, for prediction and for coming back after a dropped connection.</summary>
    public sealed class PlayerSnapshot : IEquatable<PlayerSnapshot>
    {
        public PlayerSnapshot(MovementSnapshot movement, CombatSnapshot combat, ResourceSnapshot resources, int respawnTicksLeft)
        {
            Movement = movement;
            Combat = combat;
            Resources = resources;
            RespawnTicksLeft = respawnTicksLeft;
        }

        public MovementSnapshot Movement { get; }
        public CombatSnapshot Combat { get; }
        public ResourceSnapshot Resources { get; }

        /// <summary>Ticks until a dead player comes back; 0 while alive.</summary>
        public int RespawnTicksLeft { get; }

        public bool Equals(PlayerSnapshot? other)
        {
            return other != null
                && Movement.Equals(other.Movement)
                && Combat.Equals(other.Combat)
                && Resources.Equals(other.Resources)
                && RespawnTicksLeft == other.RespawnTicksLeft;
        }

        public override bool Equals(object? obj) => Equals(obj as PlayerSnapshot);

        public override int GetHashCode() => Movement.GetHashCode() ^ Resources.GetHashCode();
    }

    /// <summary>Everything a client needs to draw the boss and its red warning zone.</summary>
    public readonly struct BossSnapshot : IEquatable<BossSnapshot>
    {
        public BossSnapshot(int hp, double x, int facing, BossPhase phase, int cycleIndex, int phaseTicksLeft, double lockedX, int slowTicksLeft, double slowPercent)
        {
            Hp = hp;
            X = x;
            Facing = facing;
            Phase = phase;
            CycleIndex = cycleIndex;
            PhaseTicksLeft = phaseTicksLeft;
            LockedX = lockedX;
            SlowTicksLeft = slowTicksLeft;
            SlowPercent = slowPercent;
        }

        public int Hp { get; }
        public double X { get; }
        public int Facing { get; }
        public BossPhase Phase { get; }
        public int CycleIndex { get; }
        public int PhaseTicksLeft { get; }
        public double LockedX { get; }
        public int SlowTicksLeft { get; }
        public double SlowPercent { get; }

        public bool Equals(BossSnapshot other)
        {
            return Hp == other.Hp && X == other.X && Facing == other.Facing && Phase == other.Phase
                && CycleIndex == other.CycleIndex && PhaseTicksLeft == other.PhaseTicksLeft
                && LockedX == other.LockedX && SlowTicksLeft == other.SlowTicksLeft && SlowPercent == other.SlowPercent;
        }

        public override bool Equals(object? obj) => obj is BossSnapshot other && Equals(other);

        public override int GetHashCode() => Hp ^ X.GetHashCode();
    }
}
