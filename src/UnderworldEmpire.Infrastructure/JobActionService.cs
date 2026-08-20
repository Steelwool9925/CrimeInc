using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why a do-job request could not be carried out, beyond the domain's own refusals.</summary>
public enum JobActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No job in the active content pack carries the requested id.</summary>
    UnknownJob,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>
    /// A concurrent request modified the same player first. The caller should surface this as a
    /// conflict rather than retrying silently — see the remarks on <see cref="JobActionService"/>.
    /// </summary>
    ConcurrencyConflict
}

/// <summary>The full result of a do-job request: a transport-level error, a domain refusal, or an outcome.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="JobActionError.None"/>.</param>
/// <param name="Failure">Domain refusal reason, or <see cref="JobFailure.None"/>.</param>
/// <param name="Outcome">The outcome when everything succeeded.</param>
/// <param name="Player">
///     The player after the action was applied, so the endpoint can return the post-action
///     projection without re-reading. Null unless the action succeeded.
/// </param>
/// <param name="LevelUp">What the XP grant did to the player's level. Null unless the action succeeded.</param>
public record JobActionResult(
    JobActionError Error,
    JobFailure Failure,
    JobOutcome? Outcome,
    Player? Player = null,
    LevelUpOutcome? LevelUp = null)
{
    /// <summary>Whether the job was actually performed and persisted.</summary>
    public bool Succeeded => Error == JobActionError.None && Failure == JobFailure.None;
}

/// <summary>
/// Application service for performing a job: loads the player and their mastery row, delegates the
/// rules to <see cref="JobService"/>, and persists the whole change in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> Energy, cash, XP and mastery are all written by a single
/// <c>SaveChangesAsync</c>, which EF wraps in one transaction. There is no state where energy was
/// spent but the payout was lost.
/// </para>
/// <para>
/// <b>Concurrency.</b> Atomicity alone would not stop a double-tap: two requests could each read
/// energy 10, each spend 1, and each bank a payout — a lost update. <c>Player</c> maps PostgreSQL's
/// <c>xmin</c> as a concurrency token (see <c>GameDbContext</c>), so the second write fails with
/// <see cref="DbUpdateConcurrencyException"/> and is reported as
/// <see cref="JobActionError.ConcurrencyConflict"/> instead of paying out twice. The conflict is
/// <b>not</b> retried here: a retry would re-roll the payout and re-spend energy, which is precisely
/// the duplicate the token exists to prevent. Refusing is the correct answer, and the client
/// re-reads state.
/// </para>
/// <para>
/// <b>Query count.</b> Two reads per attempt — the player, and that player's single mastery row for
/// this job by its composite primary key. The mastery read is a primary-key seek, not a scan, and
/// the count does not grow with the size of the catalog or the player's history.
/// </para>
/// </remarks>
public class JobActionService
{
    private readonly GameDbContext _db;
    private readonly JobService _jobs;
    private readonly IContentPackProvider _content;

    public JobActionService(GameDbContext db, JobService jobs, IContentPackProvider content)
    {
        _db = db;
        _jobs = jobs;
        _content = content;
    }

    /// <summary>Performs <paramref name="jobId"/> on behalf of <paramref name="playerId"/>.</summary>
    public async Task<JobActionResult> PerformAsync(
        Guid playerId,
        string jobId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var catalog = _content.JobCatalog;

        // Resolve content before touching the database: an unknown job id is a client error and
        // needs no round trip to detect.
        var job = catalog.Find(jobId);
        if (job is null)
            return new JobActionResult(JobActionError.UnknownJob, JobFailure.None, null);

        // Tracked (not AsNoTracking) — this is a write path.
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
            return new JobActionResult(JobActionError.UnknownPlayer, JobFailure.None, null);

        // Primary-key seek on (PlayerId, JobId). FindAsync also short-circuits to the change tracker
        // if this row is already loaded, which matters if a future caller performs two jobs in one
        // unit of work.
        var mastery = await _db.PlayerJobMasteries
            .FindAsync([playerId, jobId], cancellationToken);

        bool isNewMasteryRow = mastery is null;
        if (mastery is null)
        {
            mastery = new PlayerJobMastery { PlayerId = playerId, JobId = jobId };
            _db.PlayerJobMasteries.Add(mastery);
        }

        var result = _jobs.Attempt(
            player,
            job,
            mastery,
            catalog.CompletionsPerRank,
            catalog.MaxRank,
            nowUtc);

        if (!result.Succeeded)
        {
            // The domain refused and mutated nothing. Detach a mastery row we speculatively added so
            // a refusal cannot leave an empty row behind on some later SaveChanges.
            if (isNewMasteryRow)
                _db.Entry(mastery).State = EntityState.Detached;

            return new JobActionResult(JobActionError.None, result.Failure, null);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin token fired: another request updated this player between our read and write.
            return new JobActionResult(JobActionError.ConcurrencyConflict, JobFailure.None, null);
        }
        catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
        {
            // Two concurrent attempts at a job the player has never done before each try to INSERT
            // the same (PlayerId, JobId) mastery row. The loser hits the composite primary key, not
            // the concurrency token — the INSERT fails before the player UPDATE's xmin check can
            // matter. Same outcome, different mechanism, so it reports the same conflict rather than
            // surfacing as a 500.
            return new JobActionResult(JobActionError.ConcurrencyConflict, JobFailure.None, null);
        }

        return new JobActionResult(
            JobActionError.None,
            JobFailure.None,
            result.Outcome,
            player,
            result.LevelUp);
    }

}
