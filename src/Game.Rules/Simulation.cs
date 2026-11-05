using System.Collections.Generic;

namespace Game.Rules
{
    public enum MatchResult
    {
        Playing,
        Won,
        Lost,
    }

    /// <summary>One fight against the boss, advanced a tick at a time. Replaying means a new Simulation.</summary>
    public sealed class Simulation
    {
        private readonly GameConfig config;
        private readonly CollisionMap map;
        private readonly List<CombatEvent> tickEvents = new List<CombatEvent>();
        private readonly List<CombatEvent> combatEvents = new List<CombatEvent>();
        private readonly List<BossEvent> bossEvents = new List<BossEvent>();

        public Simulation(GameConfig config, double playerX)
        {
            this.config = config;
            map = new CollisionMap(config.MapBoxes);
            Player = new MovementState(playerX, 0d, config.Character.MaxStamina);
            Resources = new ResourceState(config.Character);
            Combat = new CombatState(config.Potion);
            Boss = new BossState(config.Boss, BossAi.StartX(config.Boss, playerX));
        }

        public MovementState Player { get; }
        public ResourceState Resources { get; }
        public CombatState Combat { get; }
        public BossState Boss { get; }
        public MatchResult Result { get; private set; }
        public int HitsTaken { get; private set; }
        public int TicksPlayed { get; private set; }
        public double SecondsPlayed => TicksPlayed * Ticks.DeltaSeconds;
        public int PotionsUsed => config.Potion.Charges - Combat.PotionsLeft;

        /// <summary>The attack that ended a lost fight.</summary>
        public BossAttackConfig? KilledBy { get; private set; }

        /// <summary>Events since the last <see cref="ClearEvents"/>, so a view can show them once a frame.</summary>
        public IReadOnlyList<CombatEvent> CombatEvents => combatEvents;
        public IReadOnlyList<BossEvent> BossEvents => bossEvents;

        public void ClearEvents()
        {
            combatEvents.Clear();
            bossEvents.Clear();
        }

        public void Step(in MovementIntent moveIntent, in CombatIntent combatIntent)
        {
            if (Result != MatchResult.Playing)
            {
                return;
            }

            TicksPlayed++;
            tickEvents.Clear();

            Game.Rules.Combat.Step(
                Combat,
                Player,
                Resources,
                moveIntent,
                combatIntent,
                config,
                map,
                BossAi.TargetOf(Boss, config.Boss),
                tickEvents);

            BossAi.ApplyHits(Boss, tickEvents);
            combatEvents.AddRange(tickEvents);

            if (Boss.IsDead)
            {
                Result = MatchResult.Won;
                return;
            }

            int firstNew = bossEvents.Count;
            BossAi.Step(Boss, Player, Resources, config, map, bossEvents);

            for (int index = firstNew; index < bossEvents.Count; index++)
            {
                CountBossEvent(bossEvents[index]);
            }
        }

        private void CountBossEvent(in BossEvent item)
        {
            if (item.Kind != BossEventKind.PlayerHit)
            {
                return;
            }

            HitsTaken++;

            if (Resources.IsDead)
            {
                Result = MatchResult.Lost;
                KilledBy = item.Attack;
            }
        }
    }
}
