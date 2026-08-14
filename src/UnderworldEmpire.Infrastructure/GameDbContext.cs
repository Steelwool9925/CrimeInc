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
    }
}
