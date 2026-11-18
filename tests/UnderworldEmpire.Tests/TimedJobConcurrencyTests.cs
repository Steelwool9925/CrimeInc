using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Timed;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves the timed-job concurrency guarantees: two simultaneous starts against one player's one
/// slot never both succeed, and two simultaneous collects of the same running row never both pay
/// out. Mirrors <see cref="JobConcurrencyTests"/> — the existing <c>Players</c> <c>xmin</c> token
/// is what makes both true, with no dedicated token on <c>PlayerTimedJob</c> itself.
/// </summary>
/// <remarks>Requires Postgres on 5433.</remarks>
public class TimedJobConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TimedJobConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
    }

    private const string CheapTimedJobId = "run-a-late-night-shipment";

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

    private async Task<int> RunningRowCountAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return await db.PlayerTimedJobs.AsNoTracking().CountAsync(t => t.PlayerId == playerId);
    }

    private async Task BackdateRunningJobAsync(Guid playerId, TimeSpan backBy)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var row = await db.PlayerTimedJobs.FirstAsync(t => t.PlayerId == playerId);
        row.StartedAtUtc -= backBy;
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Double-start — the one-slot race
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_starts_never_both_occupy_the_slot()
    {
        var playerId = await SeedPlayerAsync();
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<TimedJobActionService>()
                .StartAsync(playerId, CheapTimedJobId, now),
            scopeB.ServiceProvider.GetRequiredService<TimedJobActionService>()
                .StartAsync(playerId, CheapTimedJobId, now));

        int succeeded = results.Count(r => r.WasApplied);
        int conflicted = results.Count(r => r.Error == TimedJobActionError.ConcurrencyConflict);

        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        // Exactly one row, and exactly one energy spend — never two of either.
        Assert.Equal(1, await RunningRowCountAsync(playerId));
        var after = await ReadPlayerAsync(playerId);
        Assert.Equal(PlayerDefaults.EnergyMax - 3, after.Energy);
    }

    [Fact]
    public async Task Sequential_starts_the_second_is_refused_once_the_first_is_running()
    {
        var playerId = await SeedPlayerAsync();
        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        var first = await scopeA.ServiceProvider.GetRequiredService<TimedJobActionService>()
            .StartAsync(playerId, CheapTimedJobId, now);
        Assert.True(first.WasApplied);

        using var scopeB = _factory.Services.CreateScope();
        var second = await scopeB.ServiceProvider.GetRequiredService<TimedJobActionService>()
            .StartAsync(playerId, CheapTimedJobId, now);

        Assert.False(second.WasApplied);
        Assert.Equal(TimedJobFailure.AlreadyRunning, second.Failure);
        Assert.Equal(1, await RunningRowCountAsync(playerId));
    }

    // -------------------------------------------------------------------------
    // Double-collect — the same running row raced by two requests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_collects_of_the_same_ready_appointment_pay_out_exactly_once()
    {
        var playerId = await SeedPlayerAsync();
        var startNow = DateTimeOffset.UtcNow;

        using (var startScope = _factory.Services.CreateScope())
        {
            var started = await startScope.ServiceProvider.GetRequiredService<TimedJobActionService>()
                .StartAsync(playerId, CheapTimedJobId, startNow);
            Assert.True(started.WasApplied);
        }
        await BackdateRunningJobAsync(playerId, TimeSpan.FromHours(8));

        var before = await ReadPlayerAsync(playerId);
        var collectNow = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<TimedJobActionService>().CollectAsync(playerId, collectNow),
            scopeB.ServiceProvider.GetRequiredService<TimedJobActionService>().CollectAsync(playerId, collectNow));

        int succeeded = results.Count(r => r.WasApplied);
        int conflicted = results.Count(r => r.Error == TimedJobActionError.ConcurrencyConflict);

        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        var after = await ReadPlayerAsync(playerId);
        Assert.Equal(40, after.Xp - before.Xp); // the appointment's fixed XP, paid exactly once
        Assert.InRange(after.Cash - before.Cash, 180, 320);
        Assert.Equal(0, await RunningRowCountAsync(playerId));
    }
}
