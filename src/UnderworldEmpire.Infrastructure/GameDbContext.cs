using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Jobs;

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
    }
}
