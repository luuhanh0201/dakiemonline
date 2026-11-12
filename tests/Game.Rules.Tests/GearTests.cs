using System.Linq;

namespace Game.Rules.Tests
{
    public class GearTests
    {
        private static readonly GameConfig Config = TestConfig.Load();

        private static int Item(string id) => Config.Gear.ToList().FindIndex(item => item.Id == id);

        private static InvalidConfigException Rejected(string find, string replace)
        {
            var files = TestConfig.Files();
            // A checkout may turn the file's line ends into CRLF; the cases are written with LF.
            files[GameConfig.ItemsFile] = files[GameConfig.ItemsFile].Replace("\r\n", "\n");
            Assert.Contains(find, files[GameConfig.ItemsFile]);
            files[GameConfig.ItemsFile] = files[GameConfig.ItemsFile].Replace(find, replace);
            return Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        [Fact]
        public void CatalogHasTwoTierOneItemsInEachOfTheFiveSlots()
        {
            Assert.Equal(10, Config.Gear.Count);

            foreach (GearSlot slot in new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants, GearSlot.Hat, GearSlot.Cape })
            {
                Assert.Equal(2, Config.Gear.Count(item => item.Slot == slot && item.Tier == 1));
            }
        }

        [Fact]
        public void NewCharactersWearTheClothSet()
        {
            Appearance start = GearRules.Default(Config);

            Assert.Equal(new Appearance(Item("gear.kiem"), Item("gear.aovai"), Item("gear.vai")), start);
            Assert.True(GearRules.IsValid(Config, start));
        }

        [Fact]
        public void WearingAnItemChangesOnlyItsSlot()
        {
            Appearance start = GearRules.Default(Config);

            Assert.True(GearRules.TryWear(Config, start, Item("gear.giap"), out Appearance next));
            Assert.Equal(new Appearance(start.Weapon, Item("gear.giap"), start.Pants), next);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        [InlineData(255)]
        public void UnknownItemsAreRefused(int item)
        {
            Appearance start = GearRules.Default(Config);

            Assert.False(GearRules.TryWear(Config, start, item, out Appearance next));
            Assert.Equal(start, next);
        }

        [Theory]
        [InlineData("gear.non")]
        [InlineData("gear.choang_dai")]
        public void HatsAndCapesDoNotChangeTheLook(string id)
        {
            Appearance start = GearRules.Default(Config);

            Assert.False(GearRules.TryWear(Config, start, Item(id), out Appearance next));
            Assert.Equal(start, next);
        }

        [Fact]
        public void AnAppearanceWithAnItemInTheWrongSlotIsInvalid()
        {
            Appearance start = GearRules.Default(Config);

            Assert.False(GearRules.IsValid(Config, new Appearance(start.Armor, start.Armor, start.Pants)));
            Assert.False(GearRules.IsValid(Config, new Appearance(start.Weapon, start.Armor, 200)));
        }

        [Fact]
        public void NextCyclesThroughTheItemsOfOneSlot()
        {
            Appearance start = GearRules.Default(Config);

            int second = GearRules.Next(Config, start, GearSlot.Pants);
            Assert.Equal(Item("gear.hakama"), second);
            Assert.Equal(Item("gear.vai"), GearRules.Next(Config, start.With(GearSlot.Pants, second), GearSlot.Pants));
        }

        [Theory]
        [InlineData("\"slot\": \"cape\",\n      \"tier\": 1,\n      \"art\": \"dai\"", "\"slot\": \"boots\",\n      \"tier\": 1,\n      \"art\": \"dai\"", "not one of")]
        [InlineData("\"art\": \"nodachi\"", "\"art\": \"kiem\"", "already uses")]
        [InlineData("\"art\": \"nodachi\"", "\"art\": \"No-Dachi\"", "lower-case")]
        [InlineData("\"id\": \"gear.mu\"", "\"id\": \"gear.non\"", "duplicate id")]
        [InlineData("\"art\": \"nodachi\"", "\"art\": \"nodachi\",\n      \"default\": true", "exactly one default weapon")]
        [InlineData("\"art\": \"kiem\",\n      \"default\": true", "\"art\": \"kiem\"", "exactly one default weapon")]
        [InlineData("\"art\": \"mu\"", "\"art\": \"mu\",\n      \"default\": true", "only weapon, armor and pants")]
        [InlineData("\"art\": \"mu\"", "\"art\": \"mu\",\n      \"stats\": {}", "unknown field")]
        [InlineData("\"name\": \"Mũ sắt\"", "\"name\": \" \"", "cannot be empty")]
        public void BadGearIsRejected(string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(find, replace).Message);
        }
    }
}
