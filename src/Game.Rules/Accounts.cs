using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Rules
{
    /// <summary>The account numbers of <see cref="GameConfig.AccountsFile"/>. Both the server and the login screens use them.</summary>
    public sealed class AccountConfig : ConfigEntry
    {
        public int UsernameMinLength { get; set; }
        public int UsernameMaxLength { get; set; }
        public int PasswordMinLength { get; set; }
        public int PasswordMaxLength { get; set; }
        public int CharacterNameMinLength { get; set; }
        public int CharacterNameMaxLength { get; set; }

        /// <summary>Guest accounts are named this plus digits; nobody else may take a name starting with it.</summary>
        public string GuestPrefix { get; set; } = string.Empty;

        public int GuestDigits { get; set; }
        public int CodeDigits { get; set; }
        public double CodeLifetimeSec { get; set; }
        public int CodeMaxAttempts { get; set; }
        public double CodeResendSec { get; set; }
        public double SessionLifetimeDays { get; set; }
        public int LoginMaxFailures { get; set; }
        public double LoginLockSec { get; set; }

        /// <summary>Failed logins from one address, over any accounts, before it waits <see cref="LoginLockSec"/> too.</summary>
        public int AddressMaxFailures { get; set; }
        public IReadOnlyList<string> BannedWords { get; set; } = Array.Empty<string>();
    }

    /// <summary>Why a name, password or contact was refused. The screens turn each into Vietnamese text.</summary>
    public enum AccountProblem
    {
        None,
        UsernameLength,
        UsernameCharacters,
        UsernameReserved,
        PasswordLength,
        PasswordSpaces,
        CharacterNameLength,
        CharacterNameCharacters,
        CharacterNameBanned,
        ContactInvalid,
    }

    /// <summary>Whether a contact is an email address or a Vietnamese phone number.</summary>
    public enum ContactKind
    {
        Email,
        Phone,
    }

    /// <summary>A contact in the one form the server stores: email in lower case, phone as +84 and nine digits.</summary>
    public readonly struct Contact
    {
        public Contact(ContactKind kind, string value)
        {
            Kind = kind;
            Value = value;
        }

        public ContactKind Kind { get; }
        public string Value { get; }

        /// <summary>Most of it hidden, for showing on screen: "ab***@gmail.com", "+84***4567".</summary>
        public string Masked
        {
            get
            {
                if (Kind == ContactKind.Phone)
                {
                    return Value.Substring(0, 3) + "***" + Value.Substring(Value.Length - 4);
                }

                int at = Value.IndexOf('@');
                return Value.Substring(0, Math.Min(2, at)) + "***" + Value.Substring(at);
            }
        }
    }

    public static class AccountRules
    {
        private static readonly Regex UsernamePattern = new Regex("^[a-z0-9_]+$");
        private static readonly Regex EmailPattern = new Regex(@"^[a-z0-9._%+\-]+@[a-z0-9\-]+(\.[a-z0-9\-]+)*\.[a-z]{2,}$");
        private static readonly Regex PhonePattern = new Regex(@"^(?:\+?84|0)([35789][0-9]{8})$");

        /// <summary>Usernames are compared in lower case with spaces around them dropped.</summary>
        public static string NormalizeUsername(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

        public static AccountProblem CheckUsername(string? username, AccountConfig config, bool guest = false)
        {
            string name = NormalizeUsername(username);

            if (name.Length < config.UsernameMinLength || name.Length > config.UsernameMaxLength)
            {
                return AccountProblem.UsernameLength;
            }

            if (!UsernamePattern.IsMatch(name))
            {
                return AccountProblem.UsernameCharacters;
            }

            return !guest && name.StartsWith(config.GuestPrefix, StringComparison.Ordinal)
                ? AccountProblem.UsernameReserved
                : AccountProblem.None;
        }

        public static AccountProblem CheckPassword(string? password, AccountConfig config)
        {
            string text = password ?? string.Empty;

            if (text.Length < config.PasswordMinLength || text.Length > config.PasswordMaxLength)
            {
                return AccountProblem.PasswordLength;
            }

            return text.Any(char.IsWhiteSpace) ? AccountProblem.PasswordSpaces : AccountProblem.None;
        }

        /// <summary>The name as stored: trimmed and in Unicode composed form, so "Thủy" typed two ways is one name.</summary>
        public static string NormalizeCharacterName(string? name) => (name ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);

        /// <summary>Letters, Vietnamese marks included, and digits; no spaces or symbols. Some words are not allowed anywhere in it.</summary>
        public static AccountProblem CheckCharacterName(string? name, AccountConfig config)
        {
            string text = NormalizeCharacterName(name);
            int length = new StringInfo(text).LengthInTextElements;

            if (length < config.CharacterNameMinLength || length > config.CharacterNameMaxLength)
            {
                return AccountProblem.CharacterNameLength;
            }

            if (!text.All(character => char.IsLetterOrDigit(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark))
            {
                return AccountProblem.CharacterNameCharacters;
            }

            return ContainsBannedWord(text, config) ? AccountProblem.CharacterNameBanned : AccountProblem.None;
        }

        /// <summary>
        /// A word with marks must appear with the same marks; a word without marks is matched against
        /// the name with its marks taken off, so "Ádmin" is caught but "Đụng" is not mistaken for "đụ".
        /// </summary>
        public static bool ContainsBannedWord(string name, AccountConfig config)
        {
            string lower = name.ToLowerInvariant();
            string bare = WithoutMarks(lower);

            foreach (string word in config.BannedWords)
            {
                string banned = word.ToLowerInvariant().Normalize(NormalizationForm.FormC);
                bool hasMarks = WithoutMarks(banned) != banned;

                if ((hasMarks ? lower : bare).Contains(banned))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads an email address or a Vietnamese mobile number; null when it is neither.</summary>
        public static Contact? ParseContact(string? text)
        {
            string value = (text ?? string.Empty).Trim().ToLowerInvariant();

            if (EmailPattern.IsMatch(value) && value.Length <= 254)
            {
                return new Contact(ContactKind.Email, value);
            }

            string digits = new string(value.Where(character => character != ' ' && character != '.' && character != '-').ToArray());
            Match phone = PhonePattern.Match(digits);
            return phone.Success ? new Contact(ContactKind.Phone, "+84" + phone.Groups[1].Value) : (Contact?)null;
        }

        /// <summary>What the screens say for each problem; the server sends the same text with its refusals.</summary>
        public static string Message(AccountProblem problem, AccountConfig config)
        {
            switch (problem)
            {
                case AccountProblem.UsernameLength:
                    return $"Tài khoản dài từ {config.UsernameMinLength} đến {config.UsernameMaxLength} ký tự.";
                case AccountProblem.UsernameCharacters:
                    return "Tài khoản chỉ gồm chữ thường không dấu, số và dấu gạch dưới.";
                case AccountProblem.UsernameReserved:
                    return $"Tài khoản không được bắt đầu bằng \"{config.GuestPrefix}\".";
                case AccountProblem.PasswordLength:
                    return $"Mật khẩu dài từ {config.PasswordMinLength} đến {config.PasswordMaxLength} ký tự.";
                case AccountProblem.PasswordSpaces:
                    return "Mật khẩu không được có khoảng trắng.";
                case AccountProblem.CharacterNameLength:
                    return $"Tên nhân vật dài từ {config.CharacterNameMinLength} đến {config.CharacterNameMaxLength} ký tự.";
                case AccountProblem.CharacterNameCharacters:
                    return "Tên nhân vật chỉ gồm chữ và số, không có khoảng trắng hay ký hiệu.";
                case AccountProblem.CharacterNameBanned:
                    return "Tên nhân vật có từ không được phép.";
                case AccountProblem.ContactInvalid:
                    return "Nhập email hoặc số điện thoại Việt Nam.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>Takes the Vietnamese marks off, and turns đ into d.</summary>
        public static string WithoutMarks(string text)
        {
            var bare = new StringBuilder(text.Length);

            foreach (char character in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    bare.Append(character == 'đ' ? 'd' : character == 'Đ' ? 'D' : character);
                }
            }

            return bare.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
