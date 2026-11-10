using System;
using System.Collections.Generic;
using Game.Rules;

namespace Game.Net
{
    /// <summary>
    /// Moves the local player at once, then puts it where the server says and replays the inputs the
    /// server has not used yet. Both sides run <see cref="Walking.Step"/>, so normally nothing changes.
    /// </summary>
    public sealed class Predictor
    {
        /// <summary>About three seconds; if the server is this far behind, the oldest inputs are given up.</summary>
        public const int MaxPending = 90;

        private readonly GameConfig config;
        private readonly CollisionMap map;
        private readonly List<InputMessage> pending = new List<InputMessage>();

        public Predictor(GameConfig config, in MovementSnapshot start, uint lastSequence)
        {
            this.config = config;
            map = new CollisionMap(config.MapBoxes);
            State = MovementState.FromSnapshot(start);
            LastSequence = lastSequence;
        }

        public MovementState State { get; }
        public uint LastSequence { get; private set; }
        public int PendingCount => pending.Count;

        /// <summary>How far the last <see cref="Reconcile"/> moved the player, in map units.</summary>
        public double LastCorrection { get; private set; }

        /// <summary>Applies one tick of joystick input locally and returns the message to send.</summary>
        public InputMessage Advance(double moveX, double moveY, bool dash)
        {
            LastSequence++;
            InputMessage input = InputMessage.FromJoystick(LastSequence, moveX, moveY, dash);

            if (pending.Count == MaxPending)
            {
                pending.RemoveAt(0);
            }

            pending.Add(input);
            Walking.Step(State, input.ToIntent(), config, map);
            return input;
        }

        public void Reconcile(uint ackSequence, in MovementSnapshot server)
        {
            pending.RemoveAll(input => !IsAfter(input.Sequence, ackSequence));

            double oldX = State.X;
            double oldY = State.Y;
            State.Restore(server);

            foreach (InputMessage input in pending)
            {
                Walking.Step(State, input.ToIntent(), config, map);
            }

            double dx = State.X - oldX;
            double dy = State.Y - oldY;
            LastCorrection = Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Sequence numbers wrap after four billion ticks; compare them as a circle.</summary>
        public static bool IsAfter(uint sequence, uint reference) => (int)(sequence - reference) > 0;
    }
}
