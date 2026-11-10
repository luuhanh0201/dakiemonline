using System;

namespace Game.Rules
{
    /// <summary>Health and energy. Stamina lives in MovementState because movement spends it.</summary>
    public sealed class ResourceState
    {
        public ResourceState(CharacterConfig character)
        {
            Hp = character.MaxHp;
            Energy = character.MaxEnergy;
        }

        public int Hp { get; internal set; }
        public double Energy { get; internal set; }
        public bool IsDead => Hp == 0;

        public ResourceSnapshot Capture() => new ResourceSnapshot(Hp, Energy);

        public void Restore(in ResourceSnapshot snapshot)
        {
            Hp = snapshot.Hp;
            Energy = snapshot.Energy;
        }
    }

    public static class ResourceRules
    {
        // Regeneration adds fractions of a point per tick, so the sum can land just under a whole cost.
        private const double RoundingTolerance = 1e-9;

        /// <summary>Regenerates energy and stamina for one tick, after Movement.Step.</summary>
        public static void Step(
            ResourceState resources,
            MovementState movement,
            in MovementIntent intent,
            bool attacking,
            CharacterConfig character,
            MovementConfig config)
        {
            resources.Energy = Math.Min(
                character.MaxEnergy,
                resources.Energy + character.EnergyRegenPerSec * Ticks.DeltaSeconds);

            RegenStamina(movement, intent, attacking, character, config);
        }

        /// <summary>The stamina half of <see cref="Step"/>, for players who only move.</summary>
        public static void RegenStamina(
            MovementState movement,
            in MovementIntent intent,
            bool attacking,
            CharacterConfig character,
            MovementConfig config)
        {
            if (movement.StaminaDelayTicks > 0)
            {
                movement.StaminaDelayTicks--;
                return;
            }

            double perSecond = StaminaRegenPerSecond(movement, intent, attacking, config);
            movement.Stamina = Math.Min(character.MaxStamina, movement.Stamina + perSecond * Ticks.DeltaSeconds);
        }

        public static bool CanAfford(ResourceState resources, int cost)
        {
            return resources.Energy + RoundingTolerance >= cost;
        }

        /// <summary>Spends the cost only if all of it is available.</summary>
        public static bool TrySpendEnergy(ResourceState resources, int cost)
        {
            if (!CanAfford(resources, cost))
            {
                return false;
            }

            resources.Energy = Math.Max(0d, resources.Energy - cost);
            return true;
        }

        public static void TakeDamage(ResourceState resources, int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage cannot be negative.");
            }

            resources.Hp = Math.Max(0, resources.Hp - amount);
        }

        /// <summary>Heals the skill's fixed part plus a share of max health, rounded down.</summary>
        public static void Heal(ResourceState resources, SkillHeal heal, CharacterConfig character)
        {
            int amount = (int)Math.Floor(heal.Base + character.MaxHp * heal.MaxHpPercent / 100m);
            Heal(resources, amount, character);
        }

        public static void Heal(ResourceState resources, int amount, CharacterConfig character)
        {
            resources.Hp = Math.Min(character.MaxHp, resources.Hp + amount);
        }

        private static double StaminaRegenPerSecond(
            MovementState movement,
            in MovementIntent intent,
            bool attacking,
            MovementConfig config)
        {
            if (movement.IsDashing || !movement.Grounded)
            {
                return 0d;
            }

            if (attacking)
            {
                return config.StaminaRegenAttacking;
            }

            double tilt = Math.Abs(intent.MoveX);

            if (tilt == 0d)
            {
                return config.StaminaRegenIdle;
            }

            return tilt < config.WalkTiltBelow ? config.StaminaRegenWalk : 0d;
        }
    }
}
