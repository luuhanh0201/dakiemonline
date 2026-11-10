using System;
using System.Collections.Generic;
using Game.Rules;

namespace Game.Net
{
    /// <summary>
    /// Moves and casts for the local player at once, then puts it where the server says and replays
    /// the inputs the server has not used yet. Both sides run <see cref="Room.StepPlayer"/>, so
    /// normally nothing changes. Damage the prediction deals is thrown away: only the server's counts.
    /// </summary>
    public sealed class Predictor
    {
        /// <summary>About three seconds; if the server is this far behind, the oldest inputs are given up.</summary>
        public const int MaxPending = 90;

        private readonly GameConfig config;
        private readonly CollisionMap map;
        private readonly bool testRoom;
        private readonly List<InputMessage> pending = new List<InputMessage>();
        private readonly List<CombatEvent> events = new List<CombatEvent>();

        public Predictor(GameConfig config, PlayerSnapshot start, uint lastSequence, bool testRoom)
        {
            this.config = config;
            this.testRoom = testRoom;
            map = new CollisionMap(config.MapBoxes);
            Movement = MovementState.FromSnapshot(start.Movement);
            Combat = new CombatState(config.Potion);
            Resources = new ResourceState(config.Character);
            Restore(start);
            LastSequence = lastSequence;
        }

        public MovementState Movement { get; }
        public CombatState Combat { get; }
        public ResourceState Resources { get; }

        /// <summary>Ticks until the server brings a dead player back; 0 while alive.</summary>
        public int RespawnTicksLeft { get; private set; }

        /// <summary>The boss as the server last showed it, which skills aim at while predicting.</summary>
        public BossState? Boss { get; set; }

        public uint LastSequence { get; private set; }
        public int PendingCount => pending.Count;

        /// <summary>How far the last <see cref="Reconcile"/> moved the player, in map units.</summary>
        public double LastCorrection { get; private set; }

        /// <summary>What the last <see cref="Advance"/> predicted, such as a skill starting; damage here is not real.</summary>
        public IReadOnlyList<CombatEvent> PredictedEvents => events;

        /// <summary>Applies one tick of input locally and returns the message to send.</summary>
        public InputMessage Advance(double moveX, double moveY, bool dash, SkillSlot? skill = null, bool potion = false, bool refill = false)
        {
            LastSequence++;
            InputMessage input = InputMessage.FromJoystick(LastSequence, moveX, moveY, dash, skill, potion, refill && testRoom);

            if (pending.Count == MaxPending)
            {
                pending.RemoveAt(0);
            }

            pending.Add(input);
            events.Clear();
            Step(input, events);
            return input;
        }

        public void Reconcile(uint ackSequence, PlayerSnapshot server)
        {
            pending.RemoveAll(input => !IsAfter(input.Sequence, ackSequence));

            double oldX = Movement.X;
            double oldY = Movement.Y;
            Restore(server);
            var replayed = new List<CombatEvent>();

            foreach (InputMessage input in pending)
            {
                Step(input, replayed);
            }

            double dx = Movement.X - oldX;
            double dy = Movement.Y - oldY;
            LastCorrection = Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Sequence numbers wrap after four billion ticks; compare them as a circle.</summary>
        public static bool IsAfter(uint sequence, uint reference) => (int)(sequence - reference) > 0;

        private void Step(InputMessage input, List<CombatEvent> output)
        {
            Room.StepPlayer(Movement, Combat, Resources, input.ToIntent(), config, map, Boss, testRoom, output);
        }

        private void Restore(PlayerSnapshot snapshot)
        {
            Movement.Restore(snapshot.Movement);
            Combat.Restore(snapshot.Combat, config);
            Resources.Restore(snapshot.Resources);
            RespawnTicksLeft = snapshot.RespawnTicksLeft;
        }
    }
}
