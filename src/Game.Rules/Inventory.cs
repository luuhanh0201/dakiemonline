using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    public enum ItemPlace : byte
    {
        Bag = 1,
        Worn = 2,
    }

    /// <summary>
    /// One item a character owns. Bag slots run from 0; a worn item's slot is its <see cref="GearSlot"/>.
    /// Item is the place of its kind in <see cref="GameConfig.Gear"/>.
    /// </summary>
    public readonly struct OwnedItem : IEquatable<OwnedItem>
    {
        public OwnedItem(long id, int item, int quantity, ItemPlace place, int slot)
        {
            Id = id;
            Item = item;
            Quantity = quantity;
            Place = place;
            Slot = slot;
        }

        public long Id { get; }
        public int Item { get; }
        public int Quantity { get; }
        public ItemPlace Place { get; }
        public int Slot { get; }

        public OwnedItem At(ItemPlace place, int slot) => new OwnedItem(Id, Item, Quantity, place, slot);

        public bool Equals(OwnedItem other) => Id == other.Id && Item == other.Item && Quantity == other.Quantity && Place == other.Place && Slot == other.Slot;

        public override bool Equals(object? obj) => obj is OwnedItem other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => $"#{Id} item {Item} x{Quantity} {Place} {Slot}";
    }

    /// <summary>The bag and the ground, from config/items.json.</summary>
    public sealed class BagConfig : ConfigEntry
    {
        /// <summary>Fixed for now; buying more is for when gems exist (O12). Decided 12/11/2026.</summary>
        public int Slots { get; set; }

        /// <summary>How long a dropped item lies on the ground (GDD 11.3).</summary>
        public double GroundLifetimeSec { get; set; }

        /// <summary>How close the feet must be to an item on the ground to pick it up.</summary>
        public double PickupRange { get; set; }

        /// <summary>What a new character finds in the bag, besides the default clothes it wears.</summary>
        public IReadOnlyList<int> StarterBag { get; set; } = Array.Empty<int>();
    }

    public enum InventoryProblem : byte
    {
        None = 0,
        NotFound = 1,
        NotInBag = 2,
        NotWearable = 3,
        BagFull = 4,

        /// <summary>Weapon, armour and pants stay on; another item is put on instead.</summary>
        MustStayWorn = 5,

        NothingWorn = 6,
        BadSlot = 7,
        TooFar = 8,
        Gone = 9,
        Busy = 10,
    }

    /// <summary>
    /// What one action changes: items that move and items that leave the bag. A refused action has
    /// a problem and no changes. The server stores the plan in one database transaction.
    /// </summary>
    public sealed class InventoryPlan
    {
        private InventoryPlan(InventoryProblem problem, IReadOnlyList<OwnedItem> moved, IReadOnlyList<long> removed)
        {
            Problem = problem;
            Moved = moved;
            Removed = removed;
        }

        public InventoryProblem Problem { get; }
        public bool Accepted => Problem == InventoryProblem.None;

        /// <summary>Items in their new place.</summary>
        public IReadOnlyList<OwnedItem> Moved { get; }

        public IReadOnlyList<long> Removed { get; }

        public static InventoryPlan Refuse(InventoryProblem problem) => new InventoryPlan(problem, Array.Empty<OwnedItem>(), Array.Empty<long>());

        public static InventoryPlan Move(params OwnedItem[] moved) => new InventoryPlan(InventoryProblem.None, moved, Array.Empty<long>());

        public static InventoryPlan Remove(long id) => new InventoryPlan(InventoryProblem.None, Array.Empty<OwnedItem>(), new[] { id });
    }

    /// <summary>The bag (T078): wear, take off, move and drop, worked out without touching any storage.</summary>
    public static class InventoryRules
    {
        public static int? FreeBagSlot(BagConfig bag, IEnumerable<OwnedItem> items)
        {
            var taken = new HashSet<int>(items.Where(item => item.Place == ItemPlace.Bag).Select(item => item.Slot));

            for (int slot = 0; slot < bag.Slots; slot++)
            {
                if (!taken.Contains(slot))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>Puts on an item from the bag; what was worn there goes to the bag slot it leaves.</summary>
        public static InventoryPlan Wear(GameConfig config, IReadOnlyList<OwnedItem> items, long id)
        {
            if (!TryFind(items, id, out OwnedItem item))
            {
                return InventoryPlan.Refuse(InventoryProblem.NotFound);
            }

            if (item.Place != ItemPlace.Bag)
            {
                return InventoryPlan.Refuse(InventoryProblem.NotInBag);
            }

            if (item.Item < 0 || item.Item >= config.Gear.Count)
            {
                return InventoryPlan.Refuse(InventoryProblem.NotWearable);
            }

            int slot = (int)config.Gear[item.Item].Slot;
            OwnedItem worn = items.FirstOrDefault(other => other.Place == ItemPlace.Worn && other.Slot == slot);
            OwnedItem on = item.At(ItemPlace.Worn, slot);

            return worn.Id == 0
                ? InventoryPlan.Move(on)
                : InventoryPlan.Move(on, worn.At(ItemPlace.Bag, item.Slot));
        }

        /// <summary>Takes off a hat or a cape into the first free bag slot.</summary>
        public static InventoryPlan TakeOff(BagConfig bag, IReadOnlyList<OwnedItem> items, GearSlot slot)
        {
            if (GearRules.IsShown(slot))
            {
                return InventoryPlan.Refuse(InventoryProblem.MustStayWorn);
            }

            OwnedItem worn = items.FirstOrDefault(other => other.Place == ItemPlace.Worn && other.Slot == (int)slot);

            if (worn.Id == 0)
            {
                return InventoryPlan.Refuse(InventoryProblem.NothingWorn);
            }

            int? free = FreeBagSlot(bag, items);
            return free == null ? InventoryPlan.Refuse(InventoryProblem.BagFull) : InventoryPlan.Move(worn.At(ItemPlace.Bag, free.Value));
        }

        /// <summary>Moves a bag item to another bag slot, swapping with whatever is there.</summary>
        public static InventoryPlan Move(BagConfig bag, IReadOnlyList<OwnedItem> items, int from, int to)
        {
            if (from < 0 || from >= bag.Slots || to < 0 || to >= bag.Slots || from == to)
            {
                return InventoryPlan.Refuse(InventoryProblem.BadSlot);
            }

            OwnedItem moving = items.FirstOrDefault(item => item.Place == ItemPlace.Bag && item.Slot == from);

            if (moving.Id == 0)
            {
                return InventoryPlan.Refuse(InventoryProblem.NotFound);
            }

            OwnedItem there = items.FirstOrDefault(item => item.Place == ItemPlace.Bag && item.Slot == to);
            return there.Id == 0
                ? InventoryPlan.Move(moving.At(ItemPlace.Bag, to))
                : InventoryPlan.Move(moving.At(ItemPlace.Bag, to), there.At(ItemPlace.Bag, from));
        }

        /// <summary>Takes an item out of the bag to drop it on the ground. Worn items are taken off first.</summary>
        public static InventoryPlan Drop(IReadOnlyList<OwnedItem> items, long id)
        {
            if (!TryFind(items, id, out OwnedItem item))
            {
                return InventoryPlan.Refuse(InventoryProblem.NotFound);
            }

            return item.Place == ItemPlace.Bag ? InventoryPlan.Remove(id) : InventoryPlan.Refuse(InventoryProblem.NotInBag);
        }

        /// <summary>The items after a plan, as the server and the client keep them.</summary>
        public static List<OwnedItem> Apply(IEnumerable<OwnedItem> items, InventoryPlan plan)
        {
            var moved = plan.Moved.ToDictionary(item => item.Id);
            var removed = new HashSet<long>(plan.Removed);
            return items
                .Where(item => !removed.Contains(item.Id))
                .Select(item => moved.TryGetValue(item.Id, out OwnedItem now) ? now : item)
                .ToList();
        }

        /// <summary>What others see: the worn weapon, armour and pants; the default stands in for one missing.</summary>
        public static Appearance AppearanceOf(GameConfig config, IEnumerable<OwnedItem> items)
        {
            Appearance appearance = GearRules.Default(config);

            foreach (OwnedItem item in items)
            {
                if (item.Place == ItemPlace.Worn && item.Item >= 0 && item.Item < config.Gear.Count)
                {
                    GearSlot slot = config.Gear[item.Item].Slot;

                    if (GearRules.IsShown(slot) && item.Slot == (int)slot)
                    {
                        appearance = appearance.With(slot, item.Item);
                    }
                }
            }

            return appearance;
        }

        /// <summary>The clothes a new character wears and the starter items in its bag, before they have ids.</summary>
        public static IReadOnlyList<OwnedItem> Starter(GameConfig config)
        {
            var items = new List<OwnedItem>();
            Appearance clothes = GearRules.Default(config);

            foreach (GearSlot slot in GearRules.ShownSlots)
            {
                items.Add(new OwnedItem(0, clothes[slot], 1, ItemPlace.Worn, (int)slot));
            }

            for (int index = 0; index < config.Bag.StarterBag.Count; index++)
            {
                items.Add(new OwnedItem(0, config.Bag.StarterBag[index], 1, ItemPlace.Bag, index));
            }

            return items;
        }

        /// <summary>Whether feet at one point reach an item on the ground at another.</summary>
        public static bool InReach(BagConfig bag, double feetX, double feetY, double itemX, double itemY)
        {
            double dx = feetX - itemX;
            double dy = feetY - itemY;
            return dx * dx + dy * dy <= bag.PickupRange * bag.PickupRange;
        }

        public static string Message(InventoryProblem problem)
        {
            switch (problem)
            {
                case InventoryProblem.None: return string.Empty;
                case InventoryProblem.NotFound: return "Không tìm thấy món đồ này.";
                case InventoryProblem.NotInBag: return "Món này phải ở trong túi.";
                case InventoryProblem.NotWearable: return "Món này không mặc được.";
                case InventoryProblem.BagFull: return "Túi đã đầy.";
                case InventoryProblem.MustStayWorn: return "Vũ khí, áo và quần không tháo được; hãy mặc món khác thay vào.";
                case InventoryProblem.NothingWorn: return "Ô này đang trống.";
                case InventoryProblem.BadSlot: return "Ô không hợp lệ.";
                case InventoryProblem.TooFar: return "Hãy lại gần hơn để nhặt.";
                case InventoryProblem.Gone: return "Món đồ đã không còn.";
                case InventoryProblem.Busy: return "Đang xử lý, thử lại sau một chút.";
                default: return "Không làm được.";
            }
        }

        private static bool TryFind(IReadOnlyList<OwnedItem> items, long id, out OwnedItem found)
        {
            foreach (OwnedItem item in items)
            {
                if (item.Id == id)
                {
                    found = item;
                    return true;
                }
            }

            found = default;
            return false;
        }
    }
}
