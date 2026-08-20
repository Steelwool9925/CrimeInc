using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves the property schema behaves the way the domain assumes (T3.3): a holding is keyed by
/// (player, property), survives a round trip with its units, banked cash and accrual instant intact,
/// and disappears with its player.
/// </summary>
/// <remarks>
/// The composite key, the cascade and the instant's precision are schema guarantees rather than code,
/// so only a real database can confirm them. Requires Postgres on 5433, like the other DB-backed
/// suites. Mirrors <see cref="PlayerItemPersistenceTests"/>.
/// </remarks>
public class PlayerPropertyPersistenceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PlayerPropertyPersistenceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
    }

    private GameDbContext NewContext(out IServiceScope scope)
    {
        scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GameDbContext>();
    }

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

    private static Property StreetCorner() => new()
    {
        Id = "street-corner",
        Name = "Street Corner",
        LevelRequirement = 1,
        Price = 500,
        IncomePerHour = 240,
        AccrualCapMinutes = 15
    };

    [Fact]
    public async Task A_holding_round_trips_with_its_units_and_banked_cash()
    {
        var playerId = await SeedPlayerAsync();
        var property = StreetCorner();
        var boughtAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            var holding = new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };
            holding.AddUnits(property, 2, boughtAt);
            holding.CollectAt(property, boughtAt.AddMinutes(5));
            holding.AddUnits(property, 1, boughtAt.AddMinutes(6));

            db.PlayerProperties.Add(holding);
            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            var loaded = await readDb.PlayerProperties
                .AsNoTracking()
                .FirstAsync(p => p.PlayerId == playerId && p.PropertyId == property.Id);

            Assert.Equal(3, loaded.Quantity);
            Assert.Equal(8, loaded.UncollectedCash);
        }
    }

    [Fact]
    public async Task The_accrual_instant_survives_a_round_trip_precisely_enough_to_keep_accruing()
    {
        // The stored instant is not decoration: income is derived from the gap between it and now, so a
        // column that rounded it to the second would quietly change every payout.
        var playerId = await SeedPlayerAsync();
        var property = StreetCorner();
        var boughtAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            var holding = new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };
            holding.AddUnits(property, 1, boughtAt);
            db.PlayerProperties.Add(holding);
            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            var loaded = await readDb.PlayerProperties
                .AsNoTracking()
                .FirstAsync(p => p.PlayerId == playerId);

            // Postgres timestamptz keeps microseconds, so the round trip is exact to within a tick.
            Assert.True((loaded.AccruedAtUtc - boughtAt).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.Equal(4, loaded.PendingAt(property, loaded.AccruedAtUtc.AddMinutes(1)));
        }
    }

    [Fact]
    public async Task One_player_can_hold_several_different_properties()
    {
        var playerId = await SeedPlayerAsync();

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            foreach (var id in new[] { "street-corner", "pawn-shop" })
            {
                var holding = new PlayerProperty { PlayerId = playerId, PropertyId = id };
                holding.AddUnits(StreetCorner() with { Id = id }, 1, DateTimeOffset.UtcNow);
                db.PlayerProperties.Add(holding);
            }

            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            Assert.Equal(2, await readDb.PlayerProperties.CountAsync(p => p.PlayerId == playerId));
        }
    }

    [Fact]
    public async Task A_second_row_for_the_same_player_and_property_is_rejected_by_the_composite_key()
    {
        var playerId = await SeedPlayerAsync();
        var property = StreetCorner() with { Id = "car-wash" };

        using (var db = NewContext(out var firstScope))
        using (firstScope)
        {
            var holding = new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };
            holding.AddUnits(property, 1, DateTimeOffset.UtcNow);
            db.PlayerProperties.Add(holding);
            await db.SaveChangesAsync();
        }

        using var second = NewContext(out var secondScope);
        using (secondScope)
        {
            var duplicate = new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };
            duplicate.AddUnits(property, 1, DateTimeOffset.UtcNow);
            second.PlayerProperties.Add(duplicate);

            // Buying a second unit must stack on the existing row. Two rows would each accrue their own
            // income against their own clock, and the player would collect twice.
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Deleting_a_player_takes_their_properties_with_them()
    {
        var playerId = await SeedPlayerAsync();
        var property = StreetCorner() with { Id = "night-club" };

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            var holding = new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };
            holding.AddUnits(property, 1, DateTimeOffset.UtcNow);
            db.PlayerProperties.Add(holding);
            await db.SaveChangesAsync();
        }

        using (var db = NewContext(out var deleteScope))
        using (deleteScope)
        {
            var player = await db.Players.FirstAsync(p => p.Id == playerId);
            db.Players.Remove(player);
            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            Assert.False(await readDb.PlayerProperties.AnyAsync(p => p.PlayerId == playerId));
        }
    }

    [Fact]
    public void The_host_loads_the_property_catalog_at_startup()
    {
        // Loading at startup (rather than lazily) is what makes a content mistake fail the host instead
        // of surfacing as a 500 on the first request that opens the properties screen.
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IContentPackProvider>();

        Assert.NotEmpty(provider.PropertyCatalog.Properties);
        Assert.NotNull(provider.PropertyCatalog.Find("street-corner"));
    }
}
