using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>
    /// The five equipment slots of T044. Weapon, armour and pants change how the character looks
    /// (GDD 10.1 [CHỐT]); the hat and the cape only add stats, and have only an icon.
    /// </summary>
    public enum GearSlot
    {
        Weapon,
        Armor,
        Pants,
        Hat,
        Cape,
    }

    /// <summary>One item in config/items.json. Tier 1 items have no stats yet (O01, O10 are open).</summary>
    public sealed class GearItemConfig : ConfigEntry
    {
        public string Name { get; set; } = string.Empty;
        public GearSlot Slot { get; set; }
        public int Tier { get; set; }

        /// <summary>The drawing's name: the item part of slot_item_part files, and of the icon.</summary>
        public string Art { get; set; } = string.Empty;

        /// <summary>What a new character wears in this slot. Exactly one per shown slot.</summary>
        public bool Default { get; set; }
    }

    /// <summary>
    /// What others see a character wear: one item for each shown slot, as its place in
    /// <see cref="GameConfig.Gear"/>, so it travels as three bytes.
    /// </summary>
    public readonly struct Appearance : IEquatable<Appearance>
    {
        public Appearance(int weapon, int armor, int pants)
        {
            Weapon = weapon;
            Armor = armor;
            Pants = pants;
        }

        public int Weapon { get; }
        public int Armor { get; }
        public int Pants { get; }

        public int this[GearSlot slot]
        {
            get
            {
                switch (slot)
                {
                    case GearSlot.Weapon: return Weapon;
                    case GearSlot.Armor: return Armor;
                    case GearSlot.Pants: return Pants;
                    default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "only weapon, armour and pants are shown");
                }
            }
        }

        public Appearance With(GearSlot slot, int item)
        {
            switch (slot)
            {
                case GearSlot.Weapon: return new Appearance(item, Armor, Pants);
                case GearSlot.Armor: return new Appearance(Weapon, item, Pants);
                case GearSlot.Pants: return new Appearance(Weapon, Armor, item);
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "only weapon, armour and pants are shown");
            }
        }

        public bool Equals(Appearance other) => Weapon == other.Weapon && Armor == other.Armor && Pants == other.Pants;

        public override bool Equals(object? obj) => obj is Appearance other && Equals(other);

        public override int GetHashCode() => (Weapon * 256 + Armor) * 256 + Pants;

        public static bool operator ==(Appearance left, Appearance right) => left.Equals(right);

        public static bool operator !=(Appearance left, Appearance right) => !left.Equals(right);

        public override string ToString() => $"({Weapon}, {Armor}, {Pants})";
    }

    /// <summary>
    /// Who may wear what. Until there is an inventory (T045, T078) anyone may wear any item in the
    /// catalog, as decided for T043 on 12/11/2026; the server still refuses unknown items.
    /// </summary>
    public static class GearRules
    {
        public static IReadOnlyList<GearSlot> ShownSlots { get; } = new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants };

        public static bool IsShown(GearSlot slot) => slot == GearSlot.Weapon || slot == GearSlot.Armor || slot == GearSlot.Pants;

        /// <summary>What a new character wears.</summary>
        public static Appearance Default(GameConfig config)
        {
            var appearance = new Appearance(-1, -1, -1);

            for (int index = 0; index < config.Gear.Count; index++)
            {
                GearItemConfig item = config.Gear[index];

                if (item.Default && IsShown(item.Slot))
                {
                    appearance = appearance.With(item.Slot, index);
                }
            }

            return appearance;
        }

        /// <summary>Puts on an item, if it exists and changes how the character looks; otherwise nothing changes.</summary>
        public static bool TryWear(GameConfig config, Appearance current, int item, out Appearance next)
        {
            next = current;

            if (item < 0 || item >= config.Gear.Count || !IsShown(config.Gear[item].Slot))
            {
                return false;
            }

            next = current.With(config.Gear[item].Slot, item);
            return true;
        }

        /// <summary>Whether every slot of an appearance holds an item of that slot, as a received one must.</summary>
        public static bool IsValid(GameConfig config, Appearance appearance)
        {
            foreach (GearSlot slot in ShownSlots)
            {
                int item = appearance[slot];

                if (item < 0 || item >= config.Gear.Count || config.Gear[item].Slot != slot)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The next item in a slot after the one worn, back to the first after the last.</summary>
        public static int Next(GameConfig config, Appearance current, GearSlot slot)
        {
            int worn = current[slot];

            for (int step = 1; step <= config.Gear.Count; step++)
            {
                int index = (worn + step + config.Gear.Count) % config.Gear.Count;

                if (config.Gear[index].Slot == slot)
                {
                    return index;
                }
            }

            return worn;
        }
    }
}
