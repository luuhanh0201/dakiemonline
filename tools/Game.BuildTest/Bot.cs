using Game.Rules;

namespace Game.BuildTest;

/// <summary>What a build did to the training dummy.</summary>
public sealed record DummyResult(int TotalDamage, double DamagePerSecond, double? EnergyShortAtSec, IReadOnlyList<int> DamageByTick)
{
    /// <summary>Seconds until this much damage was dealt, or null if it never was.</summary>
    public double? SecondsToDeal(int damage)
    {
        int total = 0;

        for (int tick = 0; tick < DamageByTick.Count; tick++)
        {
            total += DamageByTick[tick];

            if (total >= damage)
            {
                return (tick + 1) * Ticks.DeltaSeconds;
            }
        }

        return null;
    }
}

/// <summary>How a boss fight ended. A fight still running at the time limit is timed out, not won.</summary>
public sealed record FightResult(bool Won, bool TimedOut, double Seconds, int BossHpLeft, int PlayerHpLeft, int HitsTaken, int PotionsUsed);

/// <summary>How long a build stood in the boss's blows. Still standing at the time limit means it outlasted the trial.</summary>
public sealed record EndureResult(bool Survived, double Seconds, int HitsTaken, int PotionsUsed);

/// <summary>
/// A player that always does the same thing in the same situation, so two builds can be compared.
/// It casts whatever is ready in a fixed order, heals and drinks at set health, and either
/// stands its ground or steps out of every warning zone.
/// </summary>
public static class Bot
{
    /// <summary>Strongest first; the basic attack fills the gaps.</summary>
    private static readonly SkillSlot[] Rotation =
    {
        SkillSlot.Ultimate,
        SkillSlot.OptionA,
        SkillSlot.OptionB,
        SkillSlot.Ranged,
        SkillSlot.Attack,
    };

    // Close enough for the basic attack, which reaches 1.5 from the body's centre.
    private const double StrikingDistance = 1.4d;

    // A jump started this many ticks before a sweep lands clears it at the top of the arc.
    private const int JumpLeadTicks = 8;

    // How far ahead the bot looks for a wall before choosing which way to step out of a zone.
    private const double WallLookAhead = 2d;

    /// <summary>Hits a target that never moves or fights back, for the given time.</summary>
    public static DummyResult HitDummy(GameConfig playing, BuildTrialSettings settings, double seconds, decimal defense, bool startWithEnergy = true)
    {
        double dummyX = BossAi.StartX(playing.Boss, settings.PlayerStartX);
        double half = playing.Boss.Width / 2d;
        var target = new CombatTarget(new CombatBox(dummyX - half, 0d, dummyX + half, playing.Boss.Height), defense);
        var map = new CollisionMap(playing.MapBoxes);
        var movement = new MovementState(dummyX - StrikingDistance, 0d, playing.Character.MaxStamina);
        var combat = new CombatState(playing.Potion);
        var resources = new ResourceState(playing.Character);
        var events = new List<CombatEvent>();
        int ticks = Ticks.FromSeconds(seconds);
        var damageByTick = new int[ticks];
        double? energyShortAt = null;

        if (!startWithEnergy)
        {
            resources.Restore(new ResourceSnapshot(resources.Hp, 0d));
        }

        for (int tick = 0; tick < ticks; tick++)
        {
            if (energyShortAt == null && startWithEnergy && IsShortOfEnergy(playing, combat, resources))
            {
                energyShortAt = tick * Ticks.DeltaSeconds;
            }

            double distance = dummyX - movement.X;
            double moveX = Math.Abs(distance) > StrikingDistance ? Math.Sign(distance) : 0d;
            SkillSlot? skill = combat.IsAttacking ? null : NextSkill(playing, combat, resources, canRoot: true);

            events.Clear();
            Combat.Step(combat, movement, resources, new MovementIntent(moveX, 0d, false), new CombatIntent(skill), playing, map, target, events);
            damageByTick[tick] = events.Where(item => item.Kind == CombatEventKind.SkillHit).Sum(item => item.Amount);
        }

        int total = damageByTick.Sum();
        return new DummyResult(total, total / seconds, energyShortAt, damageByTick);
    }

    /// <summary>Stands next to the boss and trades blows, never leaving a warning zone.</summary>
    public static FightResult FightStanding(GameConfig playing, BuildTrialSettings settings)
    {
        var fight = new Simulation(playing, settings.PlayerStartX);
        int maxTicks = Ticks.FromSeconds(settings.FightMaxSec);

        for (int tick = 0; tick < maxTicks && fight.Result == MatchResult.Playing; tick++)
        {
            SkillSlot? skill = fight.Combat.IsAttacking ? null : NextMove(fight, playing, settings, canRoot: true);
            fight.Step(Chase(fight), new CombatIntent(skill, WantsPotion(fight, playing, settings)));
            fight.ClearEvents();
        }

        return Outcome(fight);
    }

