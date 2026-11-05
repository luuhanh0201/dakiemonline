using System.Collections.Generic;
using Xunit;

namespace Game.Rules.Tests
{
    public class CollisionTests
    {
        private const int Precision = 6;
        private const double FullStamina = 100d;

        private static readonly MovementIntent Idle = new MovementIntent(0d, 0d, false);
        private static readonly MovementIntent Right = new MovementIntent(1d, 0d, false);
        private static readonly MovementIntent Left = new MovementIntent(-1d, 0d, false);
        private static readonly MovementIntent DashRight = new MovementIntent(1d, 0d, true);
        private static readonly MovementIntent SwipeUp = new MovementIntent(0d, 1d, false);

        private readonly MovementConfig config = TestConfig.Movement();
        private readonly CollisionMap map = TestConfig.TestMap();

        private void Run(MovementState state, MovementIntent intent, int ticks, CollisionMap? useMap = null)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                Movement.Step(state, intent, config, useMap ?? map);
            }
        }

        [Fact]
        public void Wall_StopsBodyAtLeftEnd()
        {
            var state = new MovementState(1d, 0d, FullStamina);

            Run(state, Left, 60);

            Assert.Equal(0.25d, state.X, Precision);
        }

        [Fact]
        public void Wall_StopsBodyAtRightEnd()
        {
            var state = new MovementState(29d, 0d, FullStamina);

            Run(state, Right, 60);

            Assert.Equal(29.75d, state.X, Precision);
        }

        [Fact]
        public void Dash_DoesNotPassThroughWall()
        {
            var state = new MovementState(29d, 0d, FullStamina);

            Run(state, DashRight, 1);
            Run(state, Idle, 10);

            Assert.Equal(29.75d, state.X, Precision);
            Assert.False(state.IsDashing);
        }

        [Fact]
        public void Dash_DoesNotTunnelThroughAThinWall()
        {
            var thinWall = new CollisionMap(new List<MapBox>
            {
                new MapBox { MinX = -10d, MinY = -1d, MaxX = 20d, MaxY = 0d },
                new MapBox { MinX = 6d, MinY = 0d, MaxX = 6.01d, MaxY = 5d },
            });
            var state = new MovementState(5d, 0d, FullStamina);

            Run(state, DashRight, 1, thinWall);
            Run(state, Idle, 10, thinWall);

            Assert.Equal(5.75d, state.X, Precision);
        }

        [Fact]
        public void Platform_CanBeLandedOnAtOnePointFive()
        {
            var state = new MovementState(9d, 0d, FullStamina);

            Run(state, SwipeUp, 40);

            Assert.True(state.Grounded);
            Assert.Equal(1.5d, state.Y, Precision);
        }

        [Fact]
        public void Platform_LetsBodyJumpThroughFromBelow()
        {
            var state = new MovementState(9d, 0d, FullStamina);

            Run(state, SwipeUp, 12);

            Assert.Equal(2d, state.Y, Precision);
        }

        [Fact]
        public void Platform_DoesNotBlockSidewaysMovement()
        {
            var state = new MovementState(7d, 1.2d, FullStamina);

            Run(state, Right, 1);

            Assert.True(state.X > 7d);
        }

        [Fact]
        public void WalkingOffPlatform_FallsToTheGround()
        {
            var state = new MovementState(10.5d, 1.5d, FullStamina);

            Run(state, Right, 90);

            Assert.True(state.Grounded);
            Assert.Equal(0d, state.Y, Precision);
        }

        [Fact]
        public void SolidCeiling_StopsTheJump()
        {
            var lowCeiling = new CollisionMap(new List<MapBox>
            {
                new MapBox { MinX = -10d, MinY = -1d, MaxX = 20d, MaxY = 0d },
                new MapBox { MinX = 4d, MinY = 2d, MaxX = 6d, MaxY = 3d },
            });
            var state = new MovementState(5d, 0d, FullStamina);

            Run(state, SwipeUp, 12, lowCeiling);

            Assert.True(state.Y <= 1d + 1e-6);
        }
    }
}
