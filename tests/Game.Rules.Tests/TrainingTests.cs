using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Game.Rules.Tests
{
    /// <summary>The straw dummies of the village training yard and the channel's weather.</summary>
    public class TrainingTests
    {
        private static readonly RoomIntent Stand = new RoomIntent(new MovementIntent(0d, 0d, false), new CombatIntent(null));
        private static readonly RoomIntent Attack = new RoomIntent(new MovementIntent(0d, 0d, false), new CombatIntent(SkillSlot.Attack));

        private readonly GameConfig config = TestConfig.Load();

        private int Village => config.MapIndex("map.village");
        private TrainingConfig Training => config.Maps[Village].Training!;

        private static ServerConfig Server()
        {
            return GameConfig.ParseServer(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "config", GameConfig.ServerFile)));
        }

        private static void Run(Room room, int playerId, RoomIntent intent, int ticks)
        {
            var intents = new Dictionary<int, RoomIntent> { [playerId] = intent };

            for (int tick = 0; tick < ticks; tick++)
            {
                room.Step(intents);
            }
        }

        /// <summary>A player just left of a dummy, facing it, close enough for a plain attack.</summary>
        private RoomPlayer NextTo(Room room, int playerId, int dummy, GameConfig? custom = null)
        {
            GameConfig used = custom ?? config;
            return room.Join(playerId, used.Maps[Village].Training!.Dummies[dummy].X - 0.5d, Village)!;
        }

        /// <summary>Attacks once and waits for the swing to finish.</summary>
        private static void Swing(Room room, int playerId)
        {
            Run(room, playerId, Attack, 1);
            Run(room, playerId, Stand, Ticks.PerSecond);
        }

        /// <summary>Swings once a second until the dummy falls, then stops, so its stand-up countdown starts here.</summary>
        private void KnockDown(Room room, int playerId, int dummy)
        {
            for (int tick = 0; tick < Ticks.PerSecond * 30 && !room.Yard(Village)![dummy].Fallen; tick++)
            {
                Run(room, playerId, tick % Ticks.PerSecond == 0 ? Attack : Stand, 1);
            }

            Assert.True(room.Yard(Village)![dummy].Fallen);
        }

        [Fact]
        public void TheVillageHasFiveDummiesOfAHundredHpThatLoseTenABlowAndStandAfterTenSeconds()
        {
            Assert.Equal(5, Training.Dummies.Count);
            Assert.Equal(100, Training.MaxHp);
            Assert.Equal(10, Training.DamagePerHit);
            Assert.Equal(10, Training.HitsToFall);
            Assert.Equal(10d, Training.RespawnSec);
            Assert.True(config.Maps[Village].Shared);
            Assert.False(config.Maps[Village].Boss);
            Assert.Null(config.Maps[config.MapIndex("map.house")].Training);
        }

        [Fact]
        public void OneBlowLandsOnTheNearestDummyOnly()
        {
            var room = new Room(1, config, Server());
            NextTo(room, 1, 2);

            Swing(room, 1);

            IReadOnlyList<DummyState> yard = room.Yard(Village)!;
            Assert.Equal(1, yard[2].Hits);
            Assert.False(yard[2].Fallen);
            Assert.Equal(1, yard.Sum(dummy => dummy.Hits));
        }

        [Fact]
        public void TenBlowsKnockADummyDownAndNineDoNot()
        {
            var room = new Room(1, config, Server());
            NextTo(room, 1, 4);
            DummyState dummy = room.Yard(Village)![4];

            for (int blow = 0; blow < 9; blow++)
            {
                Swing(room, 1);
            }

            Assert.False(dummy.Fallen);
            Assert.Equal(9, dummy.Hits);
            Assert.Equal(10, TrainingRules.Hp(Training, dummy.Hits));

            Swing(room, 1);
            Assert.True(dummy.Fallen);
        }

        [Fact]
        public void AFallenDummyStandsUpTenSecondsAfterItFell()
        {
            var room = new Room(1, config, Server());
            NextTo(room, 1, 0);
            KnockDown(room, 1, 0);

            DummyState dummy = room.Yard(Village)![0];
            Assert.True(dummy.Fallen);

            Run(room, 1, Stand, Ticks.FromSeconds(10d) - 1);
            Assert.True(dummy.Fallen);

            Run(room, 1, Stand, 1);
            Assert.False(dummy.Fallen);
            Assert.Equal(0, dummy.Hits);
            Assert.Equal(0, dummy.TicksSinceChange);
        }

        [Fact]
        public void AFallenDummyTakesNoMoreBlowsAndTheNextOneIsTooFarToHit()
        {
            var room = new Room(1, config, Server());
            NextTo(room, 1, 0);

            KnockDown(room, 1, 0);
            Swing(room, 1);
            Swing(room, 1);

            IReadOnlyList<DummyState> yard = room.Yard(Village)!;
            Assert.True(yard[0].Fallen);
            Assert.Equal(Training.HitsToFall, yard[0].Hits);
            Assert.Equal(0, yard[1].Hits);
            Assert.False(yard[1].Fallen);
        }

        [Fact]
        public void AYardOfTwentyFiveHpFallsOnTheThirdBlowOfTen()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.VillageFile] = files[GameConfig.VillageFile].Replace("\"maxHp\": 100", "\"maxHp\": 25");
            GameConfig three = GameConfig.Parse(files);
            var room = new Room(1, three, Server());
            NextTo(room, 1, 3, three);

            Swing(room, 1);
            Swing(room, 1);
            DummyState dummy = room.Yard(Village)![3];
            Assert.False(dummy.Fallen);
            Assert.Equal(2, dummy.Hits);
            Assert.Equal(5, TrainingRules.Hp(three.Maps[Village].Training!, dummy.Hits));

            Swing(room, 1);
            Assert.True(dummy.Fallen);
        }

        [Fact]
        public void ADummyThatWouldNeedMoreThan255BlowsIsRefused()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.VillageFile] = files[GameConfig.VillageFile].Replace("\"maxHp\": 100", "\"maxHp\": 2560");

            Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        [Fact]
        public void DummiesNeverHitBack()
        {
            var room = new Room(1, config, Server());
            RoomPlayer player = NextTo(room, 1, 1);

            Run(room, 1, Stand, Ticks.PerSecond * 5);

            Assert.Equal(config.Character.MaxHp, player.Resources.Hp);
        }

        [Fact]
        public void AYardWithoutDummiesIsRefused()
        {
            Dictionary<string, string> files = TestConfig.Files();
            string village = files[GameConfig.VillageFile];
            int start = village.IndexOf("\"dummies\"");
            int end = village.IndexOf(']', start);
            files[GameConfig.VillageFile] = village.Substring(0, start) + "\"dummies\": [" + village.Substring(end);

            Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        [Fact]
        public void TheWeatherHoldsBetweenTheConfiguredTimesAndIsOneOfFourKinds()
        {
            var weather = new WeatherState(3, 480d, 900d);
            int held = 0;
            var seen = new HashSet<Weather> { weather.Now };

            Assert.Equal(Weather.Clear, weather.Now);
            Assert.InRange(weather.TicksLeft, Ticks.FromSeconds(480d), Ticks.FromSeconds(900d));

            for (int change = 0; change < 200; change++)
            {
                int left = weather.TicksLeft;

                for (int tick = 0; tick < left; tick++)
                {
                    weather.Step();
                    held++;
                }

                seen.Add(weather.Now);
                Assert.InRange(weather.TicksLeft, Ticks.FromSeconds(480d), Ticks.FromSeconds(900d));
            }

            Assert.Equal(4, seen.Count);
            Assert.True(held > 0);
        }

        [Fact]
        public void ChannelsWithTheSameIdHaveTheSameWeather()
        {
            var first = new WeatherState(7, 1d, 2d);
            var second = new WeatherState(7, 1d, 2d);

            for (int tick = 0; tick < Ticks.PerSecond * 30; tick++)
            {
                first.Step();
                second.Step();
                Assert.Equal(first.Now, second.Now);
            }
        }
    }
}
