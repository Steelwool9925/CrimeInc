using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
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
/// <param name="Outcome">
///     What the attempt produced, when it was applied. Present for a failed attempt too — ask
///     <see cref="JobOutcome.Succeeded"/> whether the job itself came off.
/// </param>
/// <param name="Player">
///     The player after the action was applied, so the endpoint can return the post-action
///     projection without re-reading. Null unless the action was applied.
/// </param>
/// <param name="LevelUp">
///     What the XP grant did to the player's level. Null unless the action was applied — and a
///     failed attempt still grants consolation XP, so it can still level a player.
/// </param>
public record JobActionResult(
    JobActionError Error,
    JobFailure Failure,
    JobOutcome? Outcome,
    Player? Player = null,
    LevelUpOutcome? LevelUp = null)
{
    /// <summary>
    /// Whether the attempt was applied and persisted — as opposed to refused at a gate.
    /// </summary>
    /// <remarks>
    /// Not <c>Succeeded</c>: since M3R an applied attempt may still have failed its roll, and the
    /// two answer differently on the wire. This predicate is what separates a <c>200</c> from a
    /// <c>409</c>; <see cref="JobOutcome.Succeeded"/> is what separates a win from a loss inside
    /// that <c>200</c>.
    /// </remarks>
    public bool WasApplied => Error == JobActionError.None && Failure == JobFailure.None;
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

        // The stat gate and the odds both read effective stats, so the equipped holdings must be
        // resolved before the attempt. This is the second query a job attempt now costs; it is the
        // price of gear mattering outside combat.
        var loadout = await BuildLoadoutAsync(player, cancellationToken);

        // A third read, but only for a job that opts into an item gate — skipped entirely for the 6
        // jobs with none, so query count only grows where the feature is actually used. Tracked (not
        // AsNoTracking): these rows get decremented below once the attempt is known to be applied.
        List<PlayerItem> trackedHoldings = [];
        IReadOnlyDictionary<string, int>? heldItemQuantities = null;
        if (job.ItemRequirements.Count > 0)
        {
            var itemIds = job.ItemRequirements.Select(r => r.ItemId).ToList();
            trackedHoldings = await _db.PlayerItems
                .Where(h => h.PlayerId == playerId && itemIds.Contains(h.ItemId))
                .ToListAsync(cancellationToken);
            heldItemQuantities = trackedHoldings.ToDictionary(h => h.ItemId, h => h.Quantity);
        }

        var result = _jobs.Attempt(
            player,
            job,
            mastery,
            loadout,
            catalog.CompletionsPerRank,
            catalog.MaxRank,
            new JobRiskRules(
                catalog.MaxSuccessChance,
                catalog.ConsolationCashFraction,
                catalog.ConsolationXpFraction),
            nowUtc,
            heldItemQuantities);

        if (!result.WasApplied)
        {
            // The domain refused and mutated nothing. Detach a mastery row we speculatively added so
            // a refusal cannot leave an empty row behind on some later SaveChanges.
            if (isNewMasteryRow)
                _db.Entry(mastery).State = EntityState.Detached;

            return new JobActionResult(JobActionError.None, result.Failure, null);
        }

        // Every entry in ItemsConsumed names a requirement the gate already proved is held, so the
        // matching tracked row is guaranteed to exist here — the gate cannot pass without it.
        foreach (var consumption in result.Outcome!.ItemsConsumed)
        {
            var holding = trackedHoldings.First(h => h.ItemId == consumption.ItemId);
            holding.Consume(consumption.QuantityConsumed);
        }

        try
        {
            // No new transaction and no new concurrency handling: this decrement rides in the same
            // SaveChangesAsync as Players/PlayerJobMasteries, which already writes Players.Energy on
            // every applied attempt — the existing xmin token on that row already serializes two
            // concurrent attempts by the same player, this child-row write included.
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

    /// <summary>Reads the player's equipped holdings and aggregates them into a <see cref="Loadout"/>.</summary>
    /// <remarks>
    /// The same shape <c>InventoryActionService</c> uses. Duplicated rather than shared because the
    /// two services own different units of work and neither should reach into the other's tracked
    /// entities; if a third caller appears, this is worth lifting.
    /// </remarks>
    private async Task<Loadout> BuildLoadoutAsync(Player player, CancellationToken cancellationToken)
    {
        var equipped = await _db.PlayerItems
            .Where(h => h.PlayerId == player.Id && h.Equipped)
            .ToListAsync(cancellationToken);

        return Loadout.For(player, _content.ItemCatalog.ResolveEquipped(equipped));
    }
}
