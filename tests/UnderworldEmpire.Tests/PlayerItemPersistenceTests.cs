using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Proves the inventory schema behaves the way the domain assumes (T3.1): a holding is keyed by
/// (player, item), survives a round trip with its quantity and equipped flag intact, and disappears with
/// its player.
/// </summary>
/// <remarks>
/// The composite key and the cascade are schema guarantees rather than code, so only a real database can
/// confirm them. Requires Postgres on 5433, like the other DB-backed suites.
/// </remarks>
public class PlayerItemPersistenceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PlayerItemPersistenceTests(CustomWebApplicationFactory factory)
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

    [Fact]
    public async Task A_holding_round_trips_with_its_quantity_and_equipped_flag()
    {
        var playerId = await SeedPlayerAsync();

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            var holding = new PlayerItem { PlayerId = playerId, ItemId = "brass-knuckles" };
            holding.AddCopies(3);
            holding.Equip();

            db.PlayerItems.Add(holding);
            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            var loaded = await readDb.PlayerItems
                .AsNoTracking()
                .FirstAsync(i => i.PlayerId == playerId && i.ItemId == "brass-knuckles");

            Assert.Equal(3, loaded.Quantity);
            Assert.True(loaded.Equipped);
        }
    }

    [Fact]
    public async Task One_player_can_hold_several_different_items()
    {
        var playerId = await SeedPlayerAsync();

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            foreach (var id in new[] { "brass-knuckles", "leather-jacket" })
            {
                var holding = new PlayerItem { PlayerId = playerId, ItemId = id };
                holding.AddCopies(1);
                db.PlayerItems.Add(holding);
            }

            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            var count = await readDb.PlayerItems.CountAsync(i => i.PlayerId == playerId);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task The_same_item_can_be_held_by_two_different_players()
    {
        var first = await SeedPlayerAsync();
        var second = await SeedPlayerAsync();

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            foreach (var playerId in new[] { first, second })
            {
                var holding = new PlayerItem { PlayerId = playerId, ItemId = "switchblade" };
                holding.AddCopies(1);
                db.PlayerItems.Add(holding);
            }

            await db.SaveChangesAsync();
        }

        using var readDb = NewContext(out var readScope);
        using (readScope)
        {
            var count = await readDb.PlayerItems.CountAsync(i => i.ItemId == "switchblade"
                && (i.PlayerId == first || i.PlayerId == second));
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task A_second_row_for_the_same_player_and_item_is_rejected_by_the_composite_key()
    {
        var playerId = await SeedPlayerAsync();

        using (var db = NewContext(out var firstScope))
        using (firstScope)
        {
            var holding = new PlayerItem { PlayerId = playerId, ItemId = "kevlar-vest" };
            holding.AddCopies(1);
            db.PlayerItems.Add(holding);
            await db.SaveChangesAsync();
        }

        using var second = NewContext(out var secondScope);
        using (secondScope)
        {
            var duplicate = new PlayerItem { PlayerId = playerId, ItemId = "kevlar-vest" };
            duplicate.AddCopies(1);
            second.PlayerItems.Add(duplicate);

            // Buying a second copy must stack on the existing row; a schema that allowed two rows would
            // let a player's holdings silently disagree with themselves.
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Deleting_a_player_takes_their_holdings_with_them()
    {
        var playerId = await SeedPlayerAsync();

        using (var db = NewContext(out var writeScope))
        using (writeScope)
        {
            var holding = new PlayerItem { PlayerId = playerId, ItemId = "stolen-sedan" };
            holding.AddCopies(1);
            db.PlayerItems.Add(holding);
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
            Assert.False(await readDb.PlayerItems.AnyAsync(i => i.PlayerId == playerId));
        }
    }

    [Fact]
    public void The_host_loads_the_item_catalog_at_startup()
    {
        // Loading at startup (rather than lazily) is what makes a content mistake fail the host instead of
        // surfacing as a 500 on the first request that touches the store.
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IContentPackProvider>();

        Assert.NotEmpty(provider.ItemCatalog.Items);
        Assert.NotNull(provider.ItemCatalog.Find("brass-knuckles"));
    }

    [Fact]
    public void The_inventory_service_is_resolvable_from_the_container()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InventoryService>());
    }
}
