using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for the economy domain methods on <see cref="Player"/> (T1.3).
/// No database — exercises domain logic directly.
/// </summary>
public class EconomyTests
{
    // -------------------------------------------------------------------------
    // Starting defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void New_player_cash_equals_starting_default()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Equal(PlayerDefaults.StartingCash, player.Cash);
    }

    [Fact]
    public void New_player_premium_currency_equals_starting_default()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Equal(PlayerDefaults.StartingPremiumCurrency, player.PremiumCurrency);
    }

    [Fact]
    public void PlayerDefaults_starting_cash_is_zero()
    {
        // A new player owns nothing and must work the first job before they can buy anything.
        Assert.Equal(0L, PlayerDefaults.StartingCash);
    }

    [Fact]
    public void PlayerDefaults_starting_premium_currency_is_5()
    {
        Assert.Equal(5L, PlayerDefaults.StartingPremiumCurrency);
    }

    // -------------------------------------------------------------------------
    // Cash — AddCash
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCash_increases_cash_by_amount()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        long before = player.Cash;
        player.AddCash(50);
        Assert.Equal(before + 50, player.Cash);
    }

    [Fact]
    public void AddCash_stops_at_the_ceiling_instead_of_wrapping_negative()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow, Cash = long.MaxValue - 5 };

        player.AddCash(20);

        Assert.Equal(long.MaxValue, player.Cash);
    }

    [Fact]
    public void AddXp_stops_at_the_ceiling_instead_of_wrapping_negative()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow, Xp = long.MaxValue - 5 };

        player.AddXp(20);

        Assert.Equal(long.MaxValue, player.Xp);
    }

    [Fact]
    public void AddCash_exactly_reaching_the_ceiling_is_not_clamped_early()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow, Cash = long.MaxValue - 5 };

        player.AddCash(5);

        Assert.Equal(long.MaxValue, player.Cash);
    }

    [Fact]
    public void AddCash_negative_amount_throws_ArgumentOutOfRangeException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Throws<ArgumentOutOfRangeException>(() => player.AddCash(-1));
    }

    [Fact]
    public void AddCash_zero_amount_does_not_change_cash()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        long before = player.Cash;
        player.AddCash(0);
        Assert.Equal(before, player.Cash);
    }

    // -------------------------------------------------------------------------
    // Cash — SpendCash
    // -------------------------------------------------------------------------

    [Fact]
    public void SpendCash_decreases_cash_by_amount()
    {
        // Funded explicitly: a new player now starts at zero, so there is nothing to spend until
        // they have earned some.
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        player.AddCash(100);
        long before = player.Cash;
        player.SpendCash(30);
        Assert.Equal(before - 30, player.Cash);
    }

    [Fact]
    public void SpendCash_exact_balance_leaves_zero()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        player.SpendCash(player.Cash);
        Assert.Equal(0L, player.Cash);
    }

    [Fact]
    public void SpendCash_more_than_balance_throws_InvalidOperationException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        var ex = Assert.Throws<InvalidOperationException>(() => player.SpendCash(player.Cash + 1));
        Assert.Contains("insufficient cash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpendCash_negative_amount_throws_ArgumentOutOfRangeException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SpendCash(-1));
    }

    // -------------------------------------------------------------------------
    // PremiumCurrency — AddPremiumCurrency
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPremiumCurrency_increases_premium_currency_by_amount()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        long before = player.PremiumCurrency;
        player.AddPremiumCurrency(10);
        Assert.Equal(before + 10, player.PremiumCurrency);
    }

    [Fact]
    public void AddPremiumCurrency_negative_amount_throws_ArgumentOutOfRangeException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Throws<ArgumentOutOfRangeException>(() => player.AddPremiumCurrency(-1));
    }

    [Fact]
    public void AddPremiumCurrency_zero_amount_does_not_change_premium_currency()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        long before = player.PremiumCurrency;
        player.AddPremiumCurrency(0);
        Assert.Equal(before, player.PremiumCurrency);
    }

    // -------------------------------------------------------------------------
    // PremiumCurrency — SpendPremiumCurrency
    // -------------------------------------------------------------------------

    [Fact]
    public void SpendPremiumCurrency_decreases_premium_currency_by_amount()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        long before = player.PremiumCurrency;
        player.SpendPremiumCurrency(3);
        Assert.Equal(before - 3, player.PremiumCurrency);
    }

    [Fact]
    public void SpendPremiumCurrency_exact_balance_leaves_zero()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        player.SpendPremiumCurrency(player.PremiumCurrency);
        Assert.Equal(0L, player.PremiumCurrency);
    }

    [Fact]
    public void SpendPremiumCurrency_more_than_balance_throws_InvalidOperationException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        var ex = Assert.Throws<InvalidOperationException>(() => player.SpendPremiumCurrency(player.PremiumCurrency + 1));
        Assert.Contains("insufficient premium currency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpendPremiumCurrency_negative_amount_throws_ArgumentOutOfRangeException()
    {
        var player = new Player { CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SpendPremiumCurrency(-1));
    }
}
