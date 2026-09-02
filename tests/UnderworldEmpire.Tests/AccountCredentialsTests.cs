using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="AccountCredentials"/> — pure shape validation for usernames and
/// passwords. No I/O, no crypto, no database: hashing and persistence live in Infrastructure.
/// </summary>
public class AccountCredentialsTests
{
    // -------------------------------------------------------------------------
    // IsValidUsername
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("abc")] // 3 chars — lower boundary
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef")] // 32 chars — upper boundary
    [InlineData("a1_2-3")] // digits, underscore and hyphen mixed in
    public void Valid_usernames_are_accepted(string username)
    {
        Assert.True(AccountCredentials.IsValidUsername(username));
    }

    [Theory]
    [InlineData("ab")] // 2 chars — one below the minimum
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")] // 33 chars — one above the maximum
    [InlineData("")]
    [InlineData("Abcdef")] // uppercase — caller's job to lower-case before calling
    [InlineData("abc def")] // contains a space
    public void Invalid_usernames_are_rejected(string username)
    {
        Assert.False(AccountCredentials.IsValidUsername(username));
    }

    [Fact]
    public void A_null_username_is_rejected()
    {
        Assert.False(AccountCredentials.IsValidUsername(null!));
    }

    // -------------------------------------------------------------------------
    // IsValidPassword
    // -------------------------------------------------------------------------

    [Fact]
    public void A_password_of_exactly_the_minimum_length_is_valid()
    {
        Assert.True(AccountCredentials.IsValidPassword("12345678"));
    }

    [Fact]
    public void A_password_one_below_the_minimum_length_is_rejected()
    {
        Assert.False(AccountCredentials.IsValidPassword("1234567"));
    }

    [Fact]
    public void An_empty_password_is_rejected()
    {
        Assert.False(AccountCredentials.IsValidPassword(""));
    }

    [Fact]
    public void A_null_password_is_rejected()
    {
        Assert.False(AccountCredentials.IsValidPassword(null!));
    }

    [Fact]
    public void A_long_password_is_valid()
    {
        // No upper bound to test — length alone never disqualifies a long password.
        Assert.True(AccountCredentials.IsValidPassword(new string('x', 128)));
    }
}
