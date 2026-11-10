using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Game.Rules.Tests
{
    public class ChannelTests
    {
        private const int Precision = 6;
        private const double SpawnX = 5d;

        private static readonly MovementIntent Idle = new MovementIntent(0d, 0d, false);
        private static readonly MovementIntent Right = new MovementIntent(1d, 0d, false);
        private static readonly MovementIntent Left = new MovementIntent(-1d, 0d, false);
        private static readonly MovementIntent SwipeUp = new MovementIntent(0d, 1d, false);
        private static readonly MovementIntent Dash = new MovementIntent(1d, 0d, true);

        private readonly GameConfig config = TestConfig.Load();

        private static Dictionary<int, MovementIntent> Intents(params (int Id, MovementIntent Intent)[] items)
        {
            var intents = new Dictionary<int, MovementIntent>();

            foreach ((int id, MovementIntent intent) in items)
            {
                intents[id] = intent;
            }

            return intents;
        }

        private static void Run(Channel channel, Dictionary<int, MovementIntent> intents, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                channel.Step(intents);
            }
        }

        [Fact]
        public void PlayersInOneChannelMoveOnTheirOwnIntents()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            channel.Join(2, 15d);

            Run(channel, Intents((1, Right), (2, Left)), Ticks.PerSecond);

            Assert.Equal(SpawnX + 4d, channel.Find(1)!.Movement.X, Precision);
            Assert.Equal(15d - 4d, channel.Find(2)!.Movement.X, Precision);
            Assert.Equal(Ticks.PerSecond, channel.Tick);
        }

        [Fact]
        public void PlayersPassThroughEachOther()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            channel.Join(2, SpawnX + 1d);

            Run(channel, Intents((1, Right)), Ticks.PerSecond);

            Assert.Equal(SpawnX + 4d, channel.Find(1)!.Movement.X, Precision);
        }

        [Fact]
        public void PlayerWithoutIntentStandsStill()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            channel.Join(2, 15d);

            Run(channel, Intents((1, Right)), 10);

            Assert.Equal(15d, channel.Find(2)!.Movement.X, Precision);
        }

        [Fact]
        public void ChannelsDoNotShareState()
        {
            var first = new Channel(1, config, 20);
            var second = new Channel(2, config, 20);
            first.Join(1, SpawnX);
            second.Join(1, SpawnX);

            Run(first, Intents((1, Right)), Ticks.PerSecond);

            Assert.Equal(SpawnX + 4d, first.Find(1)!.Movement.X, Precision);
            Assert.Equal(SpawnX, second.Find(1)!.Movement.X, Precision);
            Assert.Equal(0, second.Tick);
        }

        [Fact]
        public void FullChannelRefusesAnotherPlayer()
        {
            var channel = new Channel(1, config, 2);

            Assert.NotNull(channel.Join(1, SpawnX));
            Assert.NotNull(channel.Join(2, SpawnX));
            Assert.Null(channel.Join(3, SpawnX));
            Assert.True(channel.IsFull);
        }

        [Fact]
        public void SameIdCannotJoinTwice()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);

            Assert.Null(channel.Join(1, 10d));
            Assert.Equal(1, channel.Count);
        }

        [Fact]
        public void LeavingFreesTheSlot()
        {
            var channel = new Channel(1, config, 1);
            channel.Join(1, SpawnX);

            Assert.True(channel.Leave(1));
            Assert.NotNull(channel.Join(2, SpawnX));
            Assert.False(channel.Leave(1));
        }

        [Fact]
        public void PlayersStandOnTheMapGround()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);

            Run(channel, Intents((1, Idle)), Ticks.PerSecond);

            Assert.Equal(0d, channel.Find(1)!.Movement.Y, Precision);
            Assert.True(channel.Find(1)!.Movement.Grounded);
        }

        [Fact]
        public void WallStopsAPlayerInTheChannel()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, 2d);

            Run(channel, Intents((1, Left)), Ticks.PerSecond);

            Assert.Equal(config.Movement.BodyWidth / 2d, channel.Find(1)!.Movement.X, Precision);
        }

        [Fact]
        public void DashSpendsAndThenRegeneratesStamina()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            channel.Step(Intents((1, Dash)));

            double afterDash = channel.Find(1)!.Movement.Stamina;
            Assert.Equal(config.Character.MaxStamina - config.Movement.DashStaminaCost, afterDash, Precision);

            Run(channel, Intents((1, Idle)), Ticks.PerSecond * 3);

            Assert.True(channel.Find(1)!.Movement.Stamina > afterDash);
        }

        [Fact]
        public void ChannelMatchesASinglePlayerWalkingAlone()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            var alone = new MovementState(SpawnX, 0d, config.Character.MaxStamina);
            var map = new CollisionMap(config.MapBoxes);
            MovementIntent[] script = { Right, Right, SwipeUp, Right, Dash, Idle, Left, Idle };

            for (int tick = 0; tick < 90; tick++)
            {
                MovementIntent intent = script[tick % script.Length];
                channel.Step(Intents((1, intent)));
                Walking.Step(alone, intent, config, map);
            }

            Assert.Equal(alone.Capture(), channel.Find(1)!.Movement.Capture());
        }

        [Fact]
        public void RejoinRestoresEveryTimer()
        {
            var channel = new Channel(1, config, 20);
            channel.Join(1, SpawnX);
            channel.Step(Intents((1, Dash)));
            MovementSnapshot saved = channel.Find(1)!.Movement.Capture();
            channel.Leave(1);

            var other = new Channel(2, config, 20);
            other.Rejoin(1, saved);

            Assert.Equal(saved, other.Find(1)!.Movement.Capture());
            Assert.True(other.Find(1)!.Movement.IsDashing);
        }

        [Fact]
        public void RestoredStateCarriesOnLikeTheOriginal()
        {
            var map = new CollisionMap(config.MapBoxes);
            var original = new MovementState(SpawnX, 0d, config.Character.MaxStamina);
            Walking.Step(original, SwipeUp, config, map);
            Walking.Step(original, Dash, config, map);

            MovementState copy = MovementState.FromSnapshot(original.Capture());

            for (int tick = 0; tick < 20; tick++)
            {
                Walking.Step(original, Right, config, map);
                Walking.Step(copy, Right, config, map);
            }

            Assert.Equal(original.Capture(), copy.Capture());
        }

        [Fact]
        public void ServerFileLoads()
        {
            string json = File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "config", GameConfig.ServerFile));

            ServerConfig server = GameConfig.ParseServer(json);

            Assert.Equal("server.default", server.Id);
            Assert.Equal(ConfigStatus.Thu, server.Status);
            Assert.Equal(20, server.PlayersPerChannel);
            Assert.True(server.Channels >= 2);
        }

        [Theory]
        [InlineData("\"channels\": 2", "\"channels\": 0", "channels")]
        [InlineData("\"snapshotEveryTicks\": 2", "\"snapshotEveryTicks\": \"2\"", "snapshotEveryTicks")]
        [InlineData("\"reconnectHoldSec\": 30", "\"reconnectHoldSec\": -1", "reconnectHoldSec")]
        [InlineData("\"maxQueuedInputs\": 6", "\"maxQueuedInputs\": 6, \"extra\": 1", "extra")]
        public void BadServerFileIsRejected(string find, string replace, string field)
        {
            string json = File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "config", GameConfig.ServerFile));
            Assert.Contains(find, json);

            InvalidConfigException error = Assert.Throws<InvalidConfigException>(
                () => GameConfig.ParseServer(json.Replace(find, replace)));

            Assert.Equal(GameConfig.ServerFile, error.File);
            Assert.Contains(field, error.FieldPath);
        }
    }
}
