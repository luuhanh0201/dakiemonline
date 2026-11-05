using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Game.Rules
{
    /// <summary>All config files, parsed and checked. Any problem stops loading with a clear message.</summary>
    public sealed class GameConfig
    {
        public const string CharacterFile = "character.json";
        public const string MovementFile = "movement.json";
        public const string MapFile = "map.test.json";
        public const string SkillsFile = "skills.thuy.json";
        public const string ItemsFile = "items.json";

        private const int SupportedVersion = 1;
        private const decimal WholePercent = 100m;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+([.][a-z0-9_]+)*$");

        private static readonly Dictionary<string, ConfigStatus> Statuses = new Dictionary<string, ConfigStatus>
        {
            ["THU"] = ConfigStatus.Thu,
            ["CHOT"] = ConfigStatus.Chot,
            ["DE_XUAT"] = ConfigStatus.DeXuat,
        };

        private static readonly Dictionary<string, SkillSlot> Slots = new Dictionary<string, SkillSlot>
        {
            ["attack"] = SkillSlot.Attack,
            ["ranged"] = SkillSlot.Ranged,
            ["support"] = SkillSlot.Support,
            ["ultimate"] = SkillSlot.Ultimate,
            ["optionA"] = SkillSlot.OptionA,
            ["optionB"] = SkillSlot.OptionB,
        };

        private GameConfig(
            CharacterConfig character,
            MovementConfig movement,
            IReadOnlyList<MapBox> mapBoxes,
            IReadOnlyList<SkillConfig> skills,
            PotionConfig potion)
        {
            Character = character;
            Movement = movement;
            MapBoxes = mapBoxes;
            Skills = skills;
            Potion = potion;
        }

        public static IReadOnlyList<string> FileNames { get; } = new[]
        {
            CharacterFile,
            MovementFile,
            MapFile,
            SkillsFile,
            ItemsFile,
        };

        public CharacterConfig Character { get; }
        public MovementConfig Movement { get; }
        public IReadOnlyList<MapBox> MapBoxes { get; }
        public IReadOnlyList<SkillConfig> Skills { get; }
        public PotionConfig Potion { get; }

        /// <summary>Takes the text of each file in <see cref="FileNames"/>, keyed by file name.</summary>
        public static GameConfig Parse(IReadOnlyDictionary<string, string> files)
        {
            return new GameConfig(
                ParseCharacter(Open(files, CharacterFile)),
                ParseMovement(Open(files, MovementFile)),
                ParseMap(Open(files, MapFile)),
                ParseSkills(Open(files, SkillsFile)),
                ParseItems(Open(files, ItemsFile)));
        }

        public SkillConfig Skill(SkillSlot slot) => Skills.First(skill => skill.Slot == slot);

        private static ConfigObject Open(IReadOnlyDictionary<string, string> files, string name)
        {
            if (files.TryGetValue(name, out string? json) == false)
            {
                throw new InvalidConfigException(name, "file", "file is missing");
            }

            ConfigObject root = ConfigObject.Root(name, json);

            if (root.Int("version") != SupportedVersion)
            {
                throw root.Fail("version", $"only version {SupportedVersion} is supported");
            }

            return root;
        }

        private static PotionConfig ParseItems(ConfigObject root)
        {
            ConfigObject item = root.Object("potion");
            var potion = new PotionConfig
            {
                HealHp = PositiveInt(item, "healHp"),
                Charges = PositiveInt(item, "charges"),
                CooldownSec = item.NotNegative("cooldownSec"),
            };

            ReadEntry(item, potion);
            item.RejectUnknownFields();
            root.RejectUnknownFields();
            return potion;
        }

        private static CharacterConfig ParseCharacter(ConfigObject root)
        {
            var config = new CharacterConfig
            {
                MaxHp = PositiveInt(root, "maxHp"),
                MaxEnergy = PositiveInt(root, "maxEnergy"),
                EnergyRegenPerSec = root.NotNegative("energyRegenPerSec"),
                MaxStamina = PositiveInt(root, "maxStamina"),
                Attack = root.PositiveDecimal("attack"),
                Defense = root.NotNegativeDecimal("defense"),
            };

            ReadEntry(root, config);
            root.RejectUnknownFields();
            return config;
        }

        private static MovementConfig ParseMovement(ConfigObject root)
        {
            var config = new MovementConfig
            {
                RunSpeed = root.Positive("runSpeed"),
                WalkSpeed = root.Positive("walkSpeed"),
                WalkTiltBelow = root.Share("walkTiltBelow"),
                JumpHeight = root.Positive("jumpHeight"),
                JumpTimeToPeakSec = root.Positive("jumpTimeToPeakSec"),
                JumpSwipeAbove = root.Share("jumpSwipeAbove"),
                CoyoteTimeSec = root.NotNegative("coyoteTimeSec"),
                InputBufferSec = root.NotNegative("inputBufferSec"),
                DashDistance = root.Positive("dashDistance"),
                DashDurationSec = root.Positive("dashDurationSec"),
                DashStaminaCost = root.NotNegative("dashStaminaCost"),
                AirDashesPerJump = NotNegativeInt(root, "airDashesPerJump"),
                StaminaRegenIdle = root.NotNegative("staminaRegenIdle"),
                StaminaRegenWalk = root.NotNegative("staminaRegenWalk"),
                StaminaRegenAttacking = root.NotNegative("staminaRegenAttacking"),
                StaminaRegenDelaySec = root.NotNegative("staminaRegenDelaySec"),
                BodyWidth = root.Positive("bodyWidth"),
                BodyHeight = root.Positive("bodyHeight"),
            };

            ReadEntry(root, config);
            root.RejectUnknownFields();
            return config;
        }

        private static IReadOnlyList<MapBox> ParseMap(ConfigObject root)
        {
            var header = new MapHeader();
            ReadEntry(root, header);

            var ids = new HashSet<string>();
            var boxes = new List<MapBox>();

            foreach (ConfigObject item in root.Array("boxes"))
            {
                string id = ReadId(item);
                var box = new MapBox
                {
                    Id = id,
                    MinX = item.Double("minX"),
                    MinY = item.Double("minY"),
                    MaxX = item.Double("maxX"),
                    MaxY = item.Double("maxY"),
                    OneWay = item.Boolean("oneWay"),
                };

                if (ids.Add(id) == false)
                {
                    throw item.Fail("id", $"duplicate id '{id}'");
                }

                if (box.MaxX <= box.MinX)
                {
                    throw item.Fail("maxX", "must be greater than minX");
                }

                if (box.MaxY <= box.MinY)
                {
                    throw item.Fail("maxY", "must be greater than minY");
                }

                item.RejectUnknownFields();
                boxes.Add(box);
            }

            root.RejectUnknownFields();

            return boxes.Count > 0 ? boxes : throw root.Fail("boxes", "needs at least one box");
        }

        private static IReadOnlyList<SkillConfig> ParseSkills(ConfigObject root)
        {
            var ids = new HashSet<string>();
            var slots = new HashSet<SkillSlot>();
            var skills = new List<SkillConfig>();

            foreach (ConfigObject item in root.Array("skills"))
            {
                SkillConfig skill = ParseSkill(item);

                if (ids.Add(skill.Id) == false)
                {
                    throw item.Fail("id", $"duplicate id '{skill.Id}'");
                }

                if (slots.Add(skill.Slot) == false)
                {
                    throw item.Fail("slot", $"slot {skill.Slot} is already used");
                }

                skills.Add(skill);
            }

            root.RejectUnknownFields();

            foreach (SkillSlot slot in Slots.Values)
            {
                if (slots.Contains(slot) == false)
                {
                    throw root.Fail("skills", $"no skill for slot {slot}");
                }
            }

            return skills;
        }

        private static SkillConfig ParseSkill(ConfigObject item)
        {
            var skill = new SkillConfig
            {
                Slot = Lookup(item, "slot", Slots),
                CooldownSec = item.NotNegative("cooldownSec"),
                EnergyCost = NotNegativeInt(item, "energyCost"),
                BaseDamage = item.NotNegativeDecimal("baseDamage"),
                AttackCoefficient = item.NotNegativeDecimal("attackCoefficient"),
                LevelPercent = item.Has("levelPercent") ? item.NotNegativeDecimal("levelPercent") : 0m,
                WindupSec = item.NotNegative("windupSec"),
                RecoverySec = item.NotNegative("recoverySec"),
                MoveSpeedPercent = item.Percent("moveSpeedPercent"),
                AllowedInAir = item.Boolean("allowedInAir"),
                DashDistance = item.Has("dashDistance") ? item.Positive("dashDistance") : 0d,
                ActivePhases = item.Has("activePhases") ? ParsePhases(item) : Array.Empty<SkillPhase>(),
                Hitbox = item.Has("hitbox") ? ParseHitbox(item.Object("hitbox")) : null,
                Projectile = item.Has("projectile") ? ParseProjectile(item.Object("projectile")) : null,
                Heal = item.Has("heal") ? ParseHeal(item.Object("heal")) : null,
                Slow = item.Has("slow") ? ParseSlow(item.Object("slow")) : null,
            };

            ReadEntry(item, skill);
            item.RejectUnknownFields();
            CheckSkillShape(item, skill);
            return skill;
        }

        private static IReadOnlyList<SkillPhase> ParsePhases(ConfigObject item)
        {
            var phases = new List<SkillPhase>();

            foreach (ConfigObject phase in item.Array("activePhases"))
            {
                phases.Add(new SkillPhase
                {
                    DurationSec = phase.Positive("durationSec"),
                    DamagePercent = phase.PositiveDecimal("damagePercent"),
                });
                phase.RejectUnknownFields();
            }

            if (phases.Sum(phase => phase.DamagePercent) != WholePercent)
            {
                throw item.Fail("activePhases", "damagePercent must add up to 100");
            }

            return phases;
        }

        private static SkillHitbox ParseHitbox(ConfigObject item)
        {
            var hitbox = new SkillHitbox
            {
                Width = item.Positive("width"),
                Height = item.Positive("height"),
                Pierce = item.Boolean("pierce"),
            };

            item.RejectUnknownFields();
            return hitbox;
        }

        private static SkillProjectile ParseProjectile(ConfigObject item)
        {
            var projectile = new SkillProjectile
            {
                Speed = item.Positive("speed"),
                Range = item.Positive("range"),
                Width = item.Positive("width"),
            };

            item.RejectUnknownFields();
            return projectile;
        }

        private static SkillHeal ParseHeal(ConfigObject item)
        {
            var heal = new SkillHeal
            {
                Base = item.NotNegativeDecimal("base"),
                MaxHpPercent = item.NotNegativeDecimal("maxHpPercent"),
            };

            item.RejectUnknownFields();
            return heal;
        }

        private static SkillSlow ParseSlow(ConfigObject item)
        {
            var slow = new SkillSlow
            {
                Percent = item.Percent("percent"),
                DurationSec = item.Positive("durationSec"),
            };

            item.RejectUnknownFields();
            return slow;
        }

        private static void CheckSkillShape(ConfigObject item, SkillConfig skill)
        {
            bool hasPhases = skill.ActivePhases.Count > 0;

            if (hasPhases && skill.Projectile != null)
            {
                throw item.Fail("projectile", "cannot be combined with activePhases");
            }

            if (hasPhases != (skill.Hitbox != null))
            {
                throw item.Fail("hitbox", "activePhases and hitbox must be set together");
            }

            if (skill.DealsDamage && hasPhases == false && skill.Projectile == null)
            {
                throw item.Fail("activePhases", "a damaging skill needs activePhases or projectile");
            }

            if (skill.DealsDamage == false && skill.Heal == null)
            {
                throw item.Fail("heal", "a skill without damage must heal");
            }

            if (skill.Slot == SkillSlot.Attack && item.Has("levelPercent"))
            {
                throw item.Fail("levelPercent", "the basic attack has no skill level (GDD 8.3)");
            }
        }

        private static void ReadEntry(ConfigObject item, ConfigEntry entry)
        {
            entry.Id = ReadId(item);
            entry.Status = Lookup(item, "status", Statuses);
            entry.Source = item.String("source");

            if (entry.Source.Trim().Length == 0)
            {
                throw item.Fail("source", "cannot be empty");
            }
        }

        private static string ReadId(ConfigObject item)
        {
            string id = item.String("id");
            return IdPattern.IsMatch(id) ? id : throw item.Fail("id", $"'{id}' must be lowercase without marks");
        }

        private static T Lookup<T>(ConfigObject item, string name, Dictionary<string, T> values)
            where T : struct
        {
            string text = item.String(name);

            if (values.TryGetValue(text, out T value))
            {
                return value;
            }

            throw item.Fail(name, $"'{text}' is not one of {string.Join(", ", values.Keys)}");
        }

        private static int PositiveInt(ConfigObject item, string name)
        {
            int value = item.Int(name);
            return value > 0 ? value : throw item.Fail(name, "must be greater than 0");
        }

        private static int NotNegativeInt(ConfigObject item, string name)
        {
            int value = item.Int(name);
            return value >= 0 ? value : throw item.Fail(name, "cannot be negative");
        }

        private sealed class MapHeader : ConfigEntry
        {
        }
    }
}
