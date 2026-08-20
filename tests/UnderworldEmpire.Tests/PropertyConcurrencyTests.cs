using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves that two simultaneous property requests against one player cannot produce a state the rules
/// forbid: a second property bought from cash that only covered one, or one holding's takings paid out
/// twice.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="InventoryConcurrencyTests"/>. The collect case is the interesting one:
/// unlike a refused buy, a lost update here does not merely give something away — it lets a single
/// holding's banked income be credited to the player more than once, which is a cash printer rather than
/// a discount. Requires Postgres on 5433.
/// </remarks>
public class PropertyConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>Level 1, 500 cash, 240/hour (4 a minute), a 15-minute window.</summary>
    private const string Starter = "street-corner";

    /// <summary>Level 2, 1800 cash — a second property, for the shared-cash race.</summary>
    private const string SecondProperty = "pawn-shop";

    private const int StarterPerMinute = 4;

    /// <summary>
    /// How many times each race is replayed, every round against a fresh player.
    /// </summary>
    /// <remarks>
    /// Whether two concurrent requests genuinely interleave is up to the scheduler, so a single round
    /// proves nothing when it happens to serialise. Same reasoning as
    /// <see cref="InventoryConcurrencyTests"/>.
    /// </remarks>
    private const int RaceRounds = 5;

    public PropertyConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Property Definition(string propertyId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IContentPackProvider>()
            .PropertyCatalog.Find(propertyId)!;
    }

    /// <summary>
    /// Creates a player directly in the database, optionally already holding one unit of
    /// <paramref name="owned"/> that has been earning for <paramref name="earningFor"/>.
    /// </summary>
    /// <remarks>
    /// The holding is aged by starting its clock in the past rather than by waiting: <c>AddUnits</c>
    /// settles an empty holding to whatever instant it is given, so passing a past instant is the
    /// supported way to produce a property that already has takings on it.
    /// </remarks>
    private async Task<Guid> SeedPlayerAsync(
        long cash,
        int level,
        string? owned = null,
        TimeSpan earningFor = default)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var now = DateTimeOffset.UtcNow;
        var player = new Player
        {
            CreatedAtUtc = now,
            DeviceToken = DeviceTokenGenerator.Generate(),
            Cash = cash,
            Level = level,
            Energy = Meter.Full(PlayerDefaults.EnergyMax, now),
            Stamina = Meter.Full(PlayerDefaults.StaminaMax, now),
            Health = Meter.Full(PlayerDefaults.HealthMax, now)
        };

        db.Players.Add(player);

        if (owned is not null)
        {
            var holding = new PlayerProperty { PlayerId = player.Id, PropertyId = owned };
            holding.AddUnits(Definition(owned), 1, now - earningFor);
            db.PlayerProperties.Add(holding);
        }

        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<long> ReadCashAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return (await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId)).Cash;
    }

    private async Task<List<PlayerProperty>> ReadHoldingsAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return await db.PlayerProperties.AsNoTracking().Where(h => h.PlayerId == playerId).ToListAsync();
    }

    /// <summary>Runs both actions at once, each in its own DI scope.</summary>
    /// <remarks>
    /// Two scopes means two DbContexts — the shape a genuine double-tap takes, where each HTTP request
    /// gets its own scoped context. Both read before either writes; that is what creates the race.
    /// </remarks>
    private async Task<TResult[]> RaceAsync<TResult>(
        Func<PropertyActionService, Task<TResult>> first,
        Func<PropertyActionService, Task<TResult>> second)
    {
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        return await Task.WhenAll(
            first(scopeA.ServiceProvider.GetRequiredService<PropertyActionService>()),
            second(scopeB.ServiceProvider.GetRequiredService<PropertyActionService>()));
    }

    // -------------------------------------------------------------------------
    // Collecting — one holding's takings must be paid once
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_collects_pay_one_holding_once()
    {
        for (int round = 0; round < RaceRounds; round++)
        {
            // Five minutes at 4 a minute: 20 banked. Without the xmin token both requests read the same
            // pending balance, both credit it, and the player banks 40 from a property that earned 20.
            var playerId = await SeedPlayerAsync(
                cash: 0, level: 1, owned: Starter, earningFor: TimeSpan.FromMinutes(5));

            var now = DateTimeOffset.UtcNow;
            var results = await RaceAsync(
                s => s.CollectAsync(playerId, Starter, now),
                s => s.CollectAsync(playerId, Starter, now));

            long banked = await ReadCashAsync(playerId);
            long paidOut = results.Where(r => r.Succeeded).Sum(r => r.Result!.CashCollected);

            Assert.True(
                banked <= 5 * StarterPerMinute + StarterPerMinute,
                $"Round {round}: player banked {banked} from a holding that had earned about " +
                $"{5 * StarterPerMinute}.");

            // Every reported payout must correspond to cash that actually landed, and vice versa: a
            // response claiming a payout the transaction rolled back is as wrong as a double credit.
            Assert.Equal(paidOut, banked);
        }
    }

    [Fact]
    public async Task A_collect_that_loses_the_race_is_reported_rather_than_silently_dropped()
    {
        for (int round = 0; round < RaceRounds; round++)
        {
            var playerId = await SeedPlayerAsync(
                cash: 0, level: 1, owned: Starter, earningFor: TimeSpan.FromMinutes(5));

            var now = DateTimeOffset.UtcNow;
            var results = await RaceAsync(
                s => s.CollectAsync(playerId, Starter, now),
                s => s.CollectAsync(playerId, Starter, now));

            // A loser is either a reported conflict or a domain refusal — never a success that did not
            // happen. The client can tell the difference and re-read.
            Assert.All(results, r => Assert.True(
                r.Succeeded
                || r.Error == PropertyActionError.ConcurrencyConflict
                || r.Result?.Succeeded == false,
                $"Round {round}: a collect neither succeeded nor reported why not."));
        }
    }

    [Fact]
    public async Task Sequential_collects_pay_out_each_time_more_income_has_accrued()
    {
        // The counterpart to the race: with no contention, legitimate repeat collections must go
        // through. A guard that rejected sequential work would be worse than none.
        var playerId = await SeedPlayerAsync(
            cash: 0, level: 1, owned: Starter, earningFor: TimeSpan.FromMinutes(5));

        var now = DateTimeOffset.UtcNow;

        using var scopeA = _factory.Services.CreateScope();
        var first = await scopeA.ServiceProvider.GetRequiredService<PropertyActionService>()
            .CollectAsync(playerId, Starter, now);

        using var scopeB = _factory.Services.CreateScope();
        var second = await scopeB.ServiceProvider.GetRequiredService<PropertyActionService>()
            .CollectAsync(playerId, Starter, now.AddMinutes(5));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(5 * StarterPerMinute, first.Result!.CashCollected);
        Assert.Equal(5 * StarterPerMinute, second.Result!.CashCollected);
        Assert.Equal(10 * StarterPerMinute, await ReadCashAsync(playerId));
    }

    // -------------------------------------------------------------------------
    // Buying
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_first_purchases_of_one_property_charge_for_what_was_bought()
    {
        // Both attempts INSERT the same (PlayerId, PropertyId) row. The loser hits the composite primary
        // key rather than the concurrency token, so it is the unique-violation path that catches it.
        var playerId = await SeedPlayerAsync(cash: 2_000, level: 1);

        var now = DateTimeOffset.UtcNow;
        var results = await RaceAsync(
            s => s.PurchaseAsync(playerId, Starter, now),
            s => s.PurchaseAsync(playerId, Starter, now));

        int succeeded = results.Count(r => r.Succeeded);
        int conflicted = results.Count(r => r.Error == PropertyActionError.ConcurrencyConflict);

        Assert.True(succeeded >= 1, "At least one purchase should have gone through.");
        Assert.Equal(2, succeeded + conflicted);

        var holding = Assert.Single(await ReadHoldingsAsync(playerId));
        Assert.Equal(succeeded, holding.Quantity);
        Assert.Equal(2_000 - (succeeded * 500), await ReadCashAsync(playerId));
    }

    [Fact]
    public async Task Two_concurrent_purchases_of_different_properties_cannot_spend_the_same_cash_twice()
    {
        // 2000 covers the corner (500) or the pawn shop (1800), but not both.
        var playerId = await SeedPlayerAsync(cash: 2_000, level: 2);

        var now = DateTimeOffset.UtcNow;
        await RaceAsync(
            s => s.PurchaseAsync(playerId, Starter, now),
            s => s.PurchaseAsync(playerId, SecondProperty, now));

        var holdings = await ReadHoldingsAsync(playerId);
        long spent = 2_000 - await ReadCashAsync(playerId);

        Assert.True(spent <= 2_000, $"Player spent {spent} of a 2000 balance.");

        // Whatever committed must be paid for exactly once.
        long expected = holdings.Sum(h => h.PropertyId == Starter ? 500L : 1_800L);
        Assert.Equal(expected, spent);
    }

    [Fact]
    public async Task Buying_while_collecting_neither_double_pays_nor_double_charges()
    {
        // The two write paths crossing: one credits the player, the other debits them, and both settle
        // the same holding. Whatever commits, the cash must equal the starting balance plus what was
        // genuinely collected minus what was genuinely bought.
        for (int round = 0; round < RaceRounds; round++)
        {
            var playerId = await SeedPlayerAsync(
                cash: 1_000, level: 1, owned: Starter, earningFor: TimeSpan.FromMinutes(5));

            var now = DateTimeOffset.UtcNow;
            var buy = RaceAsync(
                s => s.PurchaseAsync(playerId, Starter, now).ContinueWith(t => (object)t.Result),
                s => s.CollectAsync(playerId, Starter, now).ContinueWith(t => (object)t.Result));

            var results = await buy;

            long collected = results.OfType<PropertyCollectActionResult>()
                .Where(r => r.Succeeded).Sum(r => r.Result!.CashCollected);
            long charged = results.OfType<PropertyPurchaseActionResult>()
                .Where(r => r.Succeeded).Sum(r => r.Result!.CashSpent);

            Assert.Equal(1_000 + collected - charged, await ReadCashAsync(playerId));
        }
    }
}
