using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    /// <summary>Reading the price list and the build test. The game itself does not load these yet.</summary>
    public sealed partial class GameConfig
    {
        public const string ProgressionFile = "progression.json";
        public const string BuildTestFile = "build-test.json";
        public const string AccountsFile = "accounts.json";

        /// <summary>Reads <see cref="AccountsFile"/>, which the server and the login screens share.</summary>
        public static AccountConfig ParseAccounts(string json)
        {
            ConfigObject root = Open(new Dictionary<string, string> { [AccountsFile] = json }, AccountsFile);
            var config = new AccountConfig
            {
                UsernameMinLength = PositiveInt(root, "usernameMinLength"),
                UsernameMaxLength = PositiveInt(root, "usernameMaxLength"),
                PasswordMinLength = PositiveInt(root, "passwordMinLength"),
                PasswordMaxLength = PositiveInt(root, "passwordMaxLength"),
                CharacterNameMinLength = PositiveInt(root, "characterNameMinLength"),
                CharacterNameMaxLength = PositiveInt(root, "characterNameMaxLength"),
                GuestPrefix = root.String("guestPrefix"),
                GuestDigits = PositiveInt(root, "guestDigits"),
                CodeDigits = PositiveInt(root, "codeDigits"),
                CodeLifetimeSec = root.Positive("codeLifetimeSec"),
                CodeMaxAttempts = PositiveInt(root, "codeMaxAttempts"),
                CodeResendSec = root.NotNegative("codeResendSec"),
                SessionLifetimeDays = root.Positive("sessionLifetimeDays"),
                LoginMaxFailures = PositiveInt(root, "loginMaxFailures"),
                LoginLockSec = root.Positive("loginLockSec"),
                AddressMaxFailures = PositiveInt(root, "addressMaxFailures"),
                BannedWords = root.Strings("bannedWords"),
            };

            ReadEntry(root, config);
            root.RejectUnknownFields();
            CheckRange(root, "usernameMaxLength", config.UsernameMinLength, config.UsernameMaxLength);
            CheckRange(root, "passwordMaxLength", config.PasswordMinLength, config.PasswordMaxLength);
            CheckRange(root, "characterNameMaxLength", config.CharacterNameMinLength, config.CharacterNameMaxLength);

            if (config.GuestPrefix.Length + config.GuestDigits > config.UsernameMaxLength
                || AccountRules.CheckUsername(config.GuestPrefix + new string('0', config.GuestDigits), config, guest: true) != AccountProblem.None)
            {
                throw root.Fail("guestPrefix", "with guestDigits it must make a valid username");
            }

            if (config.CodeDigits > 9)
            {
                throw root.Fail("codeDigits", "must be at most 9");
            }

            return config;
        }

        private static void CheckRange(ConfigObject root, string maxField, int min, int max)
        {
            if (max < min)
            {
                throw root.Fail(maxField, "cannot be smaller than the minimum");
            }
        }

        private const int MinMaxLevel = 2;

        private static readonly Dictionary<string, CharacterStat> Stats = new Dictionary<string, CharacterStat>
        {
            ["attack"] = CharacterStat.Attack,
            ["max_hp"] = CharacterStat.MaxHp,
            ["defense"] = CharacterStat.Defense,
            ["max_energy"] = CharacterStat.MaxEnergy,
        };

        /// <summary>The same character with other stats and skill levels, as after spending TN.</summary>
        public GameConfig WithCharacter(CharacterConfig character)
        {
            return new GameConfig(character, Movement, MapBoxes, Skills, Potion, Gear, Boss);
        }

        /// <summary>Reads <see cref="ProgressionFile"/> and checks its skills against this config's.</summary>
        public ProgressionConfig ParseProgression(string json)
        {
            ConfigObject root = Open(new Dictionary<string, string> { [ProgressionFile] = json }, ProgressionFile);
            var prices = new ProgressionConfig
            {
                Stats = ParseStatPrices(root),
                Skills = ParseSkillPrices(root),
            };

            ReadEntry(root, prices);
            root.RejectUnknownFields();
            return prices;
        }

        /// <summary>Reads <see cref="BuildTestFile"/>; a build that costs more than the budget is refused.</summary>
        public BuildTestConfig ParseBuildTest(string json, ProgressionConfig prices)
        {
            ConfigObject root = Open(new Dictionary<string, string> { [BuildTestFile] = json }, BuildTestFile);
            var test = new BuildTestConfig { Trials = ParseTrials(root.Object("trials")) };
            var ids = new HashSet<string>();
            var sets = new List<BuildSet>();

            foreach (ConfigObject item in root.Array("sets"))
            {
                sets.Add(ParseSet(item, prices, ids));
            }

            test.Sets = sets.Count > 0 ? sets : throw root.Fail("sets", "needs at least one set");
            ReadEntry(root, test);
            root.RejectUnknownFields();
            return test;
        }

        /// <summary>Ids of sets and builds share one namespace, so a report line always names one thing.</summary>
        private BuildSet ParseSet(ConfigObject item, ProgressionConfig prices, HashSet<string> ids)
        {
            var set = new BuildSet
            {
                Id = ReadId(item),
                Name = item.String("name"),
                BudgetTn = PositiveInt(item, "budgetTn"),
            };

            if (ids.Add(set.Id) == false)
            {
                throw item.Fail("id", $"duplicate id '{set.Id}'");
            }

            var builds = new List<BuildPlan>();

            foreach (ConfigObject entry in item.Array("builds"))
            {
                BuildPlan build = ParseBuild(entry, prices);

                if (ids.Add(build.Id) == false)
                {
                    throw entry.Fail("id", $"duplicate id '{build.Id}'");
                }

                int cost = Progression.Cost(prices, build);

                if (cost > set.BudgetTn)
                {
                    throw entry.Fail("id", $"costs {cost} TN, over the budget of {set.BudgetTn}");
                }

                builds.Add(build);
            }

            // One build alone has nothing to be compared with.
            set.Builds = builds.Count > 1 ? builds : throw item.Fail("builds", "needs at least two builds");
            item.RejectUnknownFields();
            return set;
        }

        private static IReadOnlyList<StatPrice> ParseStatPrices(ConfigObject root)
        {
            var prices = new List<StatPrice>();

            foreach (ConfigObject item in root.Array("stats"))
            {
                var price = new StatPrice
                {
                    Stat = Lookup(item, "id", Stats),
                    PerStep = PositiveInt(item, "perStep"),
                    FirstCost = PositiveInt(item, "firstCost"),
                    CostIncrease = NotNegativeInt(item, "costIncrease"),
                };

                if (prices.Any(other => other.Stat == price.Stat))
                {
                    throw item.Fail("id", $"duplicate id '{item.String("id")}'");
                }

                item.RejectUnknownFields();
                prices.Add(price);
            }

            foreach (KeyValuePair<string, CharacterStat> stat in Stats)
            {
                if (prices.All(price => price.Stat != stat.Value))
                {
                    throw root.Fail("stats", $"no price for {stat.Key}");
                }
            }

            return prices;
        }

        private IReadOnlyList<SkillPrice> ParseSkillPrices(ConfigObject root)
        {
            var prices = new List<SkillPrice>();

            foreach (ConfigObject item in root.Array("skills"))
            {
                SkillConfig skill = SkillById(item);
                var price = new SkillPrice
                {
                    SkillId = skill.Id,
                    Slot = skill.Slot,
                    MaxLevel = item.Int("maxLevel"),
                    FirstCost = PositiveInt(item, "firstCost"),
                    CostIncrease = NotNegativeInt(item, "costIncrease"),
                };

                if (prices.Any(other => other.Slot == price.Slot))
                {
                    throw item.Fail("id", $"duplicate id '{skill.Id}'");
                }

                if (skill.Slot == SkillSlot.Attack)
                {
                    throw item.Fail("id", "the basic attack has no levels");
                }

                if (skill.LevelPercent <= 0m)
                {
                    throw item.Fail("id", $"'{skill.Id}' has no levelPercent in {SkillsFile}, so its levels would do nothing");
                }

                if (price.MaxLevel < MinMaxLevel)
                {
                    throw item.Fail("maxLevel", $"must be at least {MinMaxLevel}");
                }

                item.RejectUnknownFields();
                prices.Add(price);
            }

            return prices;
        }

        private SkillConfig SkillById(ConfigObject item)
        {
            string id = ReadId(item);
            return Skills.FirstOrDefault(skill => skill.Id == id) ?? throw item.Fail("id", $"unknown skill '{id}'");
        }

        private static BuildTrialSettings ParseTrials(ConfigObject item)
        {
            var ids = new HashSet<string>();
            var dummies = new List<DummyTrial>();

            foreach (ConfigObject entry in item.Array("dummies"))
            {
                var dummy = new DummyTrial
                {
                    Id = ReadId(entry),
                    Name = entry.String("name"),
                    Seconds = entry.Positive("seconds"),
                };

                if (ids.Add(dummy.Id) == false)
                {
                    throw entry.Fail("id", $"duplicate id '{dummy.Id}'");
                }

                entry.RejectUnknownFields();
                dummies.Add(dummy);
            }

            var trials = new BuildTrialSettings
            {
                Dummies = dummies.Count > 0 ? dummies : throw item.Fail("dummies", "needs at least one run"),
                DummyDefense = item.NotNegativeDecimal("dummyDefense"),
                MinLeadPercent = item.Percent("minLeadPercent"),
                FightMaxSec = item.Positive("fightMaxSec"),
                PotionBelowHpPercent = item.Percent("potionBelowHpPercent"),
                HealBelowHpPercent = item.Percent("healBelowHpPercent"),
                PlayerStartX = item.Double("playerStartX"),
                MinTrialsLedByEachBuild = NotNegativeInt(item, "minTrialsLedByEachBuild"),
                OneBuildMayLeadEveryTrial = item.Boolean("oneBuildMayLeadEveryTrial"),
            };

            item.RejectUnknownFields();
            return trials;
        }

        private BuildPlan ParseBuild(ConfigObject item, ProgressionConfig prices)
        {
            var steps = new Dictionary<CharacterStat, int>();
            var levels = new Dictionary<SkillSlot, int>();

            foreach (ConfigObject stat in item.Array("stats"))
            {
                CharacterStat which = Lookup(stat, "id", Stats);

                if (steps.ContainsKey(which))
                {
                    throw stat.Fail("id", $"duplicate id '{stat.String("id")}'");
                }

                steps[which] = NotNegativeInt(stat, "steps");
                stat.RejectUnknownFields();
            }

            foreach (ConfigObject entry in item.Array("skills"))
            {
                SkillConfig skill = SkillById(entry);
                SkillPrice? price = prices.Skill(skill.Slot);
                int level = entry.Int("level");

                if (price == null)
                {
                    throw entry.Fail("id", $"'{skill.Id}' has no price in {ProgressionFile}");
                }

                if (levels.ContainsKey(skill.Slot))
                {
                    throw entry.Fail("id", $"duplicate id '{skill.Id}'");
                }

                if (level < 1 || level > price.MaxLevel)
                {
                    throw entry.Fail("level", $"must be from 1 to {price.MaxLevel}");
                }

                levels[skill.Slot] = level;
                entry.RejectUnknownFields();
            }

            var build = new BuildPlan
            {
                Id = ReadId(item),
                Name = item.String("name"),
                StatSteps = steps,
                SkillLevels = levels,
            };

            item.RejectUnknownFields();
            return build;
        }
    }
}
