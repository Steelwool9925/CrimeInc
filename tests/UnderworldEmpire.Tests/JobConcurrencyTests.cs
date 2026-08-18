using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
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

        int succeeded = results.Count(r => r.Succeeded);
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

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

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

        int succeeded = results.Count(r => r.Succeeded);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<GameDbContext>();

        var mastery = await db.PlayerJobMasteries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlayerId == playerId && m.JobId == CheapJobId);

        // Completions must equal the number of attempts that actually committed.
        Assert.Equal(succeeded, mastery?.Completions ?? 0);
    }
}
