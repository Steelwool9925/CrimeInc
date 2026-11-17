using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why a daily request could not be carried out, beyond the domain's own refusal.</summary>
public enum DailyActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>A concurrent request modified the same player or streak row first. Surfaced as a conflict; not retried.</summary>
    ConcurrencyConflict
}

/// <summary>Today's claimable state for a player, for <c>GET /daily</c>. Writes nothing.</summary>
/// <param name="Claimable">Whether a claim right now would be accepted.</param>
/// <param name="CurrentStreak">The player's stored streak, before any claim.</param>
/// <param name="LongestStreak">The player's longest streak ever reached.</param>
/// <param name="NextReward">What claiming right now would pay.</param>
public record DailyStatusResult(bool Claimable, int CurrentStreak, int LongestStreak, DailyReward NextReward);

/// <summary>The full result of a claim: a transport-level error, a domain refusal, or a payout.</summary>
/// <param name="Error">Lookup or conflict problem, or <see cref="DailyActionError.None"/>.</param>
/// <param name="Failure">Domain refusal, or <see cref="DailyFailure.None"/>.</param>
/// <param name="Reward">What was paid. Null unless the claim was applied.</param>
/// <param name="Streak">The player's streak after the claim. Null unless the claim was applied.</param>
/// <param name="Player">The player after the payout. Null unless the claim was applied.</param>
/// <param name="LevelUp">What the XP reward did to the level. Null unless the claim was applied.</param>
public record DailyClaimResult(
    DailyActionError Error,
    DailyFailure Failure,
    DailyReward? Reward = null,
    DailyStreak? Streak = null,
    Player? Player = null,
    LevelUpOutcome? LevelUp = null)
{
    /// <summary>Whether the claim was applied and persisted.</summary>
    public bool WasApplied => Error == DailyActionError.None && Failure == DailyFailure.None;
}

/// <summary>
/// Application service for the daily login streak: reports today's claimable state and pays out a
/// claim, once per UTC day.
/// </summary>
/// <remarks>
/// <para>
/// <b>The streak row is created lazily</b> on a player's first-ever claim, exactly like the
/// holding row in <see cref="PropertyActionService"/>. Two concurrent first-ever claims race to
/// <c>INSERT</c> the same single-column-keyed row; the loser hits the primary key before the
/// player row's <c>xmin</c> check can matter, which is why <see cref="TrySaveAsync"/> catches both
/// the <c>xmin</c> conflict and the unique-violation, the same two-catch shape
/// <see cref="PropertyActionService"/> uses for its own lazily-inserted holding row.
/// </para>
/// <para>
/// <b>Claim pays unconditionally with an energy refill</b>, same as every level-up in
/// <see cref="Player.ApplyXpAndLevelUp"/> — the Tier 1 design: no overflow/compensation cash for a
/// bar that was already full.
/// </para>
/// </remarks>
public class DailyActionService
{
    private readonly GameDbContext _db;
    private readonly DailyRewardService _rewards;
    private readonly IContentPackProvider _content;

    public DailyActionService(GameDbContext db, DailyRewardService rewards, IContentPackProvider content)
    {
        _db = db;
        _rewards = rewards;
        _content = content;
    }

    /// <summary>Today's claimable state for <paramref name="playerId"/>, or <see langword="null"/> if unknown. Writes nothing.</summary>
    public async Task<DailyStatusResult?> ViewAsync(
        Guid playerId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        bool playerExists = await _db.Players.AsNoTracking().AnyAsync(p => p.Id == playerId, cancellationToken);
        if (!playerExists)
            return null;

        var streak = await _db.DailyStreaks
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.PlayerId == playerId, cancellationToken);

        int previewDay = streak?.StreakIfClaimedAt(nowUtc) ?? 1;
        var preview = _rewards.RewardFor(_content.DailyRewardCatalog.Rewards, previewDay);

        return new DailyStatusResult(
            Claimable: streak is null || streak.IsClaimableAt(nowUtc),
            CurrentStreak: streak?.CurrentStreak ?? 0,
            LongestStreak: streak?.LongestStreak ?? 0,
            NextReward: preview);
    }

    /// <summary>Pays out today's reward, exactly once per UTC day.</summary>
    public async Task<DailyClaimResult> ClaimAsync(
        Guid playerId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new DailyClaimResult(DailyActionError.UnknownPlayer, DailyFailure.None);

        var streak = await _db.DailyStreaks.FirstOrDefaultAsync(d => d.PlayerId == playerId, cancellationToken);

        bool isNewStreak = streak is null;
        if (streak is null)
        {
            streak = new DailyStreak { PlayerId = playerId };
            _db.DailyStreaks.Add(streak);
        }

        var failure = streak.Claim(nowUtc);
        if (failure != DailyFailure.None)
        {
            // The domain refused and mutated nothing. Detach a streak row we speculatively added
            // so a refusal cannot leave an empty row behind on some later SaveChanges — mirrors
            // PropertyActionService.PurchaseAsync's handling of a refused first-ever holding.
            if (isNewStreak)
                _db.Entry(streak).State = EntityState.Detached;

            return new DailyClaimResult(DailyActionError.None, failure);
        }

        var reward = _rewards.RewardFor(_content.DailyRewardCatalog.Rewards, streak.CurrentStreak);

        player.AddCash(reward.Cash);
        var levelUp = player.ApplyXpAndLevelUp(reward.Xp, nowUtc);
        // Unconditional refill, even when the XP reward alone did not cross a level — the whole
        // point of a login bonus at this energy cap (PlayerDefaults.EnergyMax) is a free top-up.
        player.Energy = player.Energy.RefillAt(nowUtc);

        if (!await TrySaveAsync(cancellationToken))
            return new DailyClaimResult(DailyActionError.ConcurrencyConflict, DailyFailure.None);

        return new DailyClaimResult(DailyActionError.None, DailyFailure.None, reward, streak, player, levelUp);
    }

    /// <summary>Saves, translating both flavours of write conflict into <see langword="false"/>.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin token fired: another request updated the player or streak row between our
            // read and write.
            return false;
        }
        catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
        {
            // Two concurrent first-ever claims each try to INSERT the same (PlayerId)-keyed streak
            // row. The loser hits the primary key before the player UPDATE's xmin check can matter.
            return false;
        }
    }
}
