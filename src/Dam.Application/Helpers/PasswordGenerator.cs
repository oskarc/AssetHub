using System.Security.Cryptography;

namespace Dam.Application.Helpers;

/// <summary>
/// Cryptographically secure password generation.
/// Uses RandomNumberGenerator for entropy and Fisher-Yates shuffle for uniform distribution.
/// </summary>
public static class PasswordGenerator
{
    // Character sets that avoid ambiguous characters (0/O, 1/l/I)
    private const string UpperCase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowerCase = "abcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%&*?";
    private const string AllChars = UpperCase + LowerCase + Digits + Special;

    /// <summary>
    /// Generates a cryptographically secure password guaranteed to contain
    /// at least one uppercase, lowercase, digit, and special character.
    /// </summary>
    /// <param name="length">Password length (minimum 8).</param>
    public static string Generate(int length = 16)
    {
        if (length < 8)
            throw new ArgumentOutOfRangeException(nameof(length), "Password length must be at least 8");

        var password = new char[length];
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);

        // Guarantee at least one of each category
        password[0] = UpperCase[bytes[0] % UpperCase.Length];
        password[1] = LowerCase[bytes[1] % LowerCase.Length];
        password[2] = Digits[bytes[2] % Digits.Length];
        password[3] = Special[bytes[3] % Special.Length];

        // Fill the rest randomly from all characters
        for (int i = 4; i < length; i++)
        {
            password[i] = AllChars[bytes[i] % AllChars.Length];
        }

        // Fisher-Yates shuffle for uniform distribution
        var shuffleBytes = new byte[length];
        RandomNumberGenerator.Fill(shuffleBytes);
        for (int i = length - 1; i > 0; i--)
        {
            int j = shuffleBytes[i] % (i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