    /// <summary>
    /// Stands in the boss's blows without hitting back or stepping away, healing and drinking as
    /// in a fight, until it falls. Shows how much punishment a build takes, apart from its damage.
    /// </summary>
    public static EndureResult Endure(GameConfig playing, BuildTrialSettings settings)
    {
        var fight = new Simulation(playing, settings.PlayerStartX);
        int maxTicks = Ticks.FromSeconds(settings.FightMaxSec);
        SkillConfig heal = playing.Skill(SkillSlot.Support);

        for (int tick = 0; tick < maxTicks && fight.Result == MatchResult.Playing; tick++)
        {
            bool hurt = fight.Resources.Hp < playing.Character.MaxHp * settings.HealBelowHpPercent / 100d;
            bool canHeal = hurt && !fight.Combat.IsAttacking && Combat.IsSkillReady(fight.Combat, fight.Resources, heal);
            fight.Step(Chase(fight), new CombatIntent(canHeal ? SkillSlot.Support : null, WantsPotion(fight, playing, settings)));
            fight.ClearEvents();
        }

        return new EndureResult(fight.Result == MatchResult.Playing, fight.SecondsPlayed, fight.HitsTaken, fight.PotionsUsed);
    }

    /// <summary>Steps or jumps out of every warning zone and attacks in between.</summary>
    public static FightResult FightDodging(GameConfig playing, BuildTrialSettings settings)
    {
        var fight = new Simulation(playing, settings.PlayerStartX);
        var map = new CollisionMap(playing.MapBoxes);
        int maxTicks = Ticks.FromSeconds(settings.FightMaxSec);
        int warningTicks = 0;

        for (int tick = 0; tick < maxTicks && fight.Result == MatchResult.Playing; tick++)
        {
            CombatBox? zone = BossAi.WarningZone(fight.Boss, playing.Boss);
            warningTicks = zone == null ? 0 : warningTicks + 1;

            MovementIntent move = zone == null ? Chase(fight) : Dodge(fight, playing, map, zone.Value, warningTicks);

            // Rooted skills would hold the bot inside a zone, so they wait until the boss rests.
            SkillSlot? skill = fight.Combat.IsAttacking ? null : NextMove(fight, playing, settings, canRoot: zone == null);
            fight.Step(move, new CombatIntent(skill, WantsPotion(fight, playing, settings)));
            fight.ClearEvents();
        }

        return Outcome(fight);
    }

    private static FightResult Outcome(Simulation fight)
    {
        return new FightResult(
            fight.Result == MatchResult.Won,
            fight.Result == MatchResult.Playing,
            fight.SecondsPlayed,
            fight.Boss.Hp,
            fight.Resources.Hp,
            fight.HitsTaken,
            fight.PotionsUsed);
    }

    private static MovementIntent Chase(Simulation fight)
    {
        double distance = fight.Boss.X - fight.Player.X;
        return new MovementIntent(Math.Abs(distance) > StrikingDistance ? Math.Sign(distance) : 0d, 0d, false);
    }

    private static MovementIntent Dodge(Simulation fight, GameConfig playing, CollisionMap map, in CombatBox zone, int warningTicks)
    {
        BossAttackConfig attack = BossAi.CurrentAttack(fight.Boss, playing.Boss);

        if (attack.Shape == BossAttackShape.BothSides)
        {
            int ticksLeft = Ticks.FromSeconds(attack.WarningSec) - warningTicks;
            return new MovementIntent(0d, ticksLeft == JumpLeadTicks ? 1d : 0d, false);
        }

        double middle = (zone.MinX + zone.MaxX) / 2d;
        double away = fight.Player.X >= middle ? 1d : -1d;
        MovementConfig body = playing.Movement;
        double reached = map.SweepX(fight.Player.X, fight.Player.Y, away * WallLookAhead, body.BodyWidth, body.BodyHeight);
        bool blockedByWall = Math.Abs(reached - fight.Player.X) < WallLookAhead - 1e-9;

        return new MovementIntent(blockedByWall ? -away : away, 0d, false);
    }

    private static bool WantsPotion(Simulation fight, GameConfig playing, BuildTrialSettings settings)
    {
        return fight.Resources.Hp < playing.Character.MaxHp * settings.PotionBelowHpPercent / 100d;
    }

    /// <summary>The heal when hurt enough and it is ready, otherwise the next attack.</summary>
    private static SkillSlot? NextMove(Simulation fight, GameConfig playing, BuildTrialSettings settings, bool canRoot)
    {
        bool hurt = fight.Resources.Hp < playing.Character.MaxHp * settings.HealBelowHpPercent / 100d;
        SkillConfig heal = playing.Skill(SkillSlot.Support);

        if (hurt && canRoot && fight.Player.Grounded && Combat.IsSkillReady(fight.Combat, fight.Resources, heal))
        {
            return SkillSlot.Support;
        }

        return NextSkill(playing, fight.Combat, fight.Resources, canRoot);
    }

    private static SkillSlot? NextSkill(GameConfig playing, CombatState combat, ResourceState resources, bool canRoot)
    {
        foreach (SkillSlot slot in Rotation)
        {
            SkillConfig skill = playing.Skill(slot);

            if ((canRoot || skill.MoveSpeedPercent > 0d) && Combat.IsSkillReady(combat, resources, skill))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>True when a skill has finished cooling down but there is not the energy to cast it.</summary>
    private static bool IsShortOfEnergy(GameConfig playing, CombatState combat, ResourceState resources)
    {
        foreach (SkillSlot slot in Rotation)
        {
            SkillConfig skill = playing.Skill(slot);

            if (skill.EnergyCost > 0 && combat.CooldownSecondsLeft(slot) == 0d && !ResourceRules.CanAfford(resources, skill.EnergyCost))
            {
                return true;
            }
        }

        return false;
    }
}
