using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>What one player asks for in one tick of a room.</summary>
    public readonly struct RoomIntent
    {
        public RoomIntent(in MovementIntent move, in CombatIntent combat, bool refill = false)
        {
            Move = move;
            Combat = combat;
            Refill = refill;
        }

        public MovementIntent Move { get; }
        public CombatIntent Combat { get; }

        /// <summary>The test room's refill button; ignored outside a test room.</summary>
        public bool Refill { get; }
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
    }

    /// <summary>
    /// One copy of the test map: several players fighting one boss, advanced a tick at a time.
    /// Players do not hurt each other. The dead come back at the spawn point; so does a beaten boss.
    /// </summary>
    public sealed class Room
    {
        private readonly GameConfig config;
        private readonly ServerConfig server;
        private readonly CollisionMap map;
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
            map = new CollisionMap(config.MapBoxes);
            Boss = NewBoss();
        }

        public int Id { get; }
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

        /// <summary>Adds a fresh player at the spawn point, or returns null when the room is full or the id is taken.</summary>
        public RoomPlayer? Join(int playerId)
        {
            if (IsFull || players.ContainsKey(playerId))
            {
                return null;
            }

            var player = new RoomPlayer(playerId, NewMovement(), new CombatState(config.Potion), new ResourceState(config.Character));
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

            foreach (RoomPlayer player in players.Values)
            {
                if (player.IsDead)
                {
                    CountDownRespawn(player);
                    continue;
                }

                intents.TryGetValue(player.Id, out RoomIntent intent);
                playerEvents.Clear();

                if (StepPlayer(player.Movement, player.Combat, player.Resources, intent, config, map, Boss, TestRoom, playerEvents))
                {
                    events.Add(new RoomEvent(RoomEventKind.Refilled, player.Id));
                }

                foreach (CombatEvent item in playerEvents)
                {
                    events.Add(new RoomEvent(RoomEventKind.Combat, player.Id, item));
                }

                HitBoss(playerEvents);
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

            CombatTarget? target = boss == null || boss.IsDead ? (CombatTarget?)null : BossAi.TargetOf(boss, config.Boss);
            Combat.Step(combat, movement, resources, intent.Move, intent.Combat, config, map, target, events);
            return refill;
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

            foreach (RoomPlayer player in players.Values)
            {
                targets.Add(new BossTarget(player.Id, player.Movement, player.Resources));
            }

            bossEvents.Clear();
            BossAi.Step(Boss, targets, config, map, bossEvents);

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

            player.Reset(NewMovement(), new CombatState(config.Potion), new ResourceState(config.Character));
            events.Add(new RoomEvent(RoomEventKind.PlayerRespawned, player.Id));
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

        private MovementState NewMovement() => new MovementState(server.SpawnX, 0d, config.Character.MaxStamina);

        private BossState NewBoss() => new BossState(config.Boss, BossAi.StartX(config.Boss, server.SpawnX));
    }
}
