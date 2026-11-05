using System;

namespace Game.Rules
{
    public readonly struct MovementIntent
    {
        public MovementIntent(double moveX, double moveY, bool dashPressed)
        {
            MoveX = moveX;
            MoveY = moveY;
            DashPressed = dashPressed;
        }

        /// <summary>Joystick tilt from -1 to 1.</summary>
        public double MoveX { get; }

        /// <summary>Joystick tilt from -1 to 1; crossing the swipe threshold upward jumps.</summary>
        public double MoveY { get; }

        public bool DashPressed { get; }
    }

    /// <summary>What a skill being cast allows the character to do this tick.</summary>
    public readonly struct MovementLimits
    {
        public MovementLimits(double speedFactor, bool canJump, bool canDash, bool lockFacing, double forcedDeltaX)
        {
            SpeedFactor = speedFactor;
            CanJump = canJump;
            CanDash = canDash;
            LockFacing = lockFacing;
            ForcedDeltaX = forcedDeltaX;
        }

        public static MovementLimits Free => new MovementLimits(1d, true, true, false, 0d);

        public double SpeedFactor { get; }
        public bool CanJump { get; }
        public bool CanDash { get; }
        public bool LockFacing { get; }

        /// <summary>Distance the skill itself moves the body this tick, replacing joystick movement.</summary>
        public double ForcedDeltaX { get; }
    }

    public sealed class MovementState
    {
        public MovementState(double x, double y, double stamina)
        {
            X = x;
            Y = y;
            Stamina = stamina;
            Grounded = true;
        }

        /// <summary>Horizontal centre of the body.</summary>
        public double X { get; internal set; }

        /// <summary>Height of the feet.</summary>
        public double Y { get; internal set; }

        public double VelocityY { get; internal set; }
        public int Facing { get; internal set; } = 1;
        public bool Grounded { get; internal set; }
        public double Stamina { get; internal set; }
        public bool IsDashing => DashTicksLeft > 0;

        internal double PreviousMoveY { get; set; }
        internal int JumpBufferTicks { get; set; }
        internal int DashBufferTicks { get; set; }
        internal int CoyoteTicks { get; set; }
        internal int DashTicksLeft { get; set; }
        internal int DashDirection { get; set; }
        internal int AirDashesUsed { get; set; }
        internal int StaminaDelayTicks { get; set; }
    }

    public static class Movement
    {
        public static void Step(
            MovementState state,
            in MovementIntent intent,
            MovementConfig config,
            CollisionMap map)
        {
            Step(state, intent, config, map, MovementLimits.Free);
        }

        public static void Step(
            MovementState state,
            in MovementIntent intent,
            MovementConfig config,
            CollisionMap map,
            in MovementLimits limits)
        {
            BufferInput(state, intent, config);

            if (!state.IsDashing && intent.MoveX != 0d && !limits.LockFacing)
            {
                state.Facing = Math.Sign(intent.MoveX);
            }

            if (limits.CanDash)
            {
                TryStartDash(state, intent, config);
            }

            if (limits.CanJump)
            {
                TryJump(state, config);
            }

            MoveHorizontally(state, intent, config, map, limits);
            MoveVertically(state, config, map);
        }

        private static void BufferInput(MovementState state, in MovementIntent intent, MovementConfig config)
        {
            int bufferTicks = Ticks.FromSeconds(config.InputBufferSec);
            bool swipedUp = intent.MoveY > config.JumpSwipeAbove && state.PreviousMoveY <= config.JumpSwipeAbove;

            state.JumpBufferTicks = swipedUp ? bufferTicks : Math.Max(0, state.JumpBufferTicks - 1);
            state.DashBufferTicks = intent.DashPressed ? bufferTicks : Math.Max(0, state.DashBufferTicks - 1);
            state.PreviousMoveY = intent.MoveY;
        }

