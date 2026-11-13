using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Game.Rules
{
    /// <summary>All config files, parsed and checked. Any problem stops loading with a clear message.</summary>
    public sealed partial class GameConfig
    {
        public const string CharacterFile = "character.json";
        public const string MovementFile = "movement.json";
        public const string MapFile = "map.test.json";
        public const string HouseFile = "map.house.json";
        public const string VillageFile = "map.village.json";
        public const string SkillsFile = "skills.thuy.json";
        public const string ItemsFile = "items.json";
        public const string BossFile = "boss.json";
        public const string ServerFile = "server.json";

        private const int SupportedVersion = 1;
        private const decimal WholePercent = 100m;
        private const int MinComboHits = 2;

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

        private static readonly Dictionary<string, BossAttackShape> Shapes = new Dictionary<string, BossAttackShape>
        {
            ["front"] = BossAttackShape.Front,
            ["both_sides"] = BossAttackShape.BothSides,
            ["column"] = BossAttackShape.Column,
        };

        private GameConfig(
            CharacterConfig character,
            MovementConfig movement,
            IReadOnlyList<MapConfig> maps,
            IReadOnlyList<SkillConfig> skills,
            PotionConfig potion,
            IReadOnlyList<GearItemConfig> gear,
            BagConfig bag,
            BossConfig boss)
        {
            Character = character;
            Movement = movement;
            Maps = maps;
            Skills = skills;
            Potion = potion;
            Gear = gear;
            Bag = bag;
            Boss = boss;
        }

        public static IReadOnlyList<string> FileNames { get; } = new[]
        {
            CharacterFile,
            MovementFile,
            MapFile,
            HouseFile,
            VillageFile,
            SkillsFile,
            ItemsFile,
            BossFile,
        };

        public CharacterConfig Character { get; }
        public MovementConfig Movement { get; }

        /// <summary>
        /// Every map, in the order of <see cref="FileNames"/>; a map travels as its place here. The first is
        /// the test map with the boss, where practising alone happens.
        /// </summary>
        public IReadOnlyList<MapConfig> Maps { get; }

        /// <summary>The test map's boxes.</summary>
        public IReadOnlyList<MapBox> MapBoxes => Maps[0].Boxes;

        /// <summary>The place in <see cref="Maps"/> of the map with this id; -1 when there is none.</summary>
        public int MapIndex(string id)
        {
            for (int index = 0; index < Maps.Count; index++)
            {
                if (Maps[index].Id == id)
                {
                    return index;
                }
            }

            return -1;
        }
        public IReadOnlyList<SkillConfig> Skills { get; }
        public PotionConfig Potion { get; }

        /// <summary>The equipment catalog, in file order; an item travels as its place in this list.</summary>
        public IReadOnlyList<GearItemConfig> Gear { get; }

        /// <summary>Bag size, the ground and the starter items.</summary>
        public BagConfig Bag { get; }

        public BossConfig Boss { get; }

        /// <summary>Takes the text of each file in <see cref="FileNames"/>, keyed by file name.</summary>
        public static GameConfig Parse(IReadOnlyDictionary<string, string> files)
        {
            ConfigObject items = Open(files, ItemsFile);
            PotionConfig potion = ParsePotion(items);
            IReadOnlyList<GearItemConfig> gear = ParseGear(items);
            BagConfig bag = ParseBag(items, gear);
            items.RejectUnknownFields();

            return new GameConfig(
                ParseCharacter(Open(files, CharacterFile)),
                ParseMovement(Open(files, MovementFile)),
                ParseMaps(files),
                ParseSkills(Open(files, SkillsFile)),
                potion,
                gear,
                bag,
                ParseBoss(Open(files, BossFile)));
        }

        public SkillConfig Skill(SkillSlot slot) => Skills.First(skill => skill.Slot == slot);

        /// <summary>Reads <see cref="ServerFile"/>. Only the server needs it, so it is not in <see cref="FileNames"/>.</summary>
        public static ServerConfig ParseServer(string json)
        {
            ConfigObject root = Open(new Dictionary<string, string> { [ServerFile] = json }, ServerFile);
            var config = new ServerConfig
            {
                ServerNumber = PositiveInt(root, "serverNumber"),
                ServerName = root.String("serverName"),
                ServerIsNew = root.Boolean("serverIsNew"),
                Channels = PositiveInt(root, "channels"),
                PlayersPerChannel = PositiveInt(root, "playersPerChannel"),
                SpawnX = root.Double("spawnX"),
                StartMap = root.String("startMap"),
                WeatherMinSec = root.Positive("weatherMinSec"),
                WeatherMaxSec = root.Positive("weatherMaxSec"),
                SnapshotEveryTicks = PositiveInt(root, "snapshotEveryTicks"),
                ReconnectHoldSec = root.NotNegative("reconnectHoldSec"),
                MaxQueuedInputs = PositiveInt(root, "maxQueuedInputs"),
                PlayerRespawnSec = root.Positive("playerRespawnSec"),
                BossRespawnSec = root.Positive("bossRespawnSec"),
                TestRoom = root.Boolean("testRoom"),
            };

            ReadEntry(root, config);
            root.RejectUnknownFields();
            return config;
        }

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

        private static PotionConfig ParsePotion(ConfigObject root)
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
            return potion;
        }

        private static readonly Dictionary<string, GearSlot> GearSlots = new Dictionary<string, GearSlot>
        {
            ["weapon"] = GearSlot.Weapon,
            ["armor"] = GearSlot.Armor,
            ["pants"] = GearSlot.Pants,
            ["hat"] = GearSlot.Hat,
            ["cape"] = GearSlot.Cape,
        };

        private static readonly Regex ArtPattern = new Regex("^[a-z0-9]+$");

        /// <summary>The bag. A slot travels as one byte; the starter items must fit in it.</summary>
        private static BagConfig ParseBag(ConfigObject root, IReadOnlyList<GearItemConfig> gear)
        {
            ConfigObject item = root.Object("bag");
            var bag = new BagConfig
            {
                Slots = PositiveInt(item, "slots"),
                GroundLifetimeSec = item.Positive("groundLifetimeSec"),
                PickupRange = item.Positive("pickupRange"),
            };

            ReadEntry(item, bag);

            if (bag.Slots > byte.MaxValue)
            {
                throw item.Fail("slots", $"at most {byte.MaxValue}");
            }

            var starter = new List<int>();

            foreach (string id in item.Strings("starterBag"))
            {
                int index = gear.ToList().FindIndex(entry => entry.Id == id);
                starter.Add(index >= 0 ? index : throw item.Fail("starterBag", $"unknown item '{id}'"));
            }

            if (starter.Count > bag.Slots)
            {
                throw item.Fail("starterBag", $"has {starter.Count} items for {bag.Slots} slots");
            }

            bag.StarterBag = starter;
            item.RejectUnknownFields();
            return bag;
        }

        /// <summary>The equipment catalog. An item travels as one byte, so there can be at most 255.</summary>
        private static IReadOnlyList<GearItemConfig> ParseGear(ConfigObject root)
        {
            var gear = new List<GearItemConfig>();
            var ids = new HashSet<string>();
            var arts = new HashSet<string>();

            foreach (ConfigObject entry in root.Array("gear"))
            {
                var item = new GearItemConfig
                {
                    Name = entry.String("name"),
                    Slot = Lookup(entry, "slot", GearSlots),
                    Tier = PositiveInt(entry, "tier"),
                    Art = entry.String("art"),
                    Default = entry.Has("default") && entry.Boolean("default"),
                };

                ReadEntry(entry, item);
                entry.RejectUnknownFields();

                if (item.Name.Trim().Length == 0)
                {
                    throw entry.Fail("name", "cannot be empty");
                }

                if (!ArtPattern.IsMatch(item.Art))
                {
                    throw entry.Fail("art", "must be lower-case letters and digits, as in the file names");
                }

                if (!ids.Add(item.Id))
                {
                    throw entry.Fail("id", $"duplicate id '{item.Id}'");
                }

                if (!arts.Add($"{item.Slot}_{item.Art}"))
                {
                    throw entry.Fail("art", $"another {item.Slot} item already uses '{item.Art}'");
                }

                if (item.Default && !GearRules.IsShown(item.Slot))
                {
                    throw entry.Fail("default", "only weapon, armor and pants items are worn by default");
                }

                gear.Add(item);
            }

            if (gear.Count > byte.MaxValue)
            {
                throw root.Fail("gear", $"at most {byte.MaxValue} items");
            }

            foreach (GearSlot slot in GearRules.ShownSlots)
            {
                int defaults = gear.Count(item => item.Slot == slot && item.Default);

                if (defaults != 1)
                {
                    throw root.Fail("gear", $"needs exactly one default {slot.ToString().ToLowerInvariant()} item, found {defaults}");
                }
            }

            return gear;
        }

        private static BossConfig ParseBoss(ConfigObject root)
        {
            var attacks = new Dictionary<string, BossAttackConfig>();

            foreach (ConfigObject entry in root.Array("attacks"))
            {
                BossAttackConfig attack = ParseBossAttack(entry);

                if (attacks.ContainsKey(attack.Id))
                {
                    throw entry.Fail("id", $"duplicate id '{attack.Id}'");
                }

                attacks[attack.Id] = attack;
            }

            ConfigObject item = root.Object("boss");
            var boss = new BossConfig
            {
                MaxHp = PositiveInt(item, "maxHp"),
                Defense = item.NotNegativeDecimal("defense"),
                Width = item.Positive("width"),
                Height = item.Positive("height"),
                MoveSpeed = item.Positive("moveSpeed"),
                StartDistance = item.Positive("startDistance"),
                EnrageHpPercent = item.Percent("enrageHpPercent"),
                EnrageRestPercent = item.Percent("enrageRestPercent"),
                Cycle = ParseCycle(item, attacks),
            };

            ReadEntry(item, boss);
            item.RejectUnknownFields();
            root.RejectUnknownFields();
            return boss;
        }

        private static IReadOnlyList<BossAttackConfig> ParseCycle(
            ConfigObject item,
            Dictionary<string, BossAttackConfig> attacks)
        {
            var cycle = new List<BossAttackConfig>();

            foreach (string id in item.Strings("cycle"))
            {
                if (attacks.TryGetValue(id, out BossAttackConfig? attack) == false)
                {
                    throw item.Fail("cycle", $"unknown attack '{id}'");
                }

                cycle.Add(attack);
            }

            return cycle.Count > 0 ? cycle : throw item.Fail("cycle", "needs at least one attack");
        }

        private static BossAttackConfig ParseBossAttack(ConfigObject item)
        {
            var attack = new BossAttackConfig
            {
                Name = item.String("name"),
                Shape = Lookup(item, "shape", Shapes),
                WarningSec = item.Positive("warningSec"),
                Width = item.Positive("width"),
                Height = item.Positive("height"),
                Damage = PositiveInt(item, "damage"),
                RestSec = item.NotNegative("restSec"),
                ApproachRange = item.Has("approachRange") ? item.Positive("approachRange") : 0d,
                SideDelaySec = item.Has("sideDelaySec") ? item.Positive("sideDelaySec") : 0d,
            };

            ReadEntry(item, attack);
            item.RejectUnknownFields();

            if ((attack.Shape == BossAttackShape.BothSides) != (attack.SideDelaySec > 0d))
            {
                throw item.Fail("sideDelaySec", "is required for both_sides and only for it");
            }

            return attack;
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

        private static IReadOnlyList<MapConfig> ParseMaps(IReadOnlyDictionary<string, string> files)
        {
            var maps = new List<MapConfig>();
            var roots = new List<ConfigObject>();

            foreach (string file in new[] { MapFile, HouseFile, VillageFile })
            {
                ConfigObject root = Open(files, file);
                MapConfig map = ParseMap(root);

                if (maps.Exists(other => other.Id == map.Id))
                {
                    throw root.Fail("id", $"another map is already '{map.Id}'");
                }

                maps.Add(map);
                roots.Add(root);
            }

            // Checked once every map is known, since a door may lead to a map read later.
            for (int index = 0; index < maps.Count; index++)
            {
                foreach (MapDoor door in maps[index].Doors)
                {
                    MapConfig? to = maps.Find(map => map.Id == door.ToMap);

                    if (to == null)
                    {
                        throw roots[index].Fail("doors", $"door '{door.Id}' leads to unknown map '{door.ToMap}'");
                    }

                    // Arriving inside a way out that is walked through would send the player straight back.
                    foreach (MapDoor edge in to.Doors)
                    {
                        if (edge.Walk && Math.Abs(door.ToX - edge.X) <= edge.Reach)
                        {
                            throw roots[index].Fail("doors", $"door '{door.Id}' lands inside '{edge.Id}' of {to.Id}, which would send the player back");
                        }
                    }
                }
            }

            return maps;
        }

        private static MapConfig ParseMap(ConfigObject root)
        {
            var map = new MapConfig();
            ReadEntry(root, map);
            map.SpawnX = root.Has("spawnX") ? root.Double("spawnX") : (double?)null;
            map.Boss = root.Boolean("boss");
            map.Shared = root.Boolean("shared");
            map.Boxes = ParseBoxes(root);
            map.Doors = ParseDoors(root);
            map.Training = root.Has("training") ? ParseTraining(root.Object("training")) : null;
            root.RejectUnknownFields();
            return map;
        }

        private static TrainingConfig ParseTraining(ConfigObject item)
        {
            var training = new TrainingConfig
            {
                MaxHp = PositiveInt(item, "maxHp"),
                DamagePerHit = PositiveInt(item, "damagePerHit"),
                RespawnSec = item.Positive("respawnSec"),
                Width = item.Positive("width"),
                Height = item.Positive("height"),
            };

            var ids = new HashSet<string>();
            var dummies = new List<MapDummy>();

            foreach (ConfigObject entry in item.Array("dummies"))
            {
                var dummy = new MapDummy { Id = ReadId(entry), X = entry.Double("x") };

                if (!ids.Add(dummy.Id))
                {
                    throw entry.Fail("id", $"duplicate id '{dummy.Id}'");
                }

                entry.RejectUnknownFields();
                dummies.Add(dummy);
            }

            if (dummies.Count == 0 || dummies.Count > byte.MaxValue)
            {
                throw item.Fail("dummies", "needs 1 to 255 dummies");
            }

            // Snapshots carry a dummy's blows in one byte.
            if (training.HitsToFall > byte.MaxValue)
            {
                throw item.Fail("damagePerHit", "a dummy must fall within 255 blows");
            }

            training.Dummies = dummies;
            item.RejectUnknownFields();
            return training;
        }

        private static IReadOnlyList<MapDoor> ParseDoors(ConfigObject root)
        {
            var ids = new HashSet<string>();
            var doors = new List<MapDoor>();

            foreach (ConfigObject item in root.Array("doors"))
            {
                var door = new MapDoor
                {
                    Id = ReadId(item),
                    Name = item.String("name"),
                    X = item.Double("x"),
                    Reach = item.Positive("reach"),
                    ToMap = item.String("toMap"),
                    ToX = item.Double("toX"),
                    Walk = item.Boolean("walk"),
                };

                if (!ids.Add(door.Id))
                {
                    throw item.Fail("id", $"duplicate id '{door.Id}'");
                }

                item.RejectUnknownFields();
                doors.Add(door);
            }

            return doors;
        }

        private static IReadOnlyList<MapBox> ParseBoxes(ConfigObject root)
        {
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
                Combo = item.Has("combo") ? ParseCombo(item.Object("combo")) : null,
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

        private static SkillCombo ParseCombo(ConfigObject item)
        {
            var percents = new List<decimal>();

            foreach (ConfigObject hit in item.Array("hits"))
            {
                percents.Add(hit.PositiveDecimal("damagePercent"));
                hit.RejectUnknownFields();
            }

            if (percents.Count < MinComboHits)
            {
                throw item.Fail("hits", $"a combo needs at least {MinComboHits} hits");
            }

            var combo = new SkillCombo { WindowSec = item.Positive("windowSec"), HitDamagePercents = percents };

            ReadEntry(item, combo);
            item.RejectUnknownFields();
            return combo;
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

    }
}
