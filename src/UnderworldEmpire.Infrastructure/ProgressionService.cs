using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why a skill allocation could not be applied.</summary>
public enum SkillAllocationError
{
    /// <summary>The point was spent.</summary>
    None = 0,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>The player has no unspent skill points.</summary>
    NoSkillPoints,

    /// <summary>A concurrent request modified the same player first.</summary>
    ConcurrencyConflict
}

/// <summary>
/// Application service for spending skill points. Same shape as
/// <see cref="EconomyService"/>: load, delegate the rule to the domain, persist.
/// </summary>
/// <remarks>
/// Carries the same <c>xmin</c> concurrency protection as <see cref="JobActionService"/>. Without it
/// two concurrent allocations could each read <c>SkillPoints = 1</c> and each award a stat point,
/// spending one point twice.
/// </remarks>
public class ProgressionService
{
    private readonly GameDbContext _db;

    public ProgressionService(GameDbContext db)
    {
        _db = db;
    }

    /// <summary>Spends one skill point for <paramref name="playerId"/> on <paramref name="target"/>.</summary>
    /// <returns>The error, and the updated player when the error is <see cref="SkillAllocationError.None"/>.</returns>
    public async Task<(SkillAllocationError Error, Player? Player)> AllocateAsync(
        Guid playerId,
        SkillTarget target,
        CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
            return (SkillAllocationError.UnknownPlayer, null);

        // The domain throws when no points remain; that is an expected client condition here rather
        // than a fault, so it is translated into a typed error instead of propagating.
        if (player.SkillPoints < 1)
            return (SkillAllocationError.NoSkillPoints, null);

        player.SpendSkillPoint(target);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (SkillAllocationError.ConcurrencyConflict, null);
        }

        return (SkillAllocationError.None, player);
    }
}
