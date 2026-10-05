using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Exercises the <c>ClearWornFlagOnEmptyHoldings</c> data migration against a real database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Players who ran <c>hijack-a-delivery-truck</c> before the fix hold a row at
/// zero copies that is still flagged as worn, which kept adding the item's attack and defense to their
/// loadout. The loader now ignores such a row, but the stored data should agree. Like the earlier energy
/// migration, this rewrites live rows, so it is driven through EF's own <see cref="IMigrator"/> rather
/// than by re-running a copy of its SQL: step back one migration, seed, step forward, assert.
/// </para>
/// <para>
/// Runs against its own throwaway database, never <c>underworld_test</c>, for the same reason as
/// <see cref="RaiseEnergyMaxMigrationTests"/>: it migrates up and down.
/// </para>
/// </remarks>
public class ClearWornFlagMigrationTests : IAsyncLifetime
{
    private const string ScratchConnectionString =
        "Host=localhost;Port=5433;Database=underworld_migration_test_worn;Username=underworld;Password=underworld";

    private const string Target = "20261004191328_ClearWornFlagOnEmptyHoldings";
    private const string Previous = "20261002122101_RaiseEnergyMaxForExistingPlayers";

    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<GameDbContext>(o => o.UseNpgsql(ScratchConnectionString));
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Up_takes_off_only_the_worn_rows_that_have_no_copies()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db,
            // the damaged row: spent its last copy while worn
            ("spent-worn", quantity: 0, equipped: true),
            // must be left exactly as it is
            ("held-worn", quantity: 1, equipped: true),
            ("held-not-worn", quantity: 2, equipped: false),
            ("spent-not-worn", quantity: 0, equipped: false));

        await migrator.MigrateAsync(Target);

        Assert.Equal(0, await CountAsync(db, "\"Quantity\" = 0 AND \"Equipped\" = TRUE"));
        Assert.Equal(1, await CountAsync(db, "\"Quantity\" = 1 AND \"Equipped\" = TRUE"));
        Assert.Equal(1, await CountAsync(db, "\"Quantity\" = 2 AND \"Equipped\" = FALSE"));
        // Two rows now sit at zero copies and unworn: the one that was repaired, and the one that
        // never was worn. Both still exist; only a flag moved.
        Assert.Equal(2, await CountAsync(db, "\"Quantity\" = 0 AND \"Equipped\" = FALSE"));
        Assert.Equal(4, await CountAsync(db, "TRUE"));
    }

    [Fact]
    public async Task Up_changes_no_quantity_and_no_other_row()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db, ("spent-worn", 0, true), ("held-worn", 3, true));

        await migrator.MigrateAsync(Target);

        // Only the flag moves: the stack that was held keeps every copy and stays worn.
        Assert.Equal(3, await ScalarAsync(db, "SELECT \"Quantity\" FROM \"PlayerItems\" WHERE \"ItemId\" = 'held-worn'"));
        Assert.Equal(1, await CountAsync(db, "\"ItemId\" = 'held-worn' AND \"Equipped\" = TRUE"));
    }

    [Fact]
    public async Task Up_changes_nothing_the_second_time()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db, ("spent-worn", 0, true), ("held-worn", 1, true));
        await migrator.MigrateAsync(Target);

        // Idempotence is a property of the predicate: after Up no row matches it, so running the same
        // statement again affects nothing. Re-running the SQL is the honest check; asking the migrator
        // again would do nothing at all and prove only that EF keeps a history table.
        var affected = await db.Database.ExecuteSqlRawAsync(
            """UPDATE "PlayerItems" SET "Equipped" = FALSE WHERE "Quantity" = 0 AND "Equipped" = TRUE;""");

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task Down_is_a_no_op_that_does_not_re_wear_anything()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db, ("spent-worn", 0, true), ("held-worn", 1, true));
        await migrator.MigrateAsync(Target);

        await migrator.MigrateAsync(Previous);

        // A worn item with no copies is the defect, not a state to restore.
        Assert.Equal(0, await CountAsync(db, "\"Quantity\" = 0 AND \"Equipped\" = TRUE"));
        Assert.Equal(1, await CountAsync(db, "\"Quantity\" = 1 AND \"Equipped\" = TRUE"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>One throwaway player owning one row per tuple (rows need a player: the FK is real).</summary>
    private static async Task SeedAsync(GameDbContext db, params (string itemId, int quantity, bool equipped)[] rows)
    {
        var playerId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Players" ("Id", "CreatedAtUtc", "DeviceToken") VALUES ({0}, now(), {1});""",
            playerId, playerId.ToString("N"));

        foreach (var (itemId, quantity, equipped) in rows)
        {
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "PlayerItems" ("PlayerId", "ItemId", "Quantity", "Equipped") VALUES ({0}, {1}, {2}, {3});""",
                playerId, itemId, quantity, equipped);
        }
    }

    private static async Task<int> CountAsync(GameDbContext db, string where)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = $"SELECT count(*) FROM \"PlayerItems\" WHERE {where}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ScalarAsync(GameDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
