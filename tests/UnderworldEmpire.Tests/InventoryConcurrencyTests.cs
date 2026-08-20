using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves that two simultaneous inventory requests against one player cannot produce a state the rules
/// forbid: a second item bought from cash that only covered one, or two items worn in the same slot.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="JobConcurrencyTests"/>. The equip case is the interesting one — equipping
/// writes only <c>PlayerItems</c> rows, which carry no concurrency token, and the invariant it can break
/// (one worn item per slot) spans rows rather than living in one. Requires Postgres on 5433.
/// </remarks>
public class InventoryConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>Level 1, 75 cash, +4 attack.</summary>
    private const string CheapWeapon = "brass-knuckles";

    /// <summary>Level 2, 180 cash, +7 attack — the second weapon, for the slot-exclusivity race.</summary>
    private const string SecondWeapon = "switchblade";

    /// <summary>Level 1, 120 cash — a different slot, so a concurrent buy races only on cash.</summary>
    private const string CheapArmor = "leather-jacket";

    public InventoryConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Creates a player directly in the database, optionally already holding some items.</summary>
    private async Task<Guid> SeedPlayerAsync(long cash, int level, params string[] owned)
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

        foreach (var itemId in owned)
        {
            var holding = new PlayerItem { PlayerId = player.Id, ItemId = itemId };
            holding.AddCopies(1);
            db.PlayerItems.Add(holding);
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

    private async Task<List<PlayerItem>> ReadHoldingsAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return await db.PlayerItems.AsNoTracking().Where(h => h.PlayerId == playerId).ToListAsync();
    }

    /// <summary>Runs <paramref name="action"/> twice at once, each in its own DI scope.</summary>
    /// <remarks>
    /// Two scopes means two DbContexts — the shape a genuine double-tap takes, where each HTTP request
    /// gets its own scoped context. Both read before either writes; that is what creates the race.
    /// </remarks>
    private async Task<TResult[]> RaceAsync<TResult>(
        Func<InventoryActionService, Task<TResult>> first,
        Func<InventoryActionService, Task<TResult>> second)
    {
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        return await Task.WhenAll(
            first(scopeA.ServiceProvider.GetRequiredService<InventoryActionService>()),
            second(scopeB.ServiceProvider.GetRequiredService<InventoryActionService>()));
    }

    // -------------------------------------------------------------------------
    // Buying
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_first_purchases_of_one_item_charge_for_what_was_actually_bought()
    {
        // Both attempts INSERT the same (PlayerId, ItemId) row. The loser hits the composite primary key
        // rather than the concurrency token, so it is the unique-violation path that has to catch it.
        var playerId = await SeedPlayerAsync(cash: 1_000, level: 1);

        var results = await RaceAsync(
            s => s.PurchaseAsync(playerId, CheapWeapon),
            s => s.PurchaseAsync(playerId, CheapWeapon));

        int succeeded = results.Count(r => r.Succeeded);
        int conflicted = results.Count(r => r.Error == InventoryActionError.ConcurrencyConflict);

        Assert.True(succeeded >= 1, "At least one purchase should have gone through.");
        Assert.Equal(2, succeeded + conflicted);

        var holding = Assert.Single(await ReadHoldingsAsync(playerId));
        Assert.Equal(succeeded, holding.Quantity);
        Assert.Equal(1_000 - (succeeded * 75), await ReadCashAsync(playerId));
    }

    [Fact]
    public async Task Two_concurrent_purchases_of_different_items_cannot_spend_the_same_cash_twice()
    {
        // 150 covers the knuckles (75) or the jacket (120), but not both. Without the xmin token both
        // requests read the same balance and both deduct from it, leaving the player 45 in debt or, worse,
        // holding two items they could only afford one of.
        var playerId = await SeedPlayerAsync(cash: 150, level: 1);

        var results = await RaceAsync(
            s => s.PurchaseAsync(playerId, CheapWeapon),
            s => s.PurchaseAsync(playerId, CheapArmor));

        var holdings = await ReadHoldingsAsync(playerId);
        long spent = 150 - await ReadCashAsync(playerId);

        Assert.True(spent <= 150, $"Player spent {spent} of a 150 balance.");
        Assert.All(results, r => Assert.True(
            r.Succeeded || r.Error == InventoryActionError.ConcurrencyConflict || r.Result?.Succeeded == false));

        // Whatever committed must be paid for exactly once.
        long expected = holdings.Sum(h => h.ItemId == CheapWeapon ? 75L : 120L);
        Assert.Equal(expected, spent);
    }

    // -------------------------------------------------------------------------
    // Equipping — the invariant with no database constraint behind it
    // -------------------------------------------------------------------------

    /// <summary>
    /// How many times the equip race is replayed, each against a fresh player.
    /// </summary>
    /// <remarks>
    /// Whether two concurrent requests genuinely interleave is up to the scheduler, so a single round
    /// proves nothing when it happens to serialise. Replaying makes the detection reliable without making
    /// the assertion timing-dependent: every round asserts the same invariant, and one violated round is
    /// a failure.
    /// </remarks>
    private const int RaceRounds = 5;

    [Fact]
    public async Task Concurrent_equips_never_leave_two_weapons_in_one_slot()
    {
        // Each request sees an empty weapon slot, so neither has an incumbent to displace. Nothing in
        // PlayerItems stops both writes landing: the rows are different, and the slot they share lives in
        // items.json rather than in a column an index could cover.
        for (int round = 0; round < RaceRounds; round++)
        {
            var playerId = await SeedPlayerAsync(cash: 1_000, level: 2, CheapWeapon, SecondWeapon);

            await RaceAsync(
                s => s.EquipAsync(playerId, CheapWeapon),
                s => s.EquipAsync(playerId, SecondWeapon));

            var equipped = (await ReadHoldingsAsync(playerId)).Where(h => h.Equipped).ToList();

            Assert.True(
                equipped.Count <= 1,
                $"Round {round}: player ended up wearing {equipped.Count} weapons at once " +
                $"({string.Join(", ", equipped.Select(h => h.ItemId))}).");
        }
    }

    [Fact]
    public async Task A_player_who_raced_two_equips_can_still_read_their_own_loadout()
    {
        // The consequence of the state above: Loadout.For refuses to aggregate two items in one slot, so
        // a lost update would not show as a wrong number — it would show as a 500 on every subsequent read
        // of the player's own inventory, permanently.
        for (int round = 0; round < RaceRounds; round++)
        {
            var playerId = await SeedPlayerAsync(cash: 1_000, level: 2, CheapWeapon, SecondWeapon);

            await RaceAsync(
                s => s.EquipAsync(playerId, CheapWeapon),
                s => s.EquipAsync(playerId, SecondWeapon));

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var catalog = scope.ServiceProvider.GetRequiredService<IContentPackProvider>().ItemCatalog;

            var player = await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId);
            var holdings = await db.PlayerItems.AsNoTracking().Where(h => h.PlayerId == playerId).ToListAsync();

            var loadout = Loadout.For(player, catalog.ResolveEquipped(holdings));

            Assert.InRange(loadout.ItemAttack, 0, 7);
        }
    }

    [Fact]
    public async Task Sequential_equips_both_succeed_and_the_second_displaces_the_first()
    {
        // The counterpart to the race: with no contention both requests must go through. A guard that
        // rejected legitimate sequential work would be worse than none.
        var playerId = await SeedPlayerAsync(cash: 1_000, level: 2, CheapWeapon, SecondWeapon);

        using var scopeA = _factory.Services.CreateScope();
        var first = await scopeA.ServiceProvider.GetRequiredService<InventoryActionService>()
            .EquipAsync(playerId, CheapWeapon);

        using var scopeB = _factory.Services.CreateScope();
        var second = await scopeB.ServiceProvider.GetRequiredService<InventoryActionService>()
            .EquipAsync(playerId, SecondWeapon);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(CheapWeapon, second.Result!.UnequippedItemId);

        var equipped = (await ReadHoldingsAsync(playerId)).Where(h => h.Equipped).ToList();
        Assert.Equal(SecondWeapon, Assert.Single(equipped).ItemId);
    }

    [Fact]
    public async Task An_equip_does_not_move_the_players_cash()
    {
        // The player row is written on every equip purely to get its concurrency token into the statement.
        // That write must remain a no-op to the data.
        var playerId = await SeedPlayerAsync(cash: 777, level: 2, CheapWeapon);

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InventoryActionService>()
            .EquipAsync(playerId, CheapWeapon);

        Assert.Equal(777, await ReadCashAsync(playerId));
    }
}
