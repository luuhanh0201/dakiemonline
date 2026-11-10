using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>One player in a channel. Milestone 1 players can only move.</summary>
    public sealed class ChannelPlayer
    {
        internal ChannelPlayer(int id, MovementState movement)
        {
            Id = id;
            Movement = movement;
        }

        public int Id { get; }
        public MovementState Movement { get; }
    }

    /// <summary>One copy of a map with the players in it, advanced a tick at a time.</summary>
    public sealed class Channel
    {
        private readonly GameConfig config;
        private readonly CollisionMap map;
        private readonly SortedDictionary<int, ChannelPlayer> players = new SortedDictionary<int, ChannelPlayer>();

        public Channel(int id, GameConfig config, int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "must be greater than 0");
            }

            Id = id;
            Capacity = capacity;
            this.config = config;
            map = new CollisionMap(config.MapBoxes);
        }

        public int Id { get; }
        public int Capacity { get; }
        public int Tick { get; private set; }
        public bool IsFull => players.Count >= Capacity;

        /// <summary>In id order, so every machine steps players the same way.</summary>
        public IEnumerable<ChannelPlayer> Players => players.Values;

        public int Count => players.Count;

        public ChannelPlayer? Find(int playerId)
        {
            return players.TryGetValue(playerId, out ChannelPlayer? player) ? player : null;
        }

        /// <summary>Adds a player standing at spawnX, or returns null when the channel is full or the id is taken.</summary>
        public ChannelPlayer? Join(int playerId, double spawnX)
        {
            return Add(playerId, new MovementState(spawnX, 0d, config.Character.MaxStamina));
        }

        /// <summary>Brings a player back exactly as they were, for a reconnect or a channel change.</summary>
        public ChannelPlayer? Rejoin(int playerId, in MovementSnapshot snapshot)
        {
            return Add(playerId, MovementState.FromSnapshot(snapshot));
        }

        public bool Leave(int playerId) => players.Remove(playerId);

        /// <summary>Advances every player one tick. A player with no intent stands still.</summary>
        public void Step(IReadOnlyDictionary<int, MovementIntent> intents)
        {
            Tick++;

            foreach (ChannelPlayer player in players.Values)
            {
                intents.TryGetValue(player.Id, out MovementIntent intent);
                Walking.Step(player.Movement, intent, config, map);
            }
        }

        private ChannelPlayer? Add(int playerId, MovementState movement)
        {
            if (IsFull || players.ContainsKey(playerId))
            {
                return null;
            }

            var player = new ChannelPlayer(playerId, movement);
            players.Add(playerId, player);
            return player;
        }
    }

    /// <summary>One tick for a player who can only move. Server and client prediction both call this.</summary>
    public static class Walking
    {
        public static void Step(MovementState state, in MovementIntent intent, GameConfig config, CollisionMap map)
        {
            Movement.Step(state, intent, config.Movement, map);
            ResourceRules.RegenStamina(state, intent, false, config.Character, config.Movement);
        }
    }
}
