using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var player = modelBuilder.Entity<Player>();
        player.HasKey(p => p.Id);
        player.Property(p => p.CreatedAtUtc).IsRequired();

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
    }
}
