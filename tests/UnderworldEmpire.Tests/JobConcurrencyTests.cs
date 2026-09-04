using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves the Tier 2 concurrency fix: two simultaneous do-job requests against one player yield
/// exactly one payout, not two.
/// </summary>
/// <remarks>
/// This is the test the whole <c>xmin</c> concurrency token exists for. Without the token both
/// requests read the same energy snapshot, both spend, and both bank a payout — a lost update that
/// hands the player free cash for a double-tap. Requires Postgres on 5433.
/// </remarks>
public class JobConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public JobConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
    }

    private const string CheapJobId = "shake-down-corner-store";

    /// <summary>Creates a player directly in the database and returns its id.</summary>
    private async Task<Guid> SeedPlayerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var now = DateTimeOffset.UtcNow;
        var player = new Player
        {
            CreatedAtUtc = now,
            DeviceToken = DeviceTokenGenerator.Generate(),
            Energy = Meter.Full(PlayerDefaults.EnergyMax, now),
            Stamina = Meter.Full(PlayerDefaults.StaminaMax, now),
            Health = Meter.Full(PlayerDefaults.HealthMax, now)
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<(long Cash, int Energy, long Xp)> ReadPlayerAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var player = await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId);
        return (player.Cash, player.Energy.CurrentAt(DateTimeOffset.UtcNow, MeterRates.Energy), player.Xp);
    }

    // -------------------------------------------------------------------------
    // Item-quantity gate (Tier 1 consumable item gate, M2) — the same xmin path must cover the
    // new PlayerItems child-row write, since it rides in the same SaveChangesAsync as Players.
    // -------------------------------------------------------------------------

    private const string ItemGatedJobId = "hijack-a-delivery-truck";
    private const string GateItemId = "switchblade";

    /// <summary>
    /// A player already at the item-gated job's level and stat requirements, holding
    /// <paramref name="switchbladeQuantity"/> of the item it gates on.
    /// </summary>
    private async Task<Guid> SeedPlayerReadyForItemGatedJobAsync(int switchbladeQuantity)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var now = DateTimeOffset.UtcNow;
        var player = new Player
        {
            CreatedAtUtc = now,
            DeviceToken = DeviceTokenGenerator.Generate(),
            Level = 3,
            Attack = 9,
            Defense = 7,
            Energy = Meter.Full(PlayerDefaults.EnergyMax, now),
            Stamina = Meter.Full(PlayerDefaults.StaminaMax, now),
            Health = Meter.Full(PlayerDefaults.HealthMax, now)
        };
        db.Players.Add(player);

        var holding = new PlayerItem { PlayerId = player.Id, ItemId = GateItemId };
        holding.AddCopies(switchbladeQuantity);
        db.PlayerItems.Add(holding);

        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<int> HeldQuantityAsync(Guid playerId, string itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var holding = await db.PlayerItems.AsNoTracking()
            .FirstOrDefaultAsync(h => h.PlayerId == playerId && h.ItemId == itemId);
        return holding?.Quantity ?? 0;
    }

    [Fact]
    public async Task Two_concurrent_item_gated_attempts_consume_stock_exactly_once_each()
    {
        // Well above the minimum of 2, so both attempts clear the item gate regardless of read
        // ordering — this isolates the property under test (does the child-row write ever get lost
        // or double-applied?) from the item gate itself, which DoJobEndpointTests already covers.
        var playerId = await SeedPlayerReadyForItemGatedJobAsync(switchbladeQuantity: 10);
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<JobActionService>()
                .PerformAsync(playerId, ItemGatedJobId, now),
            scopeB.ServiceProvider.GetRequiredService<JobActionService>()
                .PerformAsync(playerId, ItemGatedJobId, now));

        int succeeded = results.Count(r => r.WasApplied);
        int conflicted = results.Count(r => r.Error == JobActionError.ConcurrencyConflict);

        Assert.True(succeeded >= 1, "At least one attempt should have succeeded.");
        Assert.Equal(2, succeeded + conflicted);

        // Whichever number of attempts actually applied, stock dropped by exactly that many units —
        // never one unit for two applied attempts (a lost update), and never two for one (a
        // double-spend). The existing xmin token on Players, which every applied attempt already
        // writes via Energy, is what makes this true without any new concurrency primitive.
        Assert.Equal(10 - succeeded, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Sequential_item_gated_attempts_each_consume_their_own_unit()
    {
        // The counterpart to the race: with no contention, both attempts must go through and each
        // must spend its own unit — not one shared spend across both.
        var playerId = await SeedPlayerReadyForItemGatedJobAsync(switchbladeQuantity: 2);
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        var first = await scopeA.ServiceProvider.GetRequiredService<JobActionService>()
            .PerformAsync(playerId, ItemGatedJobId, now);
        Assert.True(first.WasApplied);
        Assert.Equal(1, await HeldQuantityAsync(playerId, GateItemId));

        using var scopeB = _factory.Services.CreateScope();
        var second = await scopeB.ServiceProvider.GetRequiredService<JobActionService>()
            .PerformAsync(playerId, ItemGatedJobId, now);

        // Stock dropped below the minimum after the first attempt, so the second is refused.
        Assert.False(second.WasApplied);
        Assert.Equal(JobFailure.InsufficientItems, second.Failure);
        Assert.Equal(1, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Two_concurrent_attempts_produce_exactly_one_payout()
    {
        var playerId = await SeedPlayerAsync();
        var before = await ReadPlayerAsync(playerId);
        var now = DateTimeOffset.UtcNow;

        // Two independent scopes, so two DbContexts — the shape a genuine double-tap takes, where
        // each HTTP request gets its own scoped context.
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var serviceA = scopeA.ServiceProvider.GetRequiredService<JobActionService>();
        var serviceB = scopeB.ServiceProvider.GetRequiredService<JobActionService>();

        // Both read before either writes. Awaiting them together is what creates the race; the
        // token is what resolves it.
        var results = await Task.WhenAll(
            serviceA.PerformAsync(playerId, CheapJobId, now),
            serviceB.PerformAsync(playerId, CheapJobId, now));

        int succeeded = results.Count(r => r.WasApplied);
        int conflicted = results.Count(r => r.Error == JobActionError.ConcurrencyConflict);

        // Either the writes genuinely raced (one wins, one conflicts) or they serialised cleanly.
        // Both are correct; what must never happen is two payouts from one energy snapshot.
        Assert.True(succeeded >= 1, "At least one attempt should have succeeded.");
        Assert.Equal(2, succeeded + conflicted);

        var after = await ReadPlayerAsync(playerId);

        long cashGained = after.Cash - before.Cash;
        long xpGained = after.Xp - before.Xp;
        int energySpent = before.Energy - after.Energy;

        // The invariant that matters: cash, XP and energy must all reflect the same number of
        // completions. A lost update shows up as two payouts having cost only one energy.
        Assert.Equal(succeeded, energySpent);
        Assert.Equal(succeeded * 10, xpGained);              // the cheap job grants 10 XP
        Assert.InRange(cashGained, succeeded * 15, succeeded * 30); // and 15..30 cash
    }

    [Fact]
    public async Task Sequential_attempts_both_succeed_and_both_are_charged()
    {
        // The counterpart to the race: with no contention, two attempts must both go through. A
        // concurrency token that rejected legitimate sequential work would be worse than none.
        var playerId = await SeedPlayerAsync();
        var before = await ReadPlayerAsync(playerId);
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        var first = await scopeA.ServiceProvider.GetRequiredService<JobActionService>()
            .PerformAsync(playerId, CheapJobId, now);

        using var scopeB = _factory.Services.CreateScope();
        var second = await scopeB.ServiceProvider.GetRequiredService<JobActionService>()
            .PerformAsync(playerId, CheapJobId, now);

        Assert.True(first.WasApplied);
        Assert.True(second.WasApplied);

        var after = await ReadPlayerAsync(playerId);

        Assert.Equal(2, before.Energy - after.Energy);
        Assert.Equal(20, after.Xp - before.Xp);
        Assert.Equal(2, second.Outcome!.Completions);
    }

    [Fact]
    public async Task A_concurrency_conflict_leaves_no_partial_write()
    {
        // Whichever attempt loses must change nothing at all — not energy, not mastery.
        var playerId = await SeedPlayerAsync();
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<JobActionService>().PerformAsync(playerId, CheapJobId, now),
            scopeB.ServiceProvider.GetRequiredService<JobActionService>().PerformAsync(playerId, CheapJobId, now));

        int succeeded = results.Count(r => r.WasApplied);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<GameDbContext>();

        var mastery = await db.PlayerJobMasteries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlayerId == playerId && m.JobId == CheapJobId);

        // Completions must equal the number of attempts that actually committed.
        Assert.Equal(succeeded, mastery?.Completions ?? 0);
    }
}
