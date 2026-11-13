using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>What one player asks for in one tick of a room.</summary>
    public readonly struct RoomIntent
    {
        public RoomIntent(in MovementIntent move, in CombatIntent combat, bool refill = false, bool door = false)
        {
            Move = move;
            Combat = combat;
            Refill = refill;
            Door = door;
        }

        public MovementIntent Move { get; }
        public CombatIntent Combat { get; }

        /// <summary>The test room's refill button; ignored outside a test room.</summary>
        public bool Refill { get; }

        /// <summary>Goes through the door the player stands at, if any.</summary>
        public bool Door { get; }
    }

    public enum RoomEventKind
    {
        /// <summary>A player's skill, hit, projectile, heal or potion; see <see cref="RoomEvent.Combat"/>.</summary>
        Combat,

        /// <summary>The boss warned or hit someone; see <see cref="RoomEvent.Boss"/>.</summary>
        Boss,
        PlayerDied,
        PlayerRespawned,
        Refilled,
        BossDefeated,
        BossRespawned,

        /// <summary>The player went through a door; they are on another map now.</summary>
        ChangedMap,
    }

    public readonly struct RoomEvent
    {
        public RoomEvent(RoomEventKind kind, int playerId, in CombatEvent combat = default, in BossEvent boss = default)
        {
            Kind = kind;
            PlayerId = playerId;
            Combat = combat;
            Boss = boss;
        }

        public RoomEventKind Kind { get; }

        /// <summary>Whose skill it was, or who the boss hit. 0 for boss-only events.</summary>
        public int PlayerId { get; }

        public CombatEvent Combat { get; }
        public BossEvent Boss { get; }
    }

    /// <summary>One player in a room: body, skills and resources.</summary>
    public sealed class RoomPlayer
    {
        internal RoomPlayer(int id, MovementState movement, CombatState combat, ResourceState resources)
        {
            Id = id;
            Movement = movement;
            Combat = combat;
            Resources = resources;
        }

        public int Id { get; }
        public MovementState Movement { get; private set; }
        public CombatState Combat { get; private set; }
        public ResourceState Resources { get; private set; }

        /// <summary>Which map the player is on, as its place in <see cref="GameConfig.Maps"/>.</summary>
        public int Map { get; internal set; }

        public MapSpot Spot => new MapSpot(Map, Movement.X);

        /// <summary>Ticks until a dead player comes back; 0 while alive.</summary>
        public int RespawnTicksLeft { get; internal set; }

        public bool IsDead => Resources.IsDead;

        public PlayerSnapshot Capture()
        {
            return new PlayerSnapshot(Movement.Capture(), Combat.Capture(), Resources.Capture(), RespawnTicksLeft);
        }

        internal void Reset(MovementState movement, CombatState combat, ResourceState resources)
        {
            Movement = movement;
            Combat = combat;
            Resources = resources;
            RespawnTicksLeft = 0;
        }

        /// <summary>Arrives on another map standing, keeping stamina, facing, health and skills.</summary>
        internal void MoveTo(int map, double x)
        {
            var arrived = new MovementState(x, 0d, Movement.Stamina) { Facing = Movement.Facing };
            Map = map;
            Movement = arrived;
        }
    }

    /// <summary>
    /// One channel: several players fighting one boss on the test map, advanced a tick at a time.
    /// Players do not hurt each other. The dead come back at their map's spawn point; a beaten boss
    /// comes back too. Each player is on one map and doors take them to another.
    /// </summary>
    public sealed class Room
    {
        private readonly GameConfig config;
        private readonly ServerConfig server;
        private readonly CollisionMap[] maps;
        private readonly List<DummyState>?[] yards;
        private readonly SortedDictionary<int, RoomPlayer> players = new SortedDictionary<int, RoomPlayer>();
        private readonly List<RoomEvent> events = new List<RoomEvent>();
        private readonly List<CombatEvent> playerEvents = new List<CombatEvent>();
        private readonly List<BossEvent> bossEvents = new List<BossEvent>();
        private readonly List<BossTarget> targets = new List<BossTarget>();

        public Room(int id, GameConfig config, ServerConfig server)
        {
            Id = id;
            this.config = config;
            this.server = server;
            maps = new CollisionMap[config.Maps.Count];
            yards = new List<DummyState>?[config.Maps.Count];

            for (int index = 0; index < maps.Length; index++)
            {
                maps[index] = new CollisionMap(config.Maps[index].Boxes);
                TrainingConfig? training = config.Maps[index].Training;
                yards[index] = training == null ? null : TrainingRules.NewYard(training);

                if (config.Maps[index].Boss && BossMap == 0 && !config.Maps[0].Boss)
                {
                    BossMap = index;
                }
            }

            StartMap = config.MapIndex(server.StartMap);

            if (StartMap < 0)
            {
                throw new ArgumentException($"start map '{server.StartMap}' is not one of the maps", nameof(server));
            }

            Boss = NewBoss();
            Weather = new WeatherState(id, server.WeatherMinSec, server.WeatherMaxSec);
        }

        public int Id { get; }

        /// <summary>The channel's weather, the same on every map of it.</summary>
        public WeatherState Weather { get; }

        /// <summary>The straw dummies of a map in file order; null for a map without a training yard.</summary>
        public IReadOnlyList<DummyState>? Yard(int map) => map >= 0 && map < yards.Length ? yards[map] : null;

        /// <summary>Where a new character appears, as a place in <see cref="GameConfig.Maps"/>.</summary>
        public int StartMap { get; }

        /// <summary>The map the boss lives on: the first map marked with a boss, else the first map.</summary>
        public int BossMap { get; }

        public int Tick { get; private set; }
        public int Capacity => server.PlayersPerChannel;
        public bool TestRoom => server.TestRoom;
        public bool IsFull => players.Count >= Capacity;
        public int Count => players.Count;
        public BossState Boss { get; private set; }

        /// <summary>Ticks until a beaten boss comes back; 0 while it is alive.</summary>
        public int BossRespawnTicksLeft { get; private set; }

        /// <summary>In id order, so every machine steps players the same way.</summary>
        public IEnumerable<RoomPlayer> Players => players.Values;

        /// <summary>What happened during the last <see cref="Step"/>.</summary>
        public IReadOnlyList<RoomEvent> Events => events;

        public RoomPlayer? Find(int playerId)
        {
            return players.TryGetValue(playerId, out RoomPlayer? player) ? player : null;
        }

        /// <summary>
        /// Adds a fresh player at the start map's spawn point, or where a saved character last stood, on
        /// the ground (T045). A saved X without a map is from before there were doors: the test map.
        /// Returns null when the room is full or the id is taken.
        /// </summary>
        public RoomPlayer? Join(int playerId, double? x = null, int? map = null)
        {
            if (IsFull || players.ContainsKey(playerId))
            {
                return null;
            }

            bool known = x != null && !double.IsNaN(x.Value) && !double.IsInfinity(x.Value);
            int on = map != null && map.Value >= 0 && map.Value < maps.Length ? map.Value : known ? 0 : StartMap;
            MovementState movement = known
                ? new MovementState(x!.Value, 0d, config.Character.MaxStamina)
                : NewMovement(on);
            var player = new RoomPlayer(playerId, movement, new CombatState(config.Potion), new ResourceState(config.Character)) { Map = on };
            players.Add(playerId, player);
            return player;
        }

        /// <summary>Brings a player back exactly as they were.</summary>
        public RoomPlayer? Rejoin(int playerId, PlayerSnapshot snapshot)
        {
            RoomPlayer? player = Join(playerId);

            if (player != null)
            {
                player.Movement.Restore(snapshot.Movement);
                player.Combat.Restore(snapshot.Combat, config);
                player.Resources.Restore(snapshot.Resources);
                player.RespawnTicksLeft = snapshot.RespawnTicksLeft;
            }

            return player;
        }

        public bool Leave(int playerId) => players.Remove(playerId);

        /// <summary>Advances the room one tick. A player with no intent stands still.</summary>
        public void Step(IReadOnlyDictionary<int, RoomIntent> intents)
        {
            Tick++;
            events.Clear();
            CountDownBoss();
            Weather.Step();

            foreach (List<DummyState>? yard in yards)
            {
                if (yard != null)
                {
                    foreach (DummyState dummy in yard)
                    {
                        TrainingRules.Step(dummy);
                    }
                }
            }

            foreach (RoomPlayer player in players.Values)
            {
                if (player.IsDead)
                {
                    CountDownRespawn(player);
                    continue;
                }

                intents.TryGetValue(player.Id, out RoomIntent intent);
                playerEvents.Clear();
                bool withBoss = config.Maps[player.Map].Boss;
                List<DummyState>? yard = yards[player.Map];
                TrainingConfig? training = config.Maps[player.Map].Training;
                int dummy = yard == null ? -1 : TrainingRules.Nearest(yard, player.Movement.X);
                CombatTarget? target = withBoss
                    ? (Boss.IsDead ? (CombatTarget?)null : BossAi.TargetOf(Boss, config.Boss))
                    : dummy >= 0 ? new CombatTarget(TrainingRules.Box(training!, yard![dummy].X), 0m) : (CombatTarget?)null;

                if (StepPlayerAt(player.Movement, player.Combat, player.Resources, intent, config, maps[player.Map], target, TestRoom, playerEvents))
                {
                    events.Add(new RoomEvent(RoomEventKind.Refilled, player.Id));
                }

                foreach (CombatEvent item in playerEvents)
                {
                    events.Add(new RoomEvent(RoomEventKind.Combat, player.Id, item));
                }

                if (withBoss)
                {
                    HitBoss(playerEvents);
                }
                else if (dummy >= 0)
                {
                    HitDummy(yard![dummy], training!, playerEvents);
                }

                UseDoor(player, intent.Door);
            }

            StepBoss();
            MarkNewlyDead();
        }

        /// <summary>
        /// One tick of one living player, the same on the server and in a client's prediction.
        /// Returns true when the refill button was used.
        /// </summary>
        public static bool StepPlayer(
            MovementState movement,
            CombatState combat,
            ResourceState resources,
            in RoomIntent intent,
            GameConfig config,
            CollisionMap map,
            BossState? boss,
            bool testRoom,
            List<CombatEvent> events)
        {
            CombatTarget? target = boss == null || boss.IsDead ? (CombatTarget?)null : BossAi.TargetOf(boss, config.Boss);
            return StepPlayerAt(movement, combat, resources, intent, config, map, target, testRoom, events);
        }

        /// <summary>The same tick, aimed at whatever the player's blows can hit on their map: the boss or a dummy.</summary>
        public static bool StepPlayerAt(
            MovementState movement,
            CombatState combat,
            ResourceState resources,
            in RoomIntent intent,
            GameConfig config,
            CollisionMap map,
            CombatTarget? target,
            bool testRoom,
            List<CombatEvent> events)
        {
            if (resources.IsDead)
            {
                return false;
            }

            bool refill = testRoom && intent.Refill;

            if (refill)
            {
                resources.Hp = config.Character.MaxHp;
                resources.Energy = config.Character.MaxEnergy;
                movement.Stamina = config.Character.MaxStamina;
                combat.PotionsLeft = config.Potion.Charges;
            }

            Combat.Step(combat, movement, resources, intent.Move, intent.Combat, config, map, target, events);
            return refill;
        }

        /// <summary>Every blow that landed counts on the dummy it was aimed at, until it falls.</summary>
        private static void HitDummy(DummyState dummy, TrainingConfig training, List<CombatEvent> hits)
        {
            foreach (CombatEvent hit in hits)
            {
                if (hit.Kind == CombatEventKind.SkillHit)
                {
                    TrainingRules.Hit(dummy, training);
                }
            }
        }

        private void HitBoss(List<CombatEvent> hits)
        {
            if (Boss.IsDead)
            {
                return;
            }

            BossAi.ApplyHits(Boss, hits);

            if (Boss.IsDead)
            {
                BossRespawnTicksLeft = Math.Max(1, Ticks.FromSeconds(server.BossRespawnSec));
                events.Add(new RoomEvent(RoomEventKind.BossDefeated, 0));
            }
        }

        private void StepBoss()
        {
            targets.Clear();

            bool anyBoss = config.Maps[BossMap].Boss;

            foreach (RoomPlayer player in players.Values)
            {
                if (anyBoss && player.Map == BossMap)
                {
                    targets.Add(new BossTarget(player.Id, player.Movement, player.Resources));
                }
            }

            bossEvents.Clear();
            BossAi.Step(Boss, targets, config, maps[BossMap], bossEvents);

            foreach (BossEvent item in bossEvents)
            {
                events.Add(new RoomEvent(RoomEventKind.Boss, item.PlayerId, boss: item));
            }
        }

        private void MarkNewlyDead()
        {
            foreach (RoomPlayer player in players.Values)
            {
                if (player.IsDead && player.RespawnTicksLeft == 0)
                {
                    player.RespawnTicksLeft = Math.Max(1, Ticks.FromSeconds(server.PlayerRespawnSec));
                    events.Add(new RoomEvent(RoomEventKind.PlayerDied, player.Id));
                }
            }
        }

        private void CountDownRespawn(RoomPlayer player)
        {
            if (--player.RespawnTicksLeft > 0)
            {
                return;
            }

            player.Reset(NewMovement(player.Map), new CombatState(config.Potion), new ResourceState(config.Character));
            events.Add(new RoomEvent(RoomEventKind.PlayerRespawned, player.Id));
        }

        /// <summary>
        /// Through the door the player stands at: one at the edge of the map just by walking into it, one in
        /// the middle only when its button was pressed. Nothing happens away from a door.
        /// </summary>
        private void UseDoor(RoomPlayer player, bool pressed)
        {
            if (player.IsDead)
            {
                return;
            }

            MapDoor? door = MapRules.DoorAt(config.Maps[player.Map], player.Movement.X);

            if (door != null && (pressed || door.Walk))
            {
                player.MoveTo(config.MapIndex(door.ToMap), door.ToX);
                events.Add(new RoomEvent(RoomEventKind.ChangedMap, player.Id));
            }
        }

        private void CountDownBoss()
        {
            if (!Boss.IsDead || --BossRespawnTicksLeft > 0)
            {
                return;
            }

            BossRespawnTicksLeft = 0;
            Boss = NewBoss();
            events.Add(new RoomEvent(RoomEventKind.BossRespawned, 0));
        }

        private MovementState NewMovement(int map) => new MovementState(config.Maps[map].SpawnX ?? server.SpawnX, 0d, config.Character.MaxStamina);

        private BossState NewBoss() => new BossState(config.Boss, BossAi.StartX(config.Boss, server.SpawnX));
    }
}
