using System;
using System.IO;
using Xunit;

namespace Game.Rules.Tests
{
    public class AccountRulesTests
    {
        private static string Text() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", GameConfig.AccountsFile));

        private readonly AccountConfig config = GameConfig.ParseAccounts(Text());

        [Fact]
        public void FileHoldsTheApprovedRules()
        {
            Assert.Equal(ConfigStatus.Thu, config.Status);
            Assert.Equal((6, 20), (config.UsernameMinLength, config.UsernameMaxLength));
            Assert.Equal((6, 32), (config.PasswordMinLength, config.PasswordMaxLength));
            Assert.Equal((3, 12), (config.CharacterNameMinLength, config.CharacterNameMaxLength));
            Assert.Equal("khach_", config.GuestPrefix);
        }

        [Theory]
        [InlineData("kiemsi01", AccountProblem.None)]
        [InlineData("  KiemSi_01  ", AccountProblem.None)]
        [InlineData("abc12", AccountProblem.UsernameLength)]
        [InlineData("abcdefghijklmnopqrstu", AccountProblem.UsernameLength)]
        [InlineData("kiếm_sĩ", AccountProblem.UsernameCharacters)]
        [InlineData("kiem si", AccountProblem.UsernameCharacters)]
        [InlineData("kiem-si", AccountProblem.UsernameCharacters)]
        [InlineData("khach_123456", AccountProblem.UsernameReserved)]
        public void UsernamesFollowTheRules(string username, AccountProblem expected)
        {
            Assert.Equal(expected, AccountRules.CheckUsername(username, config));
        }

        [Fact]
        public void GuestNamesAreAllowedOnlyForGuests()
        {
            Assert.Equal(AccountProblem.None, AccountRules.CheckUsername("khach_123456", config, guest: true));
            Assert.Equal("kiemsi_01", AccountRules.NormalizeUsername("  KiemSi_01 "));
        }

        [Theory]
        [InlineData("abc123", AccountProblem.None)]
        [InlineData("Mật!khẩu@2026", AccountProblem.None)]
        [InlineData("12345", AccountProblem.PasswordLength)]
        [InlineData("123456789012345678901234567890123", AccountProblem.PasswordLength)]
        [InlineData("abc 123", AccountProblem.PasswordSpaces)]
        [InlineData(null, AccountProblem.PasswordLength)]
        public void PasswordsFollowTheRules(string? password, AccountProblem expected)
        {
            Assert.Equal(expected, AccountRules.CheckPassword(password, config));
        }

        [Theory]
        [InlineData("Thủy", AccountProblem.None)]
        [InlineData("Kiếm Sĩ", AccountProblem.CharacterNameCharacters)]
        [InlineData("Đụng", AccountProblem.None)]
        [InlineData("Long99", AccountProblem.None)]
        [InlineData("Dũng", AccountProblem.None)]
        [InlineData("Ab", AccountProblem.CharacterNameLength)]
        [InlineData("Nguyễnvănthủy", AccountProblem.CharacterNameLength)]
        [InlineData("Ng@", AccountProblem.CharacterNameCharacters)]
        [InlineData("Ádmin1", AccountProblem.CharacterNameBanned)]
        [InlineData("ADMIN", AccountProblem.CharacterNameBanned)]
        [InlineData("Lồnxx", AccountProblem.CharacterNameBanned)]
        public void CharacterNamesFollowTheRules(string name, AccountProblem expected)
        {
            Assert.Equal(expected, AccountRules.CheckCharacterName(name, config));
        }

        [Fact]
        public void ANameTypedWithSeparateMarksCountsByLetters()
        {
            string decomposed = "Thủy";

            Assert.Equal("Thủy", AccountRules.NormalizeCharacterName(decomposed));
            Assert.Equal(AccountProblem.None, AccountRules.CheckCharacterName(decomposed, config));
            Assert.Equal(AccountProblem.None, AccountRules.CheckCharacterName("Nguyễnvăn12", config));
        }

        [Theory]
        [InlineData("Ab@Gmail.com ", ContactKind.Email, "ab@gmail.com", "ab***@gmail.com")]
        [InlineData("0912 345 678", ContactKind.Phone, "+84912345678", "+84***5678")]
        [InlineData("+84912345678", ContactKind.Phone, "+84912345678", "+84***5678")]
        [InlineData("84.912.345.678", ContactKind.Phone, "+84912345678", "+84***5678")]
        public void ContactsAreReadIntoOneForm(string text, ContactKind kind, string value, string masked)
        {
            Contact contact = AccountRules.ParseContact(text)!.Value;

            Assert.Equal(kind, contact.Kind);
            Assert.Equal(value, contact.Value);
            Assert.Equal(masked, contact.Masked);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("a@b")]
        [InlineData("0212345678")]
        [InlineData("091234567")]
        [InlineData("09123456789")]
        public void OtherContactsAreRefused(string text)
        {
            Assert.Null(AccountRules.ParseContact(text));
        }

        [Fact]
        public void MarksComeOffForComparing()
        {
            Assert.Equal("Dung Thuy", AccountRules.WithoutMarks("Dũng Thủy"));
            Assert.Equal("dau", AccountRules.WithoutMarks("đậu"));
        }

        [Theory]
        [InlineData("\"usernameMaxLength\": 20", "\"usernameMaxLength\": 5", "usernameMaxLength")]
        [InlineData("\"guestPrefix\": \"khach_\"", "\"guestPrefix\": \"khách_\"", "guestPrefix")]
        [InlineData("\"codeDigits\": 6", "\"codeDigits\": 12", "codeDigits")]
        [InlineData("\"loginLockSec\": 300", "\"loginLockSec\": 300, \"extra\": 1", "extra")]
        public void BadFilesAreRejected(string find, string replace, string field)
        {
            string text = Text();
            Assert.Contains(find, text);

            InvalidConfigException error = Assert.Throws<InvalidConfigException>(() => GameConfig.ParseAccounts(text.Replace(find, replace)));

            Assert.Equal(GameConfig.AccountsFile, error.File);
            Assert.Contains(field, error.FieldPath);
        }
    }
}
