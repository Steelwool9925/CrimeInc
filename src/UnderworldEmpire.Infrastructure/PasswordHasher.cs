using System.Security.Cryptography;

namespace UnderworldEmpire.Infrastructure;

/// <summary>
/// Salted password hashing via PBKDF2-HMACSHA256. Mirrors <see cref="DeviceTokenGenerator"/>'s
/// precedent: a crypto-touching utility lives here, not in Domain, which stays dependency-free.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Hashes <paramref name="password"/> with a freshly-generated random salt. The returned
    /// string self-encodes the iteration count and salt alongside the hash, so
    /// <see cref="Verify"/> needs nothing but this one string to check a later attempt against —
    /// no separate salt column, and a future iteration-count change doesn't invalidate hashes
    /// created under the old count.
    /// </summary>
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSizeBytes);

        return string.Join(
            '.',
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Whether <paramref name="password"/> matches <paramref name="hash"/> (a string produced by
    /// <see cref="Hash"/>). Uses a constant-time comparison so response timing cannot leak how
    /// many bytes of the hash matched.
    /// </summary>
    public static bool Verify(string password, string hash)
    {
        string[] parts = hash.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expected = Convert.FromBase64String(parts[2]);

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
