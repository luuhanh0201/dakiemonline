using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Tests
{
    public class InventoryTests
    {
        private static readonly GameConfig Config = TestConfig.Load();
        private static BagConfig Bag => Config.Bag;

        private static int Item(string id) => Config.Gear.ToList().FindIndex(item => item.Id == id);

        /// <summary>The starter items with ids 1, 2, 3...: worn cloth set, then the bag.</summary>
        private static List<OwnedItem> Starter()
        {
            long id = 0;
            return InventoryRules.Starter(Config).Select(item => new OwnedItem(++id, item.Item, item.Quantity, item.Place, item.Slot)).ToList();
        }

        private static OwnedItem Find(IEnumerable<OwnedItem> items, string id) => items.Single(item => item.Item == Item(id));

        [Fact]
        public void BagHasThirtySlotsAndItemsLieFiveMinutes()
        {
            Assert.Equal(30, Bag.Slots);
            Assert.Equal(300d, Bag.GroundLifetimeSec);
            Assert.Equal(ConfigStatus.Thu, Bag.Status);
        }

        [Fact]
        public void NewCharacterWearsTheClothSetAndCarriesTheRest()
        {
            List<OwnedItem> items = Starter();

            Assert.Equal(GearRules.Default(Config), InventoryRules.AppearanceOf(Config, items));
            Assert.Equal(3, items.Count(item => item.Place == ItemPlace.Worn));
            Assert.Equal(
                new[] { "gear.nodachi", "gear.giap", "gear.hakama", "gear.non", "gear.choang_ngan" },
                items.Where(item => item.Place == ItemPlace.Bag).OrderBy(item => item.Slot).Select(item => Config.Gear[item.Item].Id));
        }

        [Fact]
        public void WearingSwapsWithWhatWasWornThere()
        {
            List<OwnedItem> items = Starter();
            OwnedItem giap = Find(items, "gear.giap");

            InventoryPlan plan = InventoryRules.Wear(Config, items, giap.Id);
            List<OwnedItem> after = InventoryRules.Apply(items, plan);

            Assert.True(plan.Accepted);
            Assert.Equal(ItemPlace.Worn, Find(after, "gear.giap").Place);
            Assert.Equal((int)GearSlot.Armor, Find(after, "gear.giap").Slot);
            Assert.Equal(new OwnedItem(Find(items, "gear.aovai").Id, Item("gear.aovai"), 1, ItemPlace.Bag, giap.Slot), Find(after, "gear.aovai"));
            Assert.Equal(Item("gear.giap"), InventoryRules.AppearanceOf(Config, after).Armor);
        }

        [Fact]
        public void HatGoesOnAnEmptySlotWithoutChangingTheLook()
        {
            List<OwnedItem> items = Starter();
            List<OwnedItem> after = InventoryRules.Apply(items, InventoryRules.Wear(Config, items, Find(items, "gear.non").Id));

            Assert.Equal(ItemPlace.Worn, Find(after, "gear.non").Place);
            Assert.Equal(InventoryRules.AppearanceOf(Config, items), InventoryRules.AppearanceOf(Config, after));
        }

        [Fact]
        public void HatComesOffIntoTheFirstFreeSlot()
        {
            List<OwnedItem> items = Starter();
            items = InventoryRules.Apply(items, InventoryRules.Wear(Config, items, Find(items, "gear.non").Id));

            InventoryPlan plan = InventoryRules.TakeOff(Bag, items, GearSlot.Hat);

            Assert.True(plan.Accepted);
            Assert.Equal(InventoryRules.FreeBagSlot(Bag, items), Find(InventoryRules.Apply(items, plan), "gear.non").Slot);
        }

        [Theory]
        [InlineData(GearSlot.Weapon)]
        [InlineData(GearSlot.Armor)]
        [InlineData(GearSlot.Pants)]
        public void ShownClothesStayOn(GearSlot slot)
        {
            Assert.Equal(InventoryProblem.MustStayWorn, InventoryRules.TakeOff(Bag, Starter(), slot).Problem);
        }

        [Fact]
        public void TakingOffNeedsAFreeSlot()
        {
            var items = new List<OwnedItem> { new OwnedItem(1, Item("gear.mu"), 1, ItemPlace.Worn, (int)GearSlot.Hat) };
            items.AddRange(Enumerable.Range(0, Bag.Slots).Select(slot => new OwnedItem(100 + slot, Item("gear.kiem"), 1, ItemPlace.Bag, slot)));

            Assert.Null(InventoryRules.FreeBagSlot(Bag, items));
            Assert.Equal(InventoryProblem.BagFull, InventoryRules.TakeOff(Bag, items, GearSlot.Hat).Problem);
        }

        [Fact]
        public void TakingOffAnEmptySlotIsRefused()
        {
            Assert.Equal(InventoryProblem.NothingWorn, InventoryRules.TakeOff(Bag, Starter(), GearSlot.Cape).Problem);
        }

        [Fact]
        public void MovingSwapsTwoBagSlots()
        {
            List<OwnedItem> items = Starter();
            OwnedItem first = items.Single(item => item.Place == ItemPlace.Bag && item.Slot == 0);
            OwnedItem second = items.Single(item => item.Place == ItemPlace.Bag && item.Slot == 1);

            List<OwnedItem> after = InventoryRules.Apply(items, InventoryRules.Move(Bag, items, 0, 1));

            Assert.Equal(1, after.Single(item => item.Id == first.Id).Slot);
            Assert.Equal(0, after.Single(item => item.Id == second.Id).Slot);
        }

        [Fact]
        public void MovingIntoAnEmptySlotLeavesTheOldOneFree()
        {
            List<OwnedItem> items = Starter();
            List<OwnedItem> after = InventoryRules.Apply(items, InventoryRules.Move(Bag, items, 0, 29));

            Assert.Contains(after, item => item.Place == ItemPlace.Bag && item.Slot == 29);
            Assert.DoesNotContain(after, item => item.Place == ItemPlace.Bag && item.Slot == 0);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 3)]
        [InlineData(0, 30)]
        [InlineData(20, 21)]
        public void BadMovesAreRefused(int from, int to)
        {
            InventoryPlan plan = InventoryRules.Move(Bag, Starter(), from, to);

            Assert.False(plan.Accepted);
            Assert.Empty(plan.Moved);
        }

        [Fact]
        public void OnlyBagItemsCanBeDropped()
        {
            List<OwnedItem> items = Starter();
            OwnedItem kiem = Find(items, "gear.kiem");
            OwnedItem nodachi = Find(items, "gear.nodachi");

            Assert.Equal(InventoryProblem.NotInBag, InventoryRules.Drop(items, kiem.Id).Problem);
            Assert.Equal(InventoryProblem.NotFound, InventoryRules.Drop(items, 999).Problem);
            Assert.DoesNotContain(InventoryRules.Apply(items, InventoryRules.Drop(items, nodachi.Id)), item => item.Id == nodachi.Id);
        }

        [Fact]
        public void WornItemsCannotBeWornAgain()
        {
            List<OwnedItem> items = Starter();

            Assert.Equal(InventoryProblem.NotInBag, InventoryRules.Wear(Config, items, Find(items, "gear.kiem").Id).Problem);
            Assert.Equal(InventoryProblem.NotFound, InventoryRules.Wear(Config, items, 999).Problem);
        }

        [Theory]
        [InlineData(5.0, 0.0, 5.5, 0.0, true)]
        [InlineData(5.0, 0.0, 6.15, 0.0, true)]
        [InlineData(5.0, 0.0, 6.3, 0.0, false)]
        [InlineData(5.0, 2.0, 5.0, 0.0, false)]
        public void PickingUpNeedsTheFeetNearTheItem(double feetX, double feetY, double itemX, double itemY, bool reach)
        {
            Assert.Equal(reach, InventoryRules.InReach(Bag, feetX, feetY, itemX, itemY));
        }

        [Fact]
        public void EveryProblemHasAMessage()
        {
            foreach (InventoryProblem problem in System.Enum.GetValues(typeof(InventoryProblem)).Cast<InventoryProblem>().Where(problem => problem != InventoryProblem.None))
            {
                Assert.NotEqual("Không làm được.", InventoryRules.Message(problem));
            }
        }

        [Theory]
        [InlineData("\"slots\": 30", "\"slots\": 300", "at most 255")]
        [InlineData("\"slots\": 30", "\"slots\": 4", "for 4 slots")]
        [InlineData("\"gear.choang_ngan\"]", "\"gear.khong_co\"]", "unknown item")]
        [InlineData("\"pickupRange\": 1.2", "\"pickupRange\": 0", "greater than 0")]
        public void BadBagIsRejected(string find, string replace, string problem)
        {
            var files = TestConfig.Files();
            files[GameConfig.ItemsFile] = files[GameConfig.ItemsFile].Replace("\r\n", "\n");
            Assert.Contains(find, files[GameConfig.ItemsFile]);
            files[GameConfig.ItemsFile] = files[GameConfig.ItemsFile].Replace(find, replace);
            Assert.Contains(problem, Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files)).Message);
        }
    }
}
