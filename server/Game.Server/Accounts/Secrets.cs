using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Game.Server.Accounts;

/// <summary>
/// Passwords as "pbkdf2-sha256$iterations$salt$hash" with PBKDF2 from .NET itself, a salt of
/// its own for each account, and the iteration count stored so it can be raised later.
/// </summary>
public sealed class PasswordHasher
{
    /// <summary>What OWASP asks for PBKDF2-SHA256 in 2023; tests pass far fewer to stay fast.</summary>
    public const int DefaultIterations = 600_000;

    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly int iterations;

    public PasswordHasher(int iterations = DefaultIterations)
    {
        this.iterations = iterations > 0 ? iterations : throw new ArgumentOutOfRangeException(nameof(iterations));
    }

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>False for a wrong password, and for an account with no password at all, such as a guest.</summary>
    public bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');

        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            // Spend the same time as a real check, so a missing account cannot be told by speed.
            Rfc2898DeriveBytes.Pbkdf2(password, new byte[SaltBytes], iterations, HashAlgorithmName.SHA256, HashBytes);
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, count, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>Random tokens the client keeps, and the hashes the database keeps instead of them.</summary>
public static class Tokens
{
    /// <summary>32 random bytes, URL-safe base64 without padding.</summary>
    public static string New()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>A verification code's hash, tied to what it is for and where it was sent.</summary>
    public static byte[] HashCode(string purpose, string contact, string code) => Hash($"{purpose}\n{contact}\n{code}");

    /// <summary>A code of the given number of digits, leading zeros kept.</summary>
    public static string NewCode(int digits)
    {
        int limit = (int)Math.Pow(10, digits);
        return RandomNumberGenerator.GetInt32(limit).ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }
}
