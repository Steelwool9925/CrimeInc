using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PasswordHasher"/> — the salted PBKDF2 hash used to store and verify
/// account passwords. Mirrors <see cref="JobTests"/>'s style for a pure, dependency-free utility.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        string hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.True(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void The_wrong_password_does_not_verify()
    {
        string hash = PasswordHasher.Hash("correct");

        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_strings()
    {
        // Proves the salt is actually random per call, not fixed — two calls on an identical
        // password must not collide.
        string first = PasswordHasher.Hash("correct-horse-battery-staple");
        string second = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void An_empty_password_does_not_verify_against_a_different_passwords_hash()
    {
        string hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void A_password_differing_by_one_character_does_not_verify()
    {
        string hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.False(PasswordHasher.Verify("correct-horse-battery-staplf", hash));
    }
}
