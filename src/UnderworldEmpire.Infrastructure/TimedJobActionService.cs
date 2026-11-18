using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Domain.Timed;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why a timed-job request could not be carried out, beyond the domain's own refusals.</summary>
public enum TimedJobActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No timed job in the active content pack carries the requested id.</summary>
    UnknownTimedJob,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>
    /// A concurrent request modified the same player (or, for start, inserted the same running
    /// row) first. Not retried here — see the remarks on <see cref="TimedJobActionService"/>.
    /// </summary>
    ConcurrencyConflict
}

/// <summary>The full result of a start or collect request: a transport-level error, a domain refusal, or an outcome.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="TimedJobActionError.None"/>.</param>
/// <param name="Failure">Domain refusal reason, or <see cref="TimedJobFailure.None"/>.</param>
/// <param name="Outcome">What a collect produced. Always null for a start, and for any refusal.</param>
/// <param name="Player">The player after the action was applied, so the endpoint can return the post-action projection without re-reading.</param>
/// <param name="LevelUp">What a collect's XP grant did to the player's level. Null unless a collect was applied.</param>
public record TimedJobActionResult(
    TimedJobActionError Error,
    TimedJobFailure Failure,
    TimedJobOutcome? Outcome,
    Player? Player = null,
    LevelUpOutcome? LevelUp = null)
{
    /// <summary>Whether the action was applied and persisted — as opposed to refused.</summary>
    public bool WasApplied => Error == TimedJobActionError.None && Failure == TimedJobFailure.None;
}

/// <summary>
/// Application service for starting and collecting a timed job ("appointment"): loads the
/// player and their running row, delegates the rules to <see cref="TimedJobService"/>, and
/// persists the whole change in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> Mirrors <see cref="JobActionService"/>: energy (on start) or cash/XP (on
/// collect) and the running row are written by a single <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// <b>Concurrency.</b> <c>PlayerTimedJob</c> carries no dedicated concurrency token (see
/// <c>GameDbContext</c>) — both methods always also write <c>Players</c> in the same save, and
/// that row's existing <c>xmin</c> token already serializes two concurrent requests by the same
/// player. A double-collect therefore fails on the player update's concurrency check, not on the
/// deleted row. A double-start that both found no existing row instead races on the
/// <c>PlayerTimedJob</c> primary key insert — the second's <c>INSERT</c> hits the composite
/// primary key (just <c>PlayerId</c>) before the player update's <c>xmin</c> check can matter,
/// caught the same way <see cref="JobActionService"/> catches a duplicate mastery-row insert.
/// Neither conflict is retried: a retry would re-roll the payout or re-spend energy, which is
/// precisely the duplicate the token exists to prevent.
/// </para>
/// </remarks>
public class TimedJobActionService
{
    private readonly GameDbContext _db;
    private readonly TimedJobService _timedJobs;
    private readonly IContentPackProvider _content;

    public TimedJobActionService(GameDbContext db, TimedJobService timedJobs, IContentPackProvider content)
    {
        _db = db;
        _timedJobs = timedJobs;
        _content = content;
    }

    /// <summary>Starts <paramref name="timedJobId"/> for <paramref name="playerId"/>.</summary>
    public async Task<TimedJobActionResult> StartAsync(
        Guid playerId,
        string timedJobId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Resolve content before touching the database: an unknown id is a client error and
        // needs no round trip to detect.
        var job = _content.TimedJobCatalog.Find(timedJobId);
        if (job is null)
            return new TimedJobActionResult(TimedJobActionError.UnknownTimedJob, TimedJobFailure.None, null);

        // Tracked (not AsNoTracking) — this is a write path.
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new TimedJobActionResult(TimedJobActionError.UnknownPlayer, TimedJobFailure.None, null);

        // Primary-key seek on PlayerId — at most one row per player at Tier 1.
        var existing = await _db.PlayerTimedJobs.FindAsync([playerId], cancellationToken);

        var result = _timedJobs.Start(player, job, existing, nowUtc);

        if (!result.WasApplied)
            return new TimedJobActionResult(TimedJobActionError.None, result.Failure, null);

        _db.PlayerTimedJobs.Add(result.Running!);

        try
        {
            // No new transaction and no new concurrency handling: this insert rides in the same
            // SaveChangesAsync as the Players energy spend, which already carries an xmin check.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new TimedJobActionResult(TimedJobActionError.ConcurrencyConflict, TimedJobFailure.None, null);
        }
        catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
        {
            // Two concurrent starts that both read no existing row race on the PlayerTimedJob
            // primary key insert. The loser hits that key, not the player's xmin check.
            return new TimedJobActionResult(TimedJobActionError.ConcurrencyConflict, TimedJobFailure.None, null);
        }

        return new TimedJobActionResult(TimedJobActionError.None, TimedJobFailure.None, null, player);
    }

    /// <summary>Collects <paramref name="playerId"/>'s running appointment, if one is ready.</summary>
    public async Task<TimedJobActionResult> CollectAsync(
        Guid playerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new TimedJobActionResult(TimedJobActionError.UnknownPlayer, TimedJobFailure.None, null);

        var running = await _db.PlayerTimedJobs.FindAsync([playerId], cancellationToken);
        var job = running is not null ? _content.TimedJobCatalog.Find(running.TimedJobId) : null;

        var result = _timedJobs.Collect(player, job, running, nowUtc);

        if (!result.WasApplied)
            return new TimedJobActionResult(TimedJobActionError.None, result.Failure, null);

        // The row is already tracked (FindAsync above), so this stages a DELETE in the same
        // SaveChangesAsync as the player's cash/XP update — one slot, no history needed.
        _db.PlayerTimedJobs.Remove(running!);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request updated this player (plausibly a concurrent collect) between our
            // read and write.
            return new TimedJobActionResult(TimedJobActionError.ConcurrencyConflict, TimedJobFailure.None, null);
        }
        catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
        {
            return new TimedJobActionResult(TimedJobActionError.ConcurrencyConflict, TimedJobFailure.None, null);
        }

        return new TimedJobActionResult(
            TimedJobActionError.None, TimedJobFailure.None, result.Outcome, player, result.LevelUp);
    }
}
