namespace Game.Rules
{
    public sealed class MovementConfig : ConfigEntry
    {
        public double RunSpeed { get; set; }
        public double WalkSpeed { get; set; }
        public double WalkTiltBelow { get; set; }
        public double JumpHeight { get; set; }
        public double JumpTimeToPeakSec { get; set; }
        public double JumpSwipeAbove { get; set; }
        public double CoyoteTimeSec { get; set; }
        public double InputBufferSec { get; set; }
        public double DashDistance { get; set; }
        public double DashDurationSec { get; set; }
        public double DashStaminaCost { get; set; }
        public int AirDashesPerJump { get; set; }
        public double BodyWidth { get; set; }
        public double BodyHeight { get; set; }
    }
}
