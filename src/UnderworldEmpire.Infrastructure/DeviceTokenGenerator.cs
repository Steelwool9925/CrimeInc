using System.Security.Cryptography;

namespace UnderworldEmpire.Infrastructure;

/// <summary>
/// Generates cryptographically-random guest bearer tokens.
/// 32 random bytes encoded as Base64Url (no padding) yields a 43-character string
/// that fits within the 64-character column limit on <c>Player.DeviceToken</c>.
/// The server always generates tokens; clients never choose them.
/// </summary>
public static class DeviceTokenGenerator
{
    /// <summary>Generates a new, unique device token.</summary>
    public static string Generate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        // Base64Url without padding: replace + with -, / with _, strip =
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
