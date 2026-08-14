using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Infrastructure;

/// <summary>
/// Thin application service for player currency operations. Delegates guard
/// logic to the domain methods on <see cref="Player"/>; EF SaveChanges acts as
/// the implicit transaction boundary.
/// </summary>
public class EconomyService(GameDbContext db)
{
    /// <summary>Adds <paramref name="amount"/> to the player's <see cref="Player.Cash"/> balance.</summary>
    /// <returns>The updated cash balance.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no player with <paramref name="playerId"/> exists.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Propagated from <see cref="Player.AddCash"/>.</exception>
    public async Task<long> AddCashAsync(Guid playerId, long amount, CancellationToken cancellationToken = default)
    {
        var player = await db.Players.FindAsync([playerId], cancellationToken)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");
        player.AddCash(amount);
        await db.SaveChangesAsync(cancellationToken);
        return player.Cash;
    }

    /// <summary>Spends <paramref name="amount"/> from the player's <see cref="Player.Cash"/> balance.</summary>
    /// <returns>The updated cash balance.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no player with <paramref name="playerId"/> exists.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Propagated from <see cref="Player.SpendCash"/>.</exception>
    /// <exception cref="InvalidOperationException">Propagated from <see cref="Player.SpendCash"/> when balance is insufficient.</exception>
    public async Task<long> SpendCashAsync(Guid playerId, long amount, CancellationToken cancellationToken = default)
    {
        var player = await db.Players.FindAsync([playerId], cancellationToken)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");
        player.SpendCash(amount);
        await db.SaveChangesAsync(cancellationToken);
        return player.Cash;
    }

    /// <summary>Adds <paramref name="amount"/> to the player's <see cref="Player.PremiumCurrency"/> balance.</summary>
    /// <returns>The updated premium currency balance.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no player with <paramref name="playerId"/> exists.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Propagated from <see cref="Player.AddPremiumCurrency"/>.</exception>
    public async Task<long> AddPremiumCurrencyAsync(Guid playerId, long amount, CancellationToken cancellationToken = default)
    {
        var player = await db.Players.FindAsync([playerId], cancellationToken)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");
        player.AddPremiumCurrency(amount);
        await db.SaveChangesAsync(cancellationToken);
        return player.PremiumCurrency;
    }

    /// <summary>Spends <paramref name="amount"/> from the player's <see cref="Player.PremiumCurrency"/> balance.</summary>
    /// <returns>The updated premium currency balance.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no player with <paramref name="playerId"/> exists.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Propagated from <see cref="Player.SpendPremiumCurrency"/>.</exception>
    /// <exception cref="InvalidOperationException">Propagated from <see cref="Player.SpendPremiumCurrency"/> when balance is insufficient.</exception>
    public async Task<long> SpendPremiumCurrencyAsync(Guid playerId, long amount, CancellationToken cancellationToken = default)
    {
        var player = await db.Players.FindAsync([playerId], cancellationToken)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");
        player.SpendPremiumCurrency(amount);
        await db.SaveChangesAsync(cancellationToken);
        return player.PremiumCurrency;
    }
}
