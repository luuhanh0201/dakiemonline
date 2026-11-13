using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Game.Rules.Tests
{
    /// <summary>The house map, its door to the test map and the door back.</summary>
    public class MapTests
    {
        private const int Precision = 6;

        private static readonly CombatIntent NoSkill = new CombatIntent(null);
        private static readonly RoomIntent Right = new RoomIntent(new MovementIntent(1d, 0d, false), NoSkill);
        private static readonly RoomIntent Stand = new RoomIntent(new MovementIntent(0d, 0d, false), NoSkill);
        private static readonly RoomIntent Door = new RoomIntent(new MovementIntent(0d, 0d, false), NoSkill, door: true);

        private readonly GameConfig config = TestConfig.Load();

        private int Test => config.MapIndex("map.test");
        private int House => config.MapIndex("map.house");
        private MapConfig HouseMap => config.Maps[House];
        private MapDoor HouseDoor => HouseMap.Doors[0];
        private MapDoor YardDoor => config.Maps[Test].Doors[0];

        private static ServerConfig Server()
        {
            return GameConfig.ParseServer(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "config", GameConfig.ServerFile)));
        }

        private Room NewRoom() => new Room(1, config, Server());

        private static void Run(Room room, int playerId, RoomIntent intent, int ticks)
        {
            var intents = new Dictionary<int, RoomIntent> { [playerId] = intent };

            for (int tick = 0; tick < ticks; tick++)
            {
                room.Step(intents);
            }
        }

        private static void PlaceAt(RoomPlayer player, double x)
        {
            MovementSnapshot at = player.Movement.Capture();
            player.Movement.Restore(new MovementSnapshot(x, 0d, 0d, at.Facing, true, at.Stamina, 0d, 0, 0, 0, 0, 0, 0, 0));
        }

        [Fact]
        public void TheTestMapComesFirstAndTheHouseIsPrivateWithoutABoss()
        {
            Assert.Equal(0, Test);
            Assert.True(House > 0);
            Assert.True(config.Maps[Test].Boss);
            Assert.True(config.Maps[Test].Shared);
            Assert.False(HouseMap.Boss);
            Assert.False(HouseMap.Shared);
            Assert.Same(config.Maps[Test].Boxes, config.MapBoxes);
        }

        [Fact]
        public void EachDoorLeadsToTheOtherMapNextToItsDoor()
        {
            Assert.Equal("map.test", HouseDoor.ToMap);
            Assert.Equal("map.house", YardDoor.ToMap);
            Assert.NotNull(MapRules.DoorAt(config.Maps[Test], HouseDoor.ToX));
            Assert.NotNull(MapRules.DoorAt(HouseMap, YardDoor.ToX));
        }

        [Fact]
        public void ADoorToAMapThatDoesNotExistIsRefused()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.HouseFile] = files[GameConfig.HouseFile].Replace("\"toMap\": \"map.test\"", "\"toMap\": \"map.cave\"");

            var error = Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));

            Assert.Contains("map.cave", error.Message);
        }

        [Fact]
        public void TwoMapsWithOneIdAreRefused()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.HouseFile] = files[GameConfig.HouseFile].Replace("\"id\": \"map.house\"", "\"id\": \"map.test\"");

            Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        [Fact]
        public void AStartMapThatIsNotAMapIsRefused()
        {
            ServerConfig server = Server();
            server.StartMap = "map.cave";

            Assert.Throws<System.ArgumentException>(() => new Room(1, config, server));
        }

        [Fact]
        public void ANewCharacterStartsInTheHouseAtItsSpawnPoint()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;

            Assert.Equal("map.house", Server().StartMap);
            Assert.Equal(House, player.Map);
            Assert.Equal(HouseMap.SpawnX!.Value, player.Movement.X, Precision);
        }

        [Fact]
        public void ASavedSpotIsKeptAndOneSavedBeforeThereWereMapsIsOnTheTestMap()
        {
            Room room = NewRoom();

            Assert.Equal(House, room.Join(1, 3d, House)!.Map);
            Assert.Equal(3d, room.Find(1)!.Movement.X, Precision);
            Assert.Equal(Test, room.Join(2, 12d)!.Map);
            Assert.Equal(House, room.Join(3)!.Map);
        }

        [Fact]
        public void TheDoorTakesThePlayerToTheYardAndBack()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;
            PlaceAt(player, HouseDoor.X);

            Run(room, 1, Door, 1);

            Assert.Equal(Test, player.Map);
            Assert.Equal(HouseDoor.ToX, player.Movement.X, Precision);
            Assert.Contains(room.Events, item => item.Kind == RoomEventKind.ChangedMap && item.PlayerId == 1);

            Run(room, 1, Door, 1);

            Assert.Equal(House, player.Map);
            Assert.Equal(YardDoor.ToX, player.Movement.X, Precision);
        }

        [Fact]
        public void TheDoorDoesNothingFromTooFarAway()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;
            PlaceAt(player, HouseDoor.X - HouseDoor.Reach - 0.1d);

            Run(room, 1, Door, 1);

            Assert.Equal(House, player.Map);
            Assert.DoesNotContain(room.Events, item => item.Kind == RoomEventKind.ChangedMap);
        }

        [Fact]
        public void TheHouseWallsKeepThePlayerInsideWithAnEmptyCornerPastTheDoor()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;

            Run(room, 1, Right, Ticks.PerSecond * 5);

            // The picture is 14.83 units wide; the corner past the door is out of its reach.
            Assert.True(player.Movement.X < 14.83d);
            Assert.True(player.Movement.X > HouseDoor.X + HouseDoor.Reach);
            Assert.Null(MapRules.DoorAt(HouseMap, player.Movement.X));
            Run(room, 1, Door, 1);
            Assert.Equal(House, player.Map);
        }

        [Fact]
        public void BothDoorsNowAreInTheMiddleOfTheirMapsAndNeedTheButton()
        {
            Assert.False(HouseDoor.Walk);
            Assert.False(YardDoor.Walk);
        }

        [Fact]
        public void WalkingPastADoorInTheMiddleOfTheMapDoesNotUseIt()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;
            PlaceAt(player, HouseDoor.X - HouseDoor.Reach - 0.2d);

            Run(room, 1, Right, Ticks.PerSecond / 2);

            Assert.Equal(House, player.Map);
            Assert.True(player.Movement.X > HouseDoor.X + HouseDoor.Reach);
        }

        [Fact]
        public void WalkingIntoADoorAtTheEdgeOfTheMapGoesThroughWithoutTheButton()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.HouseFile] = files[GameConfig.HouseFile].Replace("\"toX\": 8.3, \"walk\": false", "\"toX\": 8.3, \"walk\": true");
            files[GameConfig.MapFile] = files[GameConfig.MapFile].Replace("\"toX\": 10.4", "\"toX\": 9.6");
            GameConfig edge = GameConfig.Parse(files);
            var room = new Room(1, edge, Server());
            RoomPlayer player = room.Join(1)!;
            MapDoor door = edge.Maps[House].Doors[0];
            PlaceAt(player, door.X - door.Reach - 0.2d);

            Run(room, 1, Right, Ticks.PerSecond / 2);

            Assert.True(door.Walk);
            Assert.Equal(Test, player.Map);
            Assert.InRange(player.Movement.X, door.ToX, door.ToX + 2d);
        }

        [Fact]
        public void ADoorThatLandsInsideAWalkThroughDoorIsRefused()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[GameConfig.MapFile] = files[GameConfig.MapFile].Replace("\"toX\": 10.4, \"walk\": false", "\"toX\": 10.4, \"walk\": true");

            var error = Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));

            Assert.Contains("send the player back", error.Message);
        }

        [Fact]
        public void TheBossIgnoresPlayersInTheHouse()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;
            double bossX = room.Boss.X;

            Run(room, 1, Stand, Ticks.PerSecond * 3);

            Assert.Equal(House, player.Map);
            Assert.Equal(bossX, room.Boss.X, Precision);
            Assert.Equal(config.Character.MaxHp, player.Resources.Hp);
        }

        [Fact]
        public void AnAttackInTheHouseNeverReachesTheBoss()
        {
            Room room = NewRoom();
            RoomPlayer player = room.Join(1)!;
            PlaceAt(player, room.Boss.X - 0.5d);
            var attack = new RoomIntent(new MovementIntent(0d, 0d, false), new CombatIntent(SkillSlot.Attack));

            Run(room, 1, attack, Ticks.PerSecond * 2);

            Assert.Equal(config.Boss.MaxHp, room.Boss.Hp);
        }
    }
}
