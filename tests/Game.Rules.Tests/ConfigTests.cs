using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.Rules.Tests
{
    public class ConfigTests
    {
        private const string Character = GameConfig.CharacterFile;
        private const string Movement = GameConfig.MovementFile;
        private const string Map = GameConfig.MapFile;
        private const string Skills = GameConfig.SkillsFile;

        private static SkillConfig Skill(string id) => TestConfig.Load().Skills.Single(skill => skill.Id == id);

        private static InvalidConfigException Rejected(string file, string find, string replace)
        {
            Dictionary<string, string> files = TestConfig.Files();
            Assert.Contains(find, files[file]);
            files[file] = files[file].Replace(find, replace);

            return Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        private static InvalidConfigException RejectedText(string file, string text)
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[file] = text;

            return Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));
        }

        [Fact]
        public void RealFilesLoad()
        {
            GameConfig config = TestConfig.Load();

            Assert.Equal(6, config.Skills.Count);
            Assert.Equal(6, config.Skills.Select(skill => skill.Slot).Distinct().Count());
            Assert.Equal(5, config.MapBoxes.Count);
            Assert.Equal(ConfigStatus.Thu, config.Movement.Status);
        }

        [Fact]
        public void PotionMatchesSpec()
        {
            PotionConfig potion = TestConfig.Load().Potion;

            Assert.Equal(300, potion.HealHp);
            Assert.Equal(5, potion.Charges);
            Assert.Equal(5d, potion.CooldownSec);
        }

        [Theory]
        [InlineData("\"healHp\": 300,", "", "missing field")]
        [InlineData("\"charges\": 5", "\"charges\": 0", "greater than 0")]
        [InlineData("\"cooldownSec\": 5", "\"cooldownSec\": -5", "cannot be negative")]
        [InlineData("\"charges\": 5", "\"charges\": 5, \"name\": 1", "unknown field")]
        public void BadPotionIsRejected(string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(GameConfig.ItemsFile, find, replace).Message);
        }

        [Theory]
        [InlineData("\"maxHp\": 5000,", "", "missing field")]
        [InlineData("\"maxHp\": 5000,", "\"maxHp\": 0,", "greater than 0")]
        [InlineData("\"boss.don_lon\"]", "\"boss.khong_co\"]", "unknown attack")]
        [InlineData("[\"boss.chem\", \"boss.chem\", \"boss.quet\", \"boss.chem\", \"boss.don_lon\"]", "[]", "at least one attack")]
        [InlineData("\"boss.don_lon\"]", "1]", "only strings")]
        [InlineData("\"id\": \"boss.quet\"", "\"id\": \"boss.chem\"", "duplicate id")]
        [InlineData("\"shape\": \"column\"", "\"shape\": \"round\"", "not one of")]
        [InlineData("\"warningSec\": 0.6", "\"warningSec\": 0", "greater than 0")]
        [InlineData("\"sideDelaySec\": 0.2", "\"approachRange\": 2", "required for both_sides")]
        [InlineData("\"restSec\": 2", "\"restSec\": 2, \"sideDelaySec\": 1", "only for it")]
        [InlineData("\"enrageRestPercent\": 75", "\"enrageRestPercent\": 175", "from 0 to 100")]
        [InlineData("\"restSec\": 2", "\"restSec\": 2, \"note\": 1", "unknown field")]
        public void BadBossIsRejected(string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(GameConfig.BossFile, find, replace).Message);
        }

        [Fact]
        public void CharacterMatchesFixture()
        {
            CharacterConfig character = TestConfig.Load().Character;

            Assert.Equal(1000, character.MaxHp);
            Assert.Equal(100, character.MaxEnergy);
            Assert.Equal(100, character.MaxStamina);
            Assert.Equal(100m, character.Attack);
            Assert.Equal(0m, character.Defense);
        }

        [Theory]
        [InlineData("thuy.danh_thuong", 0d, 0, 0.15d, 0.2d, 60d, true)]
        [InlineData("thuy.kiem_khi", 4d, 8, 0.2d, 0.2d, 60d, true)]
        [InlineData("thuy.hoi_mau", 20d, 25, 0.3d, 0.2d, 0d, false)]
        [InlineData("thuy.chem_tien", 7d, 12, 0.1d, 0.3d, 0d, false)]
        [InlineData("thuy.song_kiem", 10d, 15, 0.25d, 0.25d, 0d, false)]
        [InlineData("thuy.tuyet_ky", 35d, 35, 0.2d, 0.3d, 0d, false)]
        public void SkillsMatchSpec(string id, double cooldown, int energy, double windup, double recovery, double moveSpeed, bool inAir)
        {
            SkillConfig skill = Skill(id);

            Assert.Equal(cooldown, skill.CooldownSec);
            Assert.Equal(energy, skill.EnergyCost);
            Assert.Equal(windup, skill.WindupSec);
            Assert.Equal(recovery, skill.RecoverySec);
            Assert.Equal(moveSpeed, skill.MoveSpeedPercent);
            Assert.Equal(inAir, skill.AllowedInAir);
        }

        [Theory]
        [InlineData("thuy.danh_thuong", 60)]
        [InlineData("thuy.kiem_khi", 160)]
        [InlineData("thuy.chem_tien", 160)]
        [InlineData("thuy.song_kiem", 130)]
        [InlineData("thuy.tuyet_ky", 600)]
        public void DamageAtLevelOneMatchesDesign(string id, int expected)
        {
            SkillConfig skill = Skill(id);
            decimal attack = TestConfig.Load().Character.Attack;

            decimal damage = SkillDamage.BeforeDefense(skill.BaseDamage, attack, skill.AttackCoefficient, skill.LevelPercent, 1);

            Assert.Equal(expected, SkillDamage.AfterDefense(damage, 0m));
        }

        [Fact]
        public void KiemKhiLevelFiveMatchesFormulaExample()
        {
            SkillConfig skill = Skill("thuy.kiem_khi");

            decimal damage = SkillDamage.BeforeDefense(skill.BaseDamage, 100m, skill.AttackCoefficient, skill.LevelPercent, 5);

            Assert.Equal(224m, damage);
        }

        [Fact]
        public void BasicAttackIsAThreeHitCombo()
        {
            SkillCombo combo = Skill("thuy.danh_thuong").Combo!;

            Assert.Equal(new[] { 100m, 100m, 130m }, combo.HitDamagePercents);
            Assert.Equal(ConfigStatus.DeXuat, combo.Status);
        }

        [Fact]
        public void ComboNeedsAtLeastTwoHits()
        {
            InvalidConfigException error = Rejected(
                Skills,
                "{ \"damagePercent\": 100 },\n          { \"damagePercent\": 100 },\n          { \"damagePercent\": 130 }",
                "{ \"damagePercent\": 100 }");

            Assert.Contains("hits", error.Message);
        }

        [Fact]
        public void ComboWindowMustBePositive()
        {
            InvalidConfigException error = Rejected(Skills, "\"windowSec\": 0.5", "\"windowSec\": 0");

            Assert.Contains("windowSec", error.Message);
        }

        [Fact]
        public void BasicAttackTakesUnderHalfASecond()
        {
            SkillConfig skill = Skill("thuy.danh_thuong");

            double total = skill.WindupSec + skill.ActivePhases.Sum(phase => phase.DurationSec) + skill.RecoverySec;

            Assert.Equal(0.45d, total, 10);
        }

        [Fact]
        public void UltimateSplitsDamageIntoThreePhases()
        {
            SkillConfig skill = Skill("thuy.tuyet_ky");

            Assert.Equal(new[] { 25m, 25m, 50m }, skill.ActivePhases.Select(phase => phase.DamagePercent));
            Assert.Equal(new[] { 0.3d, 0.3d, 0.4d }, skill.ActivePhases.Select(phase => phase.DurationSec));
        }

        [Fact]
        public void SkillDetailsAreRead()
        {
            Assert.Equal(10d, Skill("thuy.kiem_khi").Projectile!.Speed);
            Assert.Equal(10m, Skill("thuy.hoi_mau").Heal!.MaxHpPercent);
            Assert.Equal(100m, Skill("thuy.hoi_mau").Heal!.Base);
            Assert.Equal(3d, Skill("thuy.chem_tien").DashDistance);
            Assert.True(Skill("thuy.song_kiem").Hitbox!.Pierce);
            Assert.Equal(20d, Skill("thuy.song_kiem").Slow!.Percent);
            Assert.Equal(SkillSlot.OptionA, Skill("thuy.chem_tien").Slot);
        }

        [Fact]
        public void NumbersDoNotDependOnPhoneLanguage()
        {
            CultureInfo original = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

                Assert.Equal(0.15d, Skill("thuy.danh_thuong").WindupSec);
                Assert.Equal(1.2m, Skill("thuy.kiem_khi").AttackCoefficient);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void ErrorNamesFileAndField()
        {
            InvalidConfigException error = Rejected(Skills, "\"energyCost\": 8,", string.Empty);

            Assert.Equal("skills.thuy.json: root.skills[1].energyCost: missing field", error.Message);
        }

        [Fact]
        public void BrokenJsonReportsTheLine()
        {
            InvalidConfigException error = RejectedText(Character, "{\n  \"version\": 1,\n  oops\n}");

            Assert.Equal("character.json: line 3: expected a field name in double quotes", error.Message);
        }

        [Fact]
        public void MissingFileIsRejected()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files.Remove(Skills);

            var error = Assert.Throws<InvalidConfigException>(() => GameConfig.Parse(files));

            Assert.Equal("skills.thuy.json: file: file is missing", error.Message);
        }

        [Theory]
        [InlineData(Skills, "\"energyCost\": 8,", "\"energyCost\": \"8\",", "must be a number")]
        [InlineData(Character, "\"attack\": 100", "\"attack\": null", "must be a number")]
        [InlineData(Skills, "\"allowedInAir\": true", "\"allowedInAir\": 1", "must be true or false")]
        [InlineData(Skills, "\"source\": \"GDD 18.1 va 8.3, SPEC-0.5 buoc 4\"", "\"source\": 5", "must be a string")]
        [InlineData(Skills, "\"hitbox\": { \"width\": 4, \"height\": 1.5, \"pierce\": true }", "\"hitbox\": [1]", "must be an object")]
        [InlineData(Character, "\"maxHp\": 1000", "\"maxHp\": 10.5", "must be a whole number")]
        public void WrongTypeIsRejected(string file, string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(file, find, replace).Message);
        }

        [Theory]
        [InlineData(Skills, "\"id\": \"thuy.kiem_khi\"", "\"id\": \"thuy.danh_thuong\"", "duplicate id")]
        [InlineData(Map, "\"id\": \"wall.right\"", "\"id\": \"wall.left\"", "duplicate id")]
        [InlineData(Skills, "\"slot\": \"optionB\"", "\"slot\": \"optionA\"", "already used")]
        public void DuplicateIsRejected(string file, string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(file, find, replace).Message);
        }

        [Theory]
        [InlineData(Character, "\"maxHp\": 1000,", "", "missing field")]
        [InlineData(Movement, "\"runSpeed\": 4,", "", "missing field")]
        [InlineData(Character, "\"status\": \"THU\",", "", "missing field")]
        [InlineData(Character, "\"source\": \"GDD 18.1, 18.2 va 19.4\",", "", "missing field")]
        [InlineData(Map, "\"id\": \"map.test\",", "", "missing field")]
        [InlineData(Map, "\"oneWay\": false }", "\"unused\": false }", "missing field")]
        public void MissingFieldIsRejected(string file, string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(file, find, replace).Message);
        }

        [Theory]
        [InlineData(Character, "\"maxHp\": 1000", "\"maxHp\": -1", "greater than 0")]
        [InlineData(Character, "\"defense\": 0", "\"defense\": -1", "cannot be negative")]
        [InlineData(Character, "\"version\": 1", "\"version\": 2", "only version 1")]
        [InlineData(Character, "\"status\": \"THU\"", "\"status\": \"OK\"", "not one of")]
        [InlineData(Character, "\"source\": \"GDD 18.1, 18.2 va 19.4\"", "\"source\": \" \"", "cannot be empty")]
        [InlineData(Character, "\"id\": \"character.fixture\"", "\"id\": \"Nhân Vật\"", "must be lowercase")]
        [InlineData(Movement, "\"walkTiltBelow\": 0.5", "\"walkTiltBelow\": 1.5", "between 0 and 1")]
        [InlineData(Movement, "\"dashDurationSec\": 0.2", "\"dashDurationSec\": 0", "greater than 0")]
        [InlineData(Map, "\"maxX\": 11", "\"maxX\": 7", "greater than minX")]
        [InlineData(Map, "\"maxY\": 1.5", "\"maxY\": 1", "greater than minY")]
        [InlineData(Skills, "\"moveSpeedPercent\": 60", "\"moveSpeedPercent\": 160", "from 0 to 100")]
        [InlineData(Skills, "\"slot\": \"ranged\"", "\"slot\": \"xa\"", "not one of")]
        public void BadValueIsRejected(string file, string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(file, find, replace).Message);
        }

        [Theory]
        [InlineData("\"damagePercent\": 50", "\"damagePercent\": 40", "add up to 100")]
        [InlineData("\"hitbox\": { \"width\": 4, \"height\": 1.5, \"pierce\": true },", "", "set together")]
        [InlineData("\"projectile\": { \"speed\": 10, \"range\": 6, \"width\": 0.5 }", "\"dashDistance\": 1", "activePhases or projectile")]
        [InlineData("\"heal\": { \"base\": 100, \"maxHpPercent\": 10 }", "\"dashDistance\": 1", "must heal")]
        [InlineData("\"attackCoefficient\": 0.6,", "\"attackCoefficient\": 0.6, \"levelPercent\": 5,", "no skill level")]
        public void InconsistentSkillIsRejected(string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(Skills, find, replace).Message);
        }

        [Fact]
        public void ArrayOfWrongTypeIsRejected()
        {
            InvalidConfigException error = RejectedText(Skills, "{ \"version\": 1, \"skills\": 1 }");

            Assert.Contains("must be an array", error.Message);
        }

        [Fact]
        public void MissingSlotIsRejected()
        {
            InvalidConfigException error = RejectedText(Skills, "{ \"version\": 1, \"skills\": [] }");

            Assert.Contains("no skill for slot Attack", error.Message);
        }

        [Theory]
        [InlineData(Character, "\"maxHp\": 1000,", "\"maxHp\": 1000, \"hp\": 1,", "unknown field")]
        [InlineData(Skills, "\"energyCost\": 8,", "\"energyCost\": 8, \"levelPercnt\": 1,", "unknown field")]
        [InlineData(Skills, "\"speed\": 10,", "\"speed\": 10, \"pierce\": true,", "unknown field")]
        [InlineData(Map, "\"oneWay\": true }", "\"oneWay\": true, \"note\": 1 }", "unknown field")]
        public void UnknownFieldIsRejected(string file, string find, string replace, string problem)
        {
            Assert.Contains(problem, Rejected(file, find, replace).Message);
        }

        [Theory]
        [InlineData("", "unexpected end of file")]
        [InlineData("[]", "must be an object")]
        [InlineData("{ \"version\": 1, }", "expected a field name")]
        [InlineData("{ \"version\": 1 } x", "unexpected text")]
        [InlineData("{ \"version\": 01 }", "expected ','")]
        [InlineData("{ \"version\": tru }", "expected a value")]
        [InlineData("{ \"version\": 1, \"version\": 1 }", "appears twice")]
        [InlineData("{ \"id\": \"a", "not closed")]
        [InlineData("{ \"id\": \"\\q\" }", "unknown escape")]
        [InlineData("{ \"id\": \"\\u12\" }", "four hex digits")]
        [InlineData("{ \"version\": 1e999 }", "out of range")]
        public void BrokenJsonIsRejected(string text, string problem)
        {
            Assert.Contains(problem, RejectedText(Character, text).Message);
        }

        [Fact]
        public void ByteOrderMarkAndEscapesAreAccepted()
        {
            Dictionary<string, string> files = TestConfig.Files();
            files[Character] = "\uFEFF" + files[Character].Replace("GDD 18.1, 18.2 va 19.4", "GDD \\u0041\\n");

            Assert.Equal("GDD A\n", GameConfig.Parse(files).Character.Source);
        }
    }
}
