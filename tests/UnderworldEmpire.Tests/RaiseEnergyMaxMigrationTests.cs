using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Exercises the <c>RaiseEnergyMaxForExistingPlayers</c> data migration against a real database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> This is the project's first <i>data</i> migration: it rewrites rows in
/// a live <c>Players</c> table rather than changing the schema. It was verified by hand once,
/// against a development database, and nothing in CI repeated that — the file measured 0% covered.
/// A migration that mis-fires is not a failing test, it is damaged player data, and the window to
/// notice is the deploy.
/// </para>
/// <para>
/// <b>How it runs the real thing.</b> The migration is driven through EF's own
/// <see cref="IMigrator"/> rather than by re-executing a copy of its SQL — a test that restated
/// the UPDATE would only prove the test and the migration agree with each other. Because the
/// migration is data-only the schema is identical on both sides of it, so the sequence is: create
/// the scratch database at the latest schema, step back one migration, seed the rows, then step
/// forward and assert what <c>Up</c> did.
/// </para>
/// <para>
/// Runs against its own throwaway database, never <c>underworld_test</c>: it migrates up and down
/// and would otherwise fight the integration tests sharing that schema.
/// </para>
/// </remarks>
public class RaiseEnergyMaxMigrationTests : IAsyncLifetime
{
    private const string ScratchConnectionString =
        "Host=localhost;Port=5433;Database=underworld_migration_test;Username=underworld;Password=underworld";

    /// <summary>The migration under test.</summary>
    private const string Target = "20261002122101_RaiseEnergyMaxForExistingPlayers";

    /// <summary>The state immediately before it — where seeding happens.</summary>
    private const string Previous = "20260902113225_AddPlayerAccountCredentials";

    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<GameDbContext>(o => o.UseNpgsql(ScratchConnectionString));
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        // A clean database every run: these tests assert on exact row counts, so a leftover row
        // from a previous run would make them lie in either direction.
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
    public async Task Up_raises_the_old_default_and_leaves_every_other_bar_alone()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db,
            // two on the old default, which is what the migration is for
            (max: 10, stored: 10), (max: 10, stored: 3),
            // one already on the new default — created after the constant moved
            (max: 20, stored: 0),
            // and one hand-tuned to neither, which must be treated as deliberate
            (max: 7, stored: 7));

        await migrator.MigrateAsync(Target);

        Assert.Equal(0, await CountAsync(db, "\"EnergyMax\" = 10"));
        Assert.Equal(3, await CountAsync(db, "\"EnergyMax\" = 20"));
        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 7"));

        // Only the ceiling moves. A player sitting at 10/10 becomes 10/20 and regenerates into the
        // new headroom at the usual rate, rather than being handed ten free energy.
        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 20 AND \"EnergyStoredValue\" = 10"));
        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 20 AND \"EnergyStoredValue\" = 3"));
    }

    [Fact]
    public async Task Up_changes_nothing_the_second_time()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db, (max: 10, stored: 10), (max: 7, stored: 7));
        await migrator.MigrateAsync(Target);

        // Idempotence is a property of the predicate, not of EF's migration bookkeeping: after Up
        // no row matches `EnergyMax = 10` any more, so running the same statement again is a
        // no-op. Re-running the SQL directly is the honest way to assert that — going through the
        // migrator a second time would do nothing at all and prove only that EF keeps a history
        // table.
        var affected = await db.Database.ExecuteSqlRawAsync(
            """UPDATE "Players" SET "EnergyMax" = 20 WHERE "EnergyMax" = 10;""");

        Assert.Equal(0, affected);
        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 7"));
    }

    [Fact]
    public async Task Down_narrows_the_bar_and_never_leaves_stored_energy_above_it()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await SeedAsync(db, (max: 10, stored: 10), (max: 7, stored: 7));
        await migrator.MigrateAsync(Target);

        // Let the raised player bank into the new headroom, so Down has something to clamp.
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "Players" SET "EnergyStoredValue" = 18 WHERE "EnergyMax" = 20;""");

        await migrator.MigrateAsync(Previous);

        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 10"));
        // The clamp is the point: a row left holding 18 against a ceiling of 10 would be an
        // inconsistent state for anything reading the column directly, even though Meter.CurrentAt
        // would mask it by taking the minimum.
        Assert.Equal(0, await CountAsync(db, "\"EnergyStoredValue\" > \"EnergyMax\""));
        Assert.Equal(1, await CountAsync(db, "\"EnergyMax\" = 7 AND \"EnergyStoredValue\" = 7"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Inserts one player per supplied bar.
    /// </summary>
    /// <remarks>
    /// Only the columns this test must control are listed: <c>Id</c> and <c>CreatedAtUtc</c>
    /// (no database default), the two energy columns under test, and <c>DeviceToken</c> — which
    /// does have a default, of <c>''</c>, under a unique index, so a second row relying on it
    /// collides. Everything else is left to its default; spelling the rest out would couple this
    /// test to columns it does not care about.
    /// </remarks>
    private static async Task SeedAsync(
        GameDbContext db, params (int max, int stored)[] bars)
    {
        foreach (var (max, stored) in bars)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Players"
                    ("Id", "CreatedAtUtc", "DeviceToken", "EnergyMax", "EnergyStoredValue")
                VALUES
                    (gen_random_uuid(), now(), gen_random_uuid()::text, {0}, {1});
                """,
                max, stored);
        }
    }

    private static async Task<int> CountAsync(GameDbContext db, string whereClause)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM "Players" WHERE {whereClause};""";

        await db.Database.OpenConnectionAsync();
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
