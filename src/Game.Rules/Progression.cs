using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>What TN buys: stat steps and skill levels, each step dearer than the last by a fixed amount.</summary>
    public static class Progression
    {
        /// <summary>Cost of step k, counting from 1: firstCost + (k − 1) × costIncrease.</summary>
        public static int StepCost(int firstCost, int costIncrease, int step)
        {
            if (step < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "Steps count from 1.");
            }

            return checked(firstCost + (step - 1) * costIncrease);
        }

        /// <summary>Cost of the first steps together; buying several at once adds up each step's own price.</summary>
        public static int TotalCost(int firstCost, int costIncrease, int steps)
        {
            if (steps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), steps, "Cannot buy a negative number of steps.");
            }

            return checked(steps * firstCost + costIncrease * (steps * (steps - 1) / 2));
        }

        public static int StatCost(ProgressionConfig prices, BuildPlan plan)
        {
            int total = 0;

            foreach (KeyValuePair<CharacterStat, int> pair in plan.StatSteps)
            {
                StatPrice price = prices.Stat(pair.Key);
                total = checked(total + TotalCost(price.FirstCost, price.CostIncrease, pair.Value));
            }

            return total;
        }

        public static int SkillCost(ProgressionConfig prices, BuildPlan plan)
        {
            int total = 0;

            foreach (KeyValuePair<SkillSlot, int> pair in plan.SkillLevels)
            {
                SkillPrice price = PriceOf(prices, pair.Key, pair.Value);
                total = checked(total + TotalCost(price.FirstCost, price.CostIncrease, pair.Value - 1));
            }

            return total;
        }

        public static int Cost(ProgressionConfig prices, BuildPlan plan)
        {
            return checked(StatCost(prices, plan) + SkillCost(prices, plan));
        }

        /// <summary>The character after the plan's stat steps and skill levels are added to the starting one.</summary>
        public static CharacterConfig Apply(CharacterConfig start, ProgressionConfig prices, BuildPlan plan)
        {
            var levels = new Dictionary<SkillSlot, int>();

            foreach (KeyValuePair<SkillSlot, int> pair in plan.SkillLevels)
            {
                PriceOf(prices, pair.Key, pair.Value);
                levels[pair.Key] = pair.Value;
            }

            return new CharacterConfig
            {
                Id = start.Id,
                Status = start.Status,
                Source = start.Source,
                MaxHp = start.MaxHp + Gain(prices, plan, CharacterStat.MaxHp),
                MaxEnergy = start.MaxEnergy + Gain(prices, plan, CharacterStat.MaxEnergy),
                EnergyRegenPerSec = start.EnergyRegenPerSec,
                MaxStamina = start.MaxStamina,
                Attack = start.Attack + Gain(prices, plan, CharacterStat.Attack),
                Defense = start.Defense + Gain(prices, plan, CharacterStat.Defense),
                SkillLevels = levels,
            };
        }

        private static int Gain(ProgressionConfig prices, BuildPlan plan, CharacterStat stat)
        {
            if (!plan.StatSteps.TryGetValue(stat, out int steps))
            {
                return 0;
            }

            if (steps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(plan), steps, $"{stat} cannot have a negative number of steps.");
            }

            return checked(steps * prices.Stat(stat).PerStep);
        }

        private static SkillPrice PriceOf(ProgressionConfig prices, SkillSlot slot, int level)
        {
            SkillPrice? price = prices.Skill(slot);

            if (price == null)
            {
                throw new ArgumentException($"Skill {slot} has no levels.", nameof(slot));
            }

            if (level < 1 || level > price.MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, $"{price.SkillId} goes from level 1 to {price.MaxLevel}.");
            }

            return price;
        }
    }
}
