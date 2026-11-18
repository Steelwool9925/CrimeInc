using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Domain.Timed;

namespace UnderworldEmpire.Infrastructure;

/// <summary>
/// The single application DbContext. Owns all persistent game state. Entity
/// configuration lives in <see cref="OnModelCreating"/> until it grows enough to
/// warrant per-entity IEntityTypeConfiguration classes (M1+).
/// </summary>
public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();

    public DbSet<PlayerJobMastery> PlayerJobMasteries => Set<PlayerJobMastery>();

    public DbSet<PlayerItem> PlayerItems => Set<PlayerItem>();

    public DbSet<PlayerProperty> PlayerProperties => Set<PlayerProperty>();

    public DbSet<PlayerDailyObjective> PlayerDailyObjectives => Set<PlayerDailyObjective>();

    public DbSet<PlayerTimedJob> PlayerTimedJobs => Set<PlayerTimedJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var player = modelBuilder.Entity<Player>();
        player.HasKey(p => p.Id);
        player.Property(p => p.CreatedAtUtc).IsRequired();

        // Optimistic concurrency via PostgreSQL's xmin system column, mapped as a shadow property.
        // Needs no DDL — every Postgres row already carries xmin — but it turns the do-job
        // read-modify-write into a safe operation: a second concurrent request that read the same
        // energy/cash snapshot now fails with DbUpdateConcurrencyException instead of banking a
        // duplicate payout. (The older UseXminAsConcurrencyToken() helper no longer exists in this
        // Npgsql version; this is the current supported form.)
        player.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        player.Property(p => p.DeviceToken)
            .IsRequired()
            .HasMaxLength(64);
        player.HasIndex(p => p.DeviceToken)
            .IsUnique();

        player.Property(p => p.Username)
            .HasMaxLength(AccountCredentials.MaxUsernameLength);
        player.Property(p => p.PasswordHash)
            .HasMaxLength(200); // PBKDF2 output string: "{iterations}.{saltBase64}.{hashBase64}" comfortably fits.

        // A filtered unique index: multiple guest players with a null Username must coexist (a
        // plain unique index would only allow ONE null in some databases, and even where nulls
        // are exempt by default, being explicit here documents the intent rather than relying on
        // Postgres's default null-handling). Only a *claimed* username needs to be unique.
        player.HasIndex(p => p.Username)
            .IsUnique()
            .HasFilter("\"Username\" IS NOT NULL");

        // Energy meter — mapped as a complex type (EF 8+) with explicit column names
        player.ComplexProperty(p => p.Energy, b =>
        {
            b.Property(m => m.Max).HasColumnName("EnergyMax").IsRequired();
            b.Property(m => m.StoredValue).HasColumnName("EnergyStoredValue").IsRequired();
            b.Property(m => m.StoredAtUtc).HasColumnName("EnergyStoredAtUtc").IsRequired();
        });

        // Stamina meter
        player.ComplexProperty(p => p.Stamina, b =>
        {
            b.Property(m => m.Max).HasColumnName("StaminaMax").IsRequired();
            b.Property(m => m.StoredValue).HasColumnName("StaminaStoredValue").IsRequired();
            b.Property(m => m.StoredAtUtc).HasColumnName("StaminaStoredAtUtc").IsRequired();
        });

        // Health meter
        player.ComplexProperty(p => p.Health, b =>
        {
            b.Property(m => m.Max).HasColumnName("HealthMax").IsRequired();
            b.Property(m => m.StoredValue).HasColumnName("HealthStoredValue").IsRequired();
            b.Property(m => m.StoredAtUtc).HasColumnName("HealthStoredAtUtc").IsRequired();
        });

        // Per-player, per-job mastery progress.
        var mastery = modelBuilder.Entity<PlayerJobMastery>();

        // Composite natural key rather than a surrogate id: a player can hold exactly one mastery
        // row per job, so the uniqueness the domain requires becomes a schema guarantee instead of
        // an extra unique index alongside a redundant identity column. It also makes the lookup by
        // (PlayerId, JobId) a primary-key seek.
        mastery.HasKey(m => new { m.PlayerId, m.JobId });

        // JobId is a content id, not a foreign key — the job catalog lives in jobs.json, not the
        // database. Length matches Player.DeviceToken's cap for consistency.
        mastery.Property(m => m.JobId).IsRequired().HasMaxLength(64);
        mastery.Property(m => m.Completions).IsRequired();
        mastery.Property(m => m.Rank).IsRequired();

        // Deleting a player must take their mastery rows with it. No navigation property is
        // declared on Player — nothing needs to load every mastery row for a player, and adding one
        // would invite exactly that.
        mastery.HasOne<Player>()
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-player, per-item inventory holdings.
        var holding = modelBuilder.Entity<PlayerItem>();

        // Composite natural key, same reasoning as the mastery row above: a player holds exactly one row
        // per item — a second copy raises Quantity rather than inserting again — so uniqueness becomes a
        // schema guarantee and the lookup by (PlayerId, ItemId) is a primary-key seek.
        holding.HasKey(i => new { i.PlayerId, i.ItemId });

        // ItemId is a content id, not a foreign key — the item catalog lives in items.json, not the
        // database. The 64-character cap is enforced up front by ContentPackLoader.ValidateItems, so an
        // over-long id fails at startup instead of on the first purchase.
        holding.Property(i => i.ItemId).IsRequired().HasMaxLength(64);
        holding.Property(i => i.Quantity).IsRequired();
        holding.Property(i => i.Equipped).IsRequired();

        // One equipped item per slot is deliberately NOT expressed here: the slot lives in content, so
        // there is no column to build a filtered unique index over. InventoryService.Equip enforces it on
        // write and Loadout refuses to aggregate a violation.

        // Deleting a player must take their holdings with it. No navigation property on Player, for the
        // same reason as mastery: nothing needs to load every holding for a player, and adding one would
        // invite exactly that.
        holding.HasOne<Player>()
            .WithMany()
            .HasForeignKey(i => i.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-player, per-property income holdings.
        var owned = modelBuilder.Entity<PlayerProperty>();

        // Composite natural key, same reasoning as the holdings above: a player holds exactly one row
        // per property — a second unit raises Quantity rather than inserting again. Two rows would each
        // accrue income against their own clock and the player would collect twice.
        owned.HasKey(p => new { p.PlayerId, p.PropertyId });

        // PropertyId is a content id, not a foreign key — the catalog lives in properties.json, not the
        // database. The 64-character cap is enforced up front by ContentPackLoader.ValidateProperties.
        owned.Property(p => p.PropertyId).IsRequired().HasMaxLength(64);
        owned.Property(p => p.Quantity).IsRequired();
        owned.Property(p => p.UncollectedCash).IsRequired();

        // Income is derived from the gap between this instant and now, so it is load-bearing arithmetic
        // rather than an audit field. timestamptz (Npgsql's default for DateTimeOffset) keeps
        // microseconds, which is finer than any rate content can express.
        owned.Property(p => p.AccruedAtUtc).IsRequired();

        // Deleting a player must take their properties with them. No navigation property on Player, for
        // the same reason as the holdings above.
        owned.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-player, per-day, per-objective progress.
        var daily = modelBuilder.Entity<PlayerDailyObjective>();

        // Composite natural key: one row per objective per day, so a double-credit or double-claim is a
        // schema-level impossibility rather than something the service has to remember. A player has no
        // rows until their first action of the day, which stores one per offered objective.
        daily.HasKey(d => new { d.PlayerId, d.UtcDate, d.ObjectiveId });

        // ObjectiveId is a content id, not a foreign key. The cap is enforced by the content loader.
        daily.Property(d => d.ObjectiveId).IsRequired().HasMaxLength(64);
        daily.Property(d => d.Progress).IsRequired();
        daily.Property(d => d.Claimed).IsRequired();
        daily.Property(d => d.JobsWon).IsRequired().HasMaxLength(1024);

        // Optimistic concurrency on the row itself, as on Player. A claim also updates the player (cash),
        // which already serialises it, but the row guarding itself means a double-claim cannot pay twice
        // even if a future reward ever stops touching the player.
        daily.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Deleting a player takes their progress with them. No navigation property on Player.
        daily.HasOne<Player>()
            .WithMany()
            .HasForeignKey(d => d.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // The player's single running appointment, if any.
        var timedJob = modelBuilder.Entity<PlayerTimedJob>();

        // Simple key on PlayerId alone — at most one slot per player at Tier 1, unlike the
        // per-job composite key on PlayerJobMastery.
        timedJob.HasKey(t => t.PlayerId);

        // TimedJobId is a content id, not a foreign key — the catalog lives in timed-jobs.json,
        // not the database.
        timedJob.Property(t => t.TimedJobId).IsRequired().HasMaxLength(64);
        timedJob.Property(t => t.StartedAtUtc).IsRequired();

        // No dedicated xmin, unlike PlayerDailyObjective: start and collect both always write
        // Players (energy or cash/XP) in the same SaveChangesAsync, so that row's own xmin
        // already serializes two concurrent requests by the same player — the same reasoning
        // that keeps PlayerItem and PlayerProperty without one of their own.

        // Deleting a player takes their running appointment with them. No navigation property on
        // Player, same reasoning as every other child entity above.
        timedJob.HasOne<Player>()
            .WithMany()
            .HasForeignKey(t => t.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