        private static void TryStartDash(MovementState state, in MovementIntent intent, MovementConfig config)
        {
            bool canDashInAir = state.AirDashesUsed < config.AirDashesPerJump;

            if (state.DashBufferTicks == 0
                || state.IsDashing
                || state.Stamina < config.DashStaminaCost
                || (!state.Grounded && !canDashInAir))
            {
                return;
            }

            if (!state.Grounded)
            {
                state.AirDashesUsed++;
            }

            state.Stamina -= config.DashStaminaCost;
            state.StaminaDelayTicks = Ticks.FromSeconds(config.StaminaRegenDelaySec);
            state.DashDirection = intent.MoveX != 0d ? Math.Sign(intent.MoveX) : state.Facing;
            state.Facing = state.DashDirection;
            state.DashTicksLeft = Ticks.FromSeconds(config.DashDurationSec);
            state.DashBufferTicks = 0;
            state.VelocityY = 0d;
        }

        private static void TryJump(MovementState state, MovementConfig config)
        {
            bool canJump = state.Grounded || state.CoyoteTicks > 0;

            if (state.JumpBufferTicks == 0 || state.IsDashing || !canJump)
            {
                return;
            }

            state.VelocityY = 2d * config.JumpHeight / config.JumpTimeToPeakSec;
            state.Grounded = false;
            state.CoyoteTicks = 0;
            state.JumpBufferTicks = 0;
        }

        private static void MoveHorizontally(
            MovementState state,
            in MovementIntent intent,
            MovementConfig config,
            CollisionMap map,
            in MovementLimits limits)
        {
            double deltaX;

            if (state.IsDashing)
            {
                deltaX = state.DashDirection * config.DashDistance / Ticks.FromSeconds(config.DashDurationSec);
            }
            else if (limits.ForcedDeltaX != 0d)
            {
                deltaX = limits.ForcedDeltaX;
            }
            else
            {
                double tilt = Math.Abs(intent.MoveX);
                double speed = tilt < config.WalkTiltBelow ? config.WalkSpeed : config.RunSpeed;
                deltaX = Math.Sign(intent.MoveX) * speed * limits.SpeedFactor * Ticks.DeltaSeconds;
            }

            double target = state.X + deltaX;
            state.X = map.SweepX(state.X, state.Y, deltaX, config.BodyWidth, config.BodyHeight);

            if (state.IsDashing)
            {
                bool hitWall = Math.Abs(state.X - target) > 1e-9;
                state.DashTicksLeft = hitWall ? 0 : state.DashTicksLeft - 1;
            }
        }

        private static void MoveVertically(MovementState state, MovementConfig config, CollisionMap map)
        {
            if (state.IsDashing)
            {
                return;
            }

            bool wasGrounded = state.Grounded;
            double gravity = 2d * config.JumpHeight / (config.JumpTimeToPeakSec * config.JumpTimeToPeakSec);
            double delta = Ticks.DeltaSeconds;
            double nextY = state.Y + state.VelocityY * delta - 0.5d * gravity * delta * delta;
            double nextVelocity = state.VelocityY - gravity * delta;

            state.Grounded = false;

            if (nextY <= state.Y)
            {
                double? landing = map.FindLanding(state.X, state.Y, nextY, config.BodyWidth);

                if (landing != null)
                {
                    nextY = landing.Value;
                    nextVelocity = 0d;
                    state.Grounded = true;
                    state.AirDashesUsed = 0;
                }
            }
            else
            {
                double? ceiling = map.FindCeiling(
                    state.X,
                    state.Y + config.BodyHeight,
                    nextY + config.BodyHeight,
                    config.BodyWidth);

                if (ceiling != null)
                {
                    nextY = ceiling.Value - config.BodyHeight;
                    nextVelocity = 0d;
                }
            }

            state.Y = nextY;
            state.VelocityY = nextVelocity;

            if (state.Grounded)
            {
                state.CoyoteTicks = 0;
            }
            else if (wasGrounded)
            {
                state.CoyoteTicks = Ticks.FromSeconds(config.CoyoteTimeSec);
            }
            else if (state.CoyoteTicks > 0)
            {
                state.CoyoteTicks--;
            }
        }
    }
}
