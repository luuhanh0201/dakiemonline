using System.Collections.Generic;
using Xunit;

namespace Game.Rules.Tests
{
    public class MovementTests
    {
        private const int Precision = 6;
        private const double StartX = 5d;
        private const double FullStamina = 100d;

        private static readonly MovementIntent Idle = new MovementIntent(0d, 0d, false);
        private static readonly MovementIntent SwipeUp = new MovementIntent(0d, 1d, false);
        private static readonly MovementIntent Dash = new MovementIntent(0d, 0d, true);

        private readonly MovementConfig config = TestConfig.Movement();
        private readonly CollisionMap map = TestConfig.TestMap();

        private MovementState NewState(double x = StartX, double y = 0d, double stamina = FullStamina)
        {
            return new MovementState(x, y, stamina);
        }

        private void Run(MovementState state, MovementIntent intent, int ticks = 1)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                Movement.Step(state, intent, config, map);
            }
        }

        [Theory]
        [InlineData(1d, 4d)]
        [InlineData(0.5d, 4d)]
        [InlineData(0.49d, 2d)]
        [InlineData(-1d, -4d)]
        [InlineData(-0.3d, -2d)]
        public void JoystickTiltSetsSpeedInUnitsPerSecond(double tilt, double expectedDistance)
        {
            MovementState state = NewState(x: 15d);

            Run(state, new MovementIntent(tilt, 0d, false), Ticks.PerSecond);

            Assert.Equal(15d + expectedDistance, state.X, Precision);
        }

        [Fact]
        public void Jump_ReachesTwoUnitsAfterPointFourSeconds()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 12);

            Assert.Equal(2d, state.Y, Precision);
            Assert.Equal(0d, state.VelocityY, Precision);
        }

        [Fact]
        public void Jump_LandsAfterEightTenthsOfASecond()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 23);
            Assert.False(state.Grounded);

            Run(state, SwipeUp);
            Assert.True(state.Grounded);
            Assert.Equal(0d, state.Y, Precision);
        }

        [Fact]
        public void Jump_NeedsSwipePastSixtyPercent()
        {
            MovementState state = NewState();

            Run(state, new MovementIntent(0d, 0.6d, false));

            Assert.True(state.Grounded);

            Run(state, new MovementIntent(0d, 0.61d, false));

            Assert.False(state.Grounded);
        }

        [Fact]
        public void Jump_HoldingUpDoesNotJumpAgainAfterLanding()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 40);

            Assert.True(state.Grounded);
            Assert.Equal(0d, state.Y, Precision);
        }

        [Fact]
        public void Jump_EachSwipeJumpsOnce()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 40);
            Run(state, Idle);
            Run(state, SwipeUp);

            Assert.False(state.Grounded);
        }

        [Fact]
        public void Jump_CannotJumpAgainInTheAir()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 6);
            Run(state, Idle);
            double velocityBefore = state.VelocityY;
            Run(state, SwipeUp);

            Assert.True(state.VelocityY < velocityBefore);
        }

        [Fact]
        public void Coyote_AllowsJumpWithinOneTenthOfASecondOfLeavingALedge()
        {
            MovementState state = LeaveLeftPlatformEdge();

            Run(state, Idle, 1);
            Run(state, SwipeUp);

            Assert.True(state.VelocityY > 0d);
        }

        [Fact]
        public void Coyote_RefusesJumpAfterTheWindow()
        {
            MovementState state = LeaveLeftPlatformEdge();

            Run(state, Idle, 2);
            Run(state, SwipeUp);

            Assert.True(state.VelocityY < 0d);
        }

        [Fact]
        public void Buffer_JumpsOnLandingWhenSwipedWithinFifteenHundredths()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 12);
            Run(state, Idle, 8);
            Run(state, SwipeUp);
            Run(state, SwipeUp, 3);
            Assert.True(state.Grounded);

            Run(state, SwipeUp);

            Assert.True(state.VelocityY > 0d);
        }

        [Fact]
        public void Buffer_DropsSwipeMadeTooLongBeforeLanding()
        {
            MovementState state = NewState();

            Run(state, SwipeUp, 12);
            Run(state, Idle, 7);
            Run(state, SwipeUp);
            Run(state, SwipeUp, 5);

            Assert.True(state.Grounded);
            Assert.Equal(0d, state.VelocityY, Precision);
        }

        [Fact]
        public void Dash_CoversThreeUnitsInPointTwoSecondsAndCostsStamina()
        {
            MovementState state = NewState();

            Run(state, Dash);
            Run(state, Idle, 5);

            Assert.Equal(StartX + 3d, state.X, Precision);
            Assert.Equal(75d, state.Stamina, Precision);
            Assert.False(state.IsDashing);
        }

        [Fact]
        public void Dash_FollowsJoystickDirection()
        {
            MovementState state = NewState(x: 15d);

            Run(state, new MovementIntent(-1d, 0d, true));
            Run(state, Idle, 5);

            Assert.Equal(12d, state.X, Precision);
            Assert.Equal(-1, state.Facing);
        }

        [Fact]
        public void Dash_UsesFacingWhenJoystickIsReleased()
        {
            MovementState state = NewState(x: 15d);

            Run(state, new MovementIntent(-1d, 0d, false));
            double x = state.X;
            Run(state, Dash);
            Run(state, Idle, 5);

            Assert.Equal(x - 3d, state.X, Precision);
        }

        [Fact]
        public void Dash_NeedsEnoughStamina()
        {
            MovementState state = NewState(stamina: 24d);

            Run(state, Dash);

            Assert.False(state.IsDashing);
            Assert.Equal(24d, state.Stamina, Precision);
        }

        [Fact]
        public void Dash_OnlyOncePerJump()
        {
            MovementState state = NewState(x: 15d);

            Run(state, SwipeUp, 3);
            Run(state, Dash);
            Run(state, Idle, 5);
            Assert.Equal(75d, state.Stamina, Precision);

            Run(state, Dash);
            Assert.False(state.IsDashing);
            Assert.Equal(75d, state.Stamina, Precision);
        }

        [Fact]
        public void Dash_AirDashReturnsAfterLanding()
        {
            MovementState state = NewState(x: 15d);

            Run(state, SwipeUp, 3);
            Run(state, Dash);
            Run(state, Idle, 40);
            Assert.True(state.Grounded);

            Run(state, SwipeUp);
            Run(state, Idle, 2);
            Run(state, Dash);

            Assert.True(state.IsDashing);
        }

        [Fact]
        public void Dash_HoldsHeightWhileInTheAir()
        {
            MovementState state = NewState(x: 15d);

            Run(state, SwipeUp, 6);
            double height = state.Y;
            Run(state, Dash);
            Run(state, Idle, 4);

            Assert.Equal(height, state.Y, Precision);
        }

        [Fact]
        public void SameIntents_ProduceTheSameState()
        {
            var intents = new List<MovementIntent>
            {
                new MovementIntent(1d, 0d, false),
                new MovementIntent(1d, 1d, false),
                new MovementIntent(0.3d, 1d, true),
                new MovementIntent(-1d, 0d, false),
            };

            MovementState first = RunSequence(intents);
            MovementState second = RunSequence(intents);

            Assert.Equal(first.X, second.X);
            Assert.Equal(first.Y, second.Y);
            Assert.Equal(first.VelocityY, second.VelocityY);
            Assert.Equal(first.Stamina, second.Stamina);
        }

        private MovementState RunSequence(IEnumerable<MovementIntent> intents)
        {
            MovementState state = NewState();

            foreach (MovementIntent intent in intents)
            {
                Run(state, intent, 10);
            }

            return state;
        }

        private MovementState LeaveLeftPlatformEdge()
        {
            MovementState state = NewState(x: 11.2d, y: 1.5d);

            Run(state, Idle);
            Assert.True(state.Grounded);

            Run(state, new MovementIntent(1d, 0d, false));
            Assert.False(state.Grounded);
            Run(state, Idle);

            return state;
        }
    }
}
