using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Game.Rules.Tests
{
    public class RoomTests
    {
        private const int Precision = 6;
        private const double SpawnX = 5d;

        private static readonly MovementIntent Idle = new MovementIntent(0d, 0d, false);
        private static readonly MovementIntent Right = new MovementIntent(1d, 0d, false);
        private static readonly MovementIntent Left = new MovementIntent(-1d, 0d, false);
        private static readonly MovementIntent SwipeUp = new MovementIntent(0d, 1d, false);
        private static readonly MovementIntent Dash = new MovementIntent(1d, 0d, true);
        private static readonly CombatIntent NoSkill = new CombatIntent(null);
        private static readonly CombatIntent Attack = new CombatIntent(SkillSlot.Attack);

        private readonly GameConfig config = TestConfig.Load();

        private static string ServerJson() => File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "config", GameConfig.ServerFile));

        private static ServerConfig Server(int players = 20, bool testRoom = true)
        {
            ServerConfig server = GameConfig.ParseServer(ServerJson());
            server.PlayersPerChannel = players;
            server.TestRoom = testRoom;
            return server;
        }

        private static GameConfig ConfigWith(string file, string find, string replace)
        {
            Dictionary<string, string> files = TestConfig.Files();
            Assert.Contains(find, files[file]);
            files[file] = files[file].Replace(find, replace);
            return GameConfig.Parse(files);
        }

        private Room NewRoom(int players = 20, bool testRoom = true, GameConfig? custom = null)
        {
            return new Room(1, custom ?? config, Server(players, testRoom));
        }

        private static Dictionary<int, RoomIntent> Intents(params (int Id, RoomIntent Intent)[] items)
        {
            return items.ToDictionary(item => item.Id, item => item.Intent);
        }

        private static RoomIntent Move(MovementIntent move) => new RoomIntent(move, NoSkill);

        private static void Run(Room room, Dictionary<int, RoomIntent> intents, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                room.Step(intents);
            }
        }

        /// <summary>Keeps the boss standing still where it is, by giving it nobody alive to chase.</summary>
        private static void PlaceAtBoss(Room room, int playerId, double offset)
        {
            RoomPlayer player = room.Find(playerId)!;
            MovementSnapshot at = player.Movement.Capture();
            player.Movement.Restore(new MovementSnapshot(
                room.Boss.X + offset, 0d, 0d, offset > 0 ? -1 : 1, true, at.Stamina, 0d, 0, 0, 0, 0, 0, 0, 0));
        }

        [Fact]
        public void PlayersMoveOnTheirOwnIntents()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Join(2);

            Run(room, Intents((1, Move(Right)), (2, Move(Left))), Ticks.PerSecond / 2);

            Assert.Equal(SpawnX + 2d, room.Find(1)!.Movement.X, Precision);
            Assert.Equal(SpawnX - 2d, room.Find(2)!.Movement.X, Precision);
            Assert.Equal(Ticks.PerSecond / 2, room.Tick);
        }

        [Fact]
        public void PlayerWithoutIntentStandsStill()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Join(2);

            Run(room, Intents((1, Move(Right))), 10);

            Assert.Equal(SpawnX, room.Find(2)!.Movement.X, Precision);
        }

        [Fact]
        public void RoomsDoNotShareState()
        {
            Room first = NewRoom();
            Room second = NewRoom();
            first.Join(1);
            second.Join(1);

            Run(first, Intents((1, Move(Right))), 10);

            Assert.NotEqual(SpawnX, first.Find(1)!.Movement.X);
            Assert.Equal(SpawnX, second.Find(1)!.Movement.X, Precision);
            Assert.Equal(0, second.Tick);
        }

        [Fact]
        public void FullRoomRefusesAnotherPlayerAndIdsAreUnique()
        {
            Room room = NewRoom(players: 2);

            Assert.NotNull(room.Join(1));
            Assert.Null(room.Join(1));
            Assert.NotNull(room.Join(2));
            Assert.Null(room.Join(3));
            Assert.True(room.Leave(1));
            Assert.NotNull(room.Join(3));
        }

        [Fact]
        public void WallStopsAPlayer()
        {
            Room room = NewRoom();
            room.Join(1);

            Run(room, Intents((1, Move(Left))), Ticks.PerSecond * 2);

            Assert.Equal(config.Movement.BodyWidth / 2d, room.Find(1)!.Movement.X, Precision);
        }

        [Fact]
        public void DashSpendsStaminaThatComesBack()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Step(Intents((1, Move(Dash))));
            double afterDash = room.Find(1)!.Movement.Stamina;

            Run(room, Intents((1, Move(Idle))), Ticks.PerSecond * 3);

            Assert.Equal(config.Character.MaxStamina - config.Movement.DashStaminaCost, afterDash, Precision);
            Assert.True(room.Find(1)!.Movement.Stamina > afterDash);
        }

        [Fact]
        public void OnePlayerRoomPlaysExactlyLikeTheOfflineFight()
        {
            Room room = NewRoom();
            room.Join(1);
            var fight = new Simulation(config, SpawnX);
            (MovementIntent Move, CombatIntent Combat)[] script =
            {
                (Right, Attack), (Right, NoSkill), (Idle, Attack), (SwipeUp, NoSkill), (Right, new CombatIntent(SkillSlot.Ranged)),
                (Idle, NoSkill), (Dash, NoSkill), (Idle, new CombatIntent(SkillSlot.OptionA)), (Left, NoSkill), (Idle, new CombatIntent(null, true)),
            };

            for (int tick = 0; tick < 400 && fight.Result == MatchResult.Playing; tick++)
            {
                (MovementIntent move, CombatIntent combat) = script[tick / 7 % script.Length];
                fight.Step(move, combat);
                room.Step(Intents((1, new RoomIntent(move, combat))));
            }

            RoomPlayer player = room.Find(1)!;
            Assert.Equal(fight.Player.Capture(), player.Movement.Capture());
            Assert.Equal(fight.Combat.Capture(), player.Combat.Capture());
            Assert.Equal(fight.Resources.Capture(), player.Resources.Capture());
            Assert.Equal(fight.Boss.Capture(), room.Boss.Capture());
            Assert.True(room.Boss.Hp < config.Boss.MaxHp, "the script reached the boss");
        }

        [Fact]
        public void TwoPlayersBothDamageTheBoss()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Join(2);
            PlaceAtBoss(room, 1, -1.5d);
            PlaceAtBoss(room, 2, 1.5d);
            var both = Intents((1, new RoomIntent(Idle, Attack)), (2, new RoomIntent(Idle, Attack)));
            int hitsBy1 = 0, hitsBy2 = 0, damage = 0;

            for (int tick = 0; tick < Ticks.PerSecond; tick++)
            {
                room.Step(both);

                foreach (RoomEvent item in room.Events.Where(e => e.Kind == RoomEventKind.Combat && e.Combat.Kind == CombatEventKind.SkillHit))
                {
                    hitsBy1 += item.PlayerId == 1 ? 1 : 0;
                    hitsBy2 += item.PlayerId == 2 ? 1 : 0;
                    damage += item.Combat.Amount;
                }
            }

            Assert.True(hitsBy1 > 0 && hitsBy2 > 0, $"hits {hitsBy1} and {hitsBy2}");
            Assert.Equal(config.Boss.MaxHp - damage, room.Boss.Hp);
        }

        [Fact]
        public void BossChasesTheNearestLivingPlayer()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Join(2);
            PlaceAtBoss(room, 1, -6d);
            PlaceAtBoss(room, 2, 4d);

            room.Step(Intents());
            Assert.Equal(1, room.Boss.Facing);

            PlaceAtBoss(room, 1, -3d);
            PlaceAtBoss(room, 2, 5d);
            room.Step(Intents());
            Assert.Equal(-1, room.Boss.Facing);
        }

        [Fact]
        public void BossAttackHitsEveryoneInsideItOnce()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Join(2);
            room.Join(3);
            PlaceAtBoss(room, 1, -1d);
            PlaceAtBoss(room, 2, -1.5d);
            PlaceAtBoss(room, 3, -8d);
            var hit = new List<int>();

            for (int tick = 0; tick < Ticks.PerSecond * 2 && hit.Count == 0; tick++)
            {
                room.Step(Intents());
                hit.AddRange(room.Events.Where(e => e.Kind == RoomEventKind.Boss && e.Boss.Kind == BossEventKind.PlayerHit).Select(e => e.PlayerId));
            }

            Assert.Equal(new[] { 1, 2 }, hit.OrderBy(id => id));
            int damage = config.Boss.Cycle[0].Damage;
            Assert.Equal(config.Character.MaxHp - damage, room.Find(1)!.Resources.Hp);
            Assert.Equal(config.Character.MaxHp - damage, room.Find(2)!.Resources.Hp);
            Assert.Equal(config.Character.MaxHp, room.Find(3)!.Resources.Hp);
        }

        [Fact]
        public void DeadPlayerComesBackAtTheSpawnAfterTheConfiguredTime()
        {
            GameConfig weak = ConfigWith(GameConfig.CharacterFile, "\"maxHp\": 1000", "\"maxHp\": 100");
            Room room = NewRoom(custom: weak);
            room.Join(1);
            PlaceAtBoss(room, 1, -1d);
            int diedAt = -1;

            for (int tick = 0; tick < Ticks.PerSecond * 3 && diedAt < 0; tick++)
            {
                room.Step(Intents());
                diedAt = room.Events.Any(e => e.Kind == RoomEventKind.PlayerDied && e.PlayerId == 1) ? room.Tick : -1;
            }

            Assert.True(diedAt > 0, "the boss killed the player");
            RoomPlayer player = room.Find(1)!;
            Assert.True(player.IsDead);
            int respawnTicks = Ticks.FromSeconds(Server().PlayerRespawnSec);
            Assert.Equal(respawnTicks, player.RespawnTicksLeft);

            Run(room, Intents((1, new RoomIntent(Right, Attack))), respawnTicks - 1);
            Assert.True(player.IsDead, "intents are ignored while dead");

            room.Step(Intents());
            Assert.Contains(room.Events, e => e.Kind == RoomEventKind.PlayerRespawned && e.PlayerId == 1);
            Assert.False(player.IsDead);
            Assert.Equal(SpawnX, player.Movement.X, Precision);
            Assert.Equal(weak.Character.MaxHp, player.Resources.Hp);
            Assert.Equal(weak.Potion.Charges, player.Combat.PotionsLeft);
        }

        [Fact]
        public void BeatenBossComesBackWithFullHealth()
        {
            GameConfig fragile = ConfigWith(GameConfig.BossFile, "\"maxHp\": 5000", "\"maxHp\": 10");
            Room room = NewRoom(custom: fragile);
            room.Join(1);
            PlaceAtBoss(room, 1, -1.5d);
            bool defeated = false;

            for (int tick = 0; tick < Ticks.PerSecond && !defeated; tick++)
            {
                room.Step(Intents((1, new RoomIntent(Idle, Attack))));
                defeated = room.Events.Any(e => e.Kind == RoomEventKind.BossDefeated);
            }

            Assert.True(defeated);
            Assert.True(room.Boss.IsDead);
            int respawnTicks = Ticks.FromSeconds(Server().BossRespawnSec);
            Run(room, Intents(), respawnTicks - 1);
            Assert.True(room.Boss.IsDead);

            room.Step(Intents());
            Assert.Contains(room.Events, e => e.Kind == RoomEventKind.BossRespawned);
            Assert.Equal(fragile.Boss.MaxHp, room.Boss.Hp);
        }

        [Fact]
        public void RefillWorksOnlyInATestRoom()
        {
            foreach (bool testRoom in new[] { true, false })
            {
                Room room = NewRoom(testRoom: testRoom);
                room.Join(1);
                room.Step(Intents((1, Move(Dash))));
                Run(room, Intents((1, Move(Idle))), 7);
                room.Step(Intents((1, new RoomIntent(Idle, new CombatIntent(SkillSlot.Ranged)))));
                RoomPlayer player = room.Find(1)!;
                Assert.NotNull(player.Combat.Casting);
                Run(room, Intents((1, Move(Idle))), 12);
                Assert.Null(player.Combat.Casting);
                Run(room, Intents((1, new RoomIntent(Idle, new CombatIntent(null, true)))), 3);
                Assert.True(player.Movement.Stamina < config.Character.MaxStamina);
                Assert.True(player.Combat.PotionsLeft < config.Potion.Charges);

                room.Step(Intents((1, new RoomIntent(Idle, NoSkill, refill: true))));

                Assert.Equal(testRoom, room.Events.Any(e => e.Kind == RoomEventKind.Refilled));
                Assert.Equal(testRoom, player.Combat.PotionsLeft == config.Potion.Charges);
                Assert.Equal(testRoom, player.Movement.Stamina == config.Character.MaxStamina);
                Assert.Equal(testRoom, player.Resources.Energy == config.Character.MaxEnergy);
                Assert.True(player.Combat.CooldownSecondsLeft(SkillSlot.Ranged) > 0d, "cooldowns stay");
            }
        }

        [Fact]
        public void RejoinRestoresEverythingMidCast()
        {
            Room room = NewRoom();
            room.Join(1);
            room.Step(Intents((1, new RoomIntent(Idle, new CombatIntent(SkillSlot.Ranged)))));
            Run(room, Intents((1, new RoomIntent(Right, NoSkill))), 9);
            PlayerSnapshot saved = room.Find(1)!.Capture();
            Assert.NotEmpty(saved.Combat.Projectiles);
            room.Leave(1);

            Room other = NewRoom();
            other.Rejoin(1, saved);

            Assert.Equal(saved, other.Find(1)!.Capture());
        }

        [Fact]
        public void RestoredPlayerCarriesOnLikeTheOriginal()
        {
            var map = new CollisionMap(config.MapBoxes);
            var boss = new BossState(config.Boss, 9d);
            var movement = new MovementState(SpawnX, 0d, config.Character.MaxStamina);
            var combat = new CombatState(config.Potion);
            var resources = new ResourceState(config.Character);
            var events = new List<CombatEvent>();
            Room.StepPlayer(movement, combat, resources, new RoomIntent(Idle, new CombatIntent(SkillSlot.Ultimate)), config, map, boss, true, events);
            Room.StepPlayer(movement, combat, resources, new RoomIntent(Right, NoSkill), config, map, boss, true, events);

            var copyMovement = MovementState.FromSnapshot(movement.Capture());
            var copyCombat = new CombatState(config.Potion);
            copyCombat.Restore(combat.Capture(), config);
            var copyResources = new ResourceState(config.Character);
            copyResources.Restore(resources.Capture());

            for (int tick = 0; tick < 60; tick++)
            {
                RoomIntent intent = new RoomIntent(tick % 20 < 10 ? Right : Idle, tick == 40 ? Attack : NoSkill);
                Room.StepPlayer(movement, combat, resources, intent, config, map, boss, true, events);
                Room.StepPlayer(copyMovement, copyCombat, copyResources, intent, config, map, boss, true, new List<CombatEvent>());
            }

            Assert.Equal(movement.Capture(), copyMovement.Capture());
            Assert.Equal(combat.Capture(), copyCombat.Capture());
            Assert.Equal(resources.Capture(), copyResources.Capture());
        }

        [Fact]
        public void AnotherPlayersCastIsShownAtTheSameMoment()
        {
            var map = new CollisionMap(config.MapBoxes);
            var movement = new MovementState(SpawnX, 0d, config.Character.MaxStamina);
            var combat = new CombatState(config.Potion);
            var resources = new ResourceState(config.Character);
            var events = new List<CombatEvent>();
            Room.StepPlayer(movement, combat, resources, new RoomIntent(Idle, new CombatIntent(SkillSlot.Ultimate)), config, map, null, true, events);

            while (combat.Casting != null)
            {
                CombatSnapshot real = combat.Capture();
                CombatState shown = CombatState.Showing(config, real.Casting, real.CastTicks, real.ComboStep, real.Direction);

                Assert.Equal(combat.Phase, shown.Phase);
                Assert.Equal(combat.ActiveHitIndex, shown.ActiveHitIndex);
                Assert.Equal(combat.ActiveHitCount, shown.ActiveHitCount);
                Assert.Equal(combat.ActiveHitProgress, shown.ActiveHitProgress);
                Room.StepPlayer(movement, combat, resources, new RoomIntent(Idle, NoSkill), config, map, null, true, events);
            }
        }

        [Fact]
        public void BossSnapshotRoundTrips()
        {
            Room room = NewRoom();
            room.Join(1);
            PlaceAtBoss(room, 1, -1d);
            Run(room, Intents(), 5);
            BossSnapshot saved = room.Boss.Capture();

            BossState copy = BossState.FromSnapshot(config.Boss, saved);

            Assert.Equal(saved, copy.Capture());
            Assert.Equal(BossAi.WarningZone(room.Boss, config.Boss), BossAi.WarningZone(copy, config.Boss));
        }

        [Fact]
        public void ServerFileLoads()
        {
            ServerConfig server = GameConfig.ParseServer(ServerJson());

            Assert.Equal("server.default", server.Id);
            Assert.Equal(ConfigStatus.Thu, server.Status);
            Assert.Equal(20, server.PlayersPerChannel);
            Assert.Equal(5d, server.PlayerRespawnSec);
            Assert.Equal(10d, server.BossRespawnSec);
            Assert.True(server.TestRoom);
            Assert.Equal((1, "Thủy Nguyệt", true), (server.ServerNumber, server.ServerName, server.ServerIsNew));
        }

        [Theory]
        [InlineData("\"serverNumber\": 1", "\"serverNumber\": 0", "serverNumber")]
        [InlineData("\"channels\": 2", "\"channels\": 0", "channels")]
        [InlineData("\"snapshotEveryTicks\": 2", "\"snapshotEveryTicks\": \"2\"", "snapshotEveryTicks")]
        [InlineData("\"reconnectHoldSec\": 30", "\"reconnectHoldSec\": -1", "reconnectHoldSec")]
        [InlineData("\"playerRespawnSec\": 5", "\"playerRespawnSec\": 0", "playerRespawnSec")]
        [InlineData("\"testRoom\": true", "\"testRoom\": 1", "testRoom")]
        [InlineData("\"maxQueuedInputs\": 6", "\"maxQueuedInputs\": 6, \"extra\": 1", "extra")]
        public void BadServerFileIsRejected(string find, string replace, string field)
        {
            string json = ServerJson();
            Assert.Contains(find, json);

            InvalidConfigException error = Assert.Throws<InvalidConfigException>(
                () => GameConfig.ParseServer(json.Replace(find, replace)));

            Assert.Equal(GameConfig.ServerFile, error.File);
            Assert.Contains(field, error.FieldPath);
        }
    }
}
