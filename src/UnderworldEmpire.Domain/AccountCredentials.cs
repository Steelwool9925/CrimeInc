using System.Text.RegularExpressions;

namespace UnderworldEmpire.Domain;

/// <summary>
/// Pure shape validation for account usernames and passwords. No I/O, no crypto — hashing and
/// persistence are Infrastructure's concern; this only answers "is this input shaped correctly."
/// </summary>
public static partial class AccountCredentials
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 32;
    public const int MinPasswordLength = 8;

    [GeneratedRegex("^[a-z0-9_-]+$")]
    private static partial Regex UsernameCharacters();

    /// <summary>
    /// Whether <paramref name="username"/> is a valid, already-lowercased username: 3-32
    /// characters, each one a lowercase letter, digit, underscore or hyphen.
    /// </summary>
    /// <remarks>
    /// Takes the username <b>as the caller intends to store it</b> — callers must lower-case
    /// before calling this, not after, so what this validates is exactly what gets persisted.
    /// Canonicalising here silently would let a caller validate one string and store a different
    /// one.
    /// </remarks>
    public static bool IsValidUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return false;
        if (username.Length is < MinUsernameLength or > MaxUsernameLength)
            return false;

        return UsernameCharacters().IsMatch(username);
    }

    /// <summary>Whether <paramref name="password"/> meets the minimum length requirement.</summary>
    public static bool IsValidPassword(string password) =>
        password is not null && password.Length >= MinPasswordLength;
}
