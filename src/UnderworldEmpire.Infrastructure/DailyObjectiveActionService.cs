using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why an objective request could not be carried out, beyond the domain's own refusals.</summary>
public enum ObjectiveActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>No objective in the active content pack carries the requested id.</summary>
    UnknownObjective,

    /// <summary>A concurrent request modified the same player or row first. Surface as a conflict; do not retry.</summary>
    ConcurrencyConflict
}

/// <summary>The full result of a claim: a transport-level error, a domain refusal, or a payout.</summary>
/// <param name="Error">Lookup or conflict problem, or <see cref="ObjectiveActionError.None"/>.</param>
/// <param name="Failure">Domain refusal, or <see cref="ObjectiveFailure.None"/>.</param>
/// <param name="Claimed">The objective that was paid. Null unless the claim was applied.</param>
/// <param name="Player">The player after the payout. Null unless the claim was applied.</param>
/// <param name="LevelUp">What the XP reward did to the level. Null unless the claim was applied.</param>
/// <param name="Objectives">Today's objectives after the claim. Null unless the claim was applied.</param>
public record ObjectiveClaimResult(
    ObjectiveActionError Error,
    ObjectiveFailure Failure,
    Objective? Claimed = null,
    Player? Player = null,
    LevelUpOutcome? LevelUp = null,
    IReadOnlyList<ObjectiveState>? Objectives = null)
{
    /// <summary>Whether the claim was applied and persisted.</summary>
    public bool WasApplied => Error == ObjectiveActionError.None && Failure == ObjectiveFailure.None;
}

/// <summary>
/// Application service for daily objectives: shows a player their day, credits progress on behalf of the
/// job and property services, and pays out claims.
/// </summary>
/// <remarks>
/// <para>
/// <b>Credit joins the caller's transaction.</b> <see cref="CreditAsync"/> only stages changes on the
/// shared <see cref="GameDbContext"/>; it never saves. The job or property service that called it saves
/// once, so a job cannot pay without crediting its objective, and a retry cannot credit twice. That
/// service's existing <c>xmin</c> guard on the player row is what serialises two concurrent attempts.
/// </para>
/// <para>
/// <b>A claim is guarded twice.</b> It pays the player (the player row's <c>xmin</c> fires for the
/// second of two concurrent claims) and flips the progress row's <c>Claimed</c> flag (that row's own
/// <c>xmin</c> fires too). Either is enough to pay exactly once.
/// </para>
/// <para>
/// <b>Reads write nothing, and the first action fixes the day.</b> <see cref="ViewAsync"/> is one query for
/// the day's rows. Until the player acts, the offered set is computed, not stored. The first applied
/// action (a job won or lost, a collection) stores a row per offered objective, computed at the level the
/// player held before it; from then on the day's set is those rows, so a level-up cannot change it.
/// </para>
/// </remarks>
public class DailyObjectiveActionService
{
    private readonly GameDbContext _db;
    private readonly DailyObjectiveService _rules;
    private readonly IContentPackProvider _content;

    public DailyObjectiveActionService(GameDbContext db, DailyObjectiveService rules, IContentPackProvider content)
    {
        _db = db;
        _rules = rules;
        _content = content;
    }

    /// <summary>Today's objectives for a player at <paramref name="level"/>, with stored progress. Writes nothing.</summary>
    public async Task<IReadOnlyList<ObjectiveState>> ViewAsync(
        Guid playerId, int level, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var day = UtcDay.Of(nowUtc);
        var rows = await _db.PlayerDailyObjectives
            .AsNoTracking()
            .Where(d => d.PlayerId == playerId && d.UtcDate == day)
            .ToDictionaryAsync(d => d.ObjectiveId, StringComparer.Ordinal, cancellationToken);

        return _rules.View(_content.ObjectiveCatalog.Objectives, Offered(playerId, day, level), rows);
    }

    /// <summary>
    /// Counts <paramref name="e"/> toward the player's objectives and stages the change on the shared
    /// context. Does <b>not</b> save: the calling action service saves once, in its own transaction.
    /// </summary>
    /// <param name="player">The player, already carrying the action's own changes.</param>
    /// <param name="levelBefore">
    ///     The level held <i>before</i> the action. If this action is the day's first, the set is fixed
    ///     from it, not from the level a level-up caused by this very action would give.
    /// </param>
    /// <returns>Today's objectives after crediting, for the caller to return to the client.</returns>
    public async Task<IReadOnlyList<ObjectiveState>> CreditAsync(
        Player player, int levelBefore, DateTimeOffset nowUtc, ObjectiveEvent? e,
        CancellationToken cancellationToken = default)
    {
        var day = UtcDay.Of(nowUtc);
        var rows = await LoadRowsAsync(player.Id, day, cancellationToken);
        var pool = _content.ObjectiveCatalog.Objectives;

        // The first applied action of the day fixes the set, at the level held before it.
        _db.PlayerDailyObjectives.AddRange(
            _rules.Lock(Offered(player.Id, day, levelBefore), rows, player.Id, day));

        // A null event is an action that advanced nothing (a failed job): it still fixes the day and
        // the caller still wants the view, but no progress moves.
        if (e is { } fact)
            _rules.Credit(pool, rows, fact);

        return _rules.View(pool, Offered(player.Id, day, player.Level), rows);
    }

    /// <summary>Pays out <paramref name="objectiveId"/> if it is complete and not yet claimed today.</summary>
    public async Task<ObjectiveClaimResult> ClaimAsync(
        Guid playerId, string objectiveId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var objective = _content.ObjectiveCatalog.Find(objectiveId);
        if (objective is null)
            return new ObjectiveClaimResult(ObjectiveActionError.UnknownObjective, ObjectiveFailure.None);

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new ObjectiveClaimResult(ObjectiveActionError.UnknownPlayer, ObjectiveFailure.None);

        var day = UtcDay.Of(nowUtc);
        var rows = await LoadRowsAsync(playerId, day, cancellationToken);
        rows.TryGetValue(objectiveId, out var row);

        // Once an action has fixed the day, the set is the stored rows. Before that nothing can be
        // complete, so an offered objective is merely not finished and any other is not today's.
        bool inToday = rows.Count > 0
            ? rows.ContainsKey(objectiveId)
            : Offered(playerId, day, player.Level).Any(o => o.Id == objectiveId);
        if (!inToday)
            return new ObjectiveClaimResult(ObjectiveActionError.None, ObjectiveFailure.NotToday);

        if (row is null)
            return new ObjectiveClaimResult(ObjectiveActionError.None, ObjectiveFailure.NotComplete);

        var failure = row.Claim(objective);
        if (failure != ObjectiveFailure.None)
            return new ObjectiveClaimResult(ObjectiveActionError.None, failure);

        player.AddCash(objective.RewardCash);
        var levelUp = player.ApplyXpAndLevelUp(objective.RewardXp, nowUtc);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ObjectiveClaimResult(ObjectiveActionError.ConcurrencyConflict, ObjectiveFailure.None);
        }

        var view = _rules.View(_content.ObjectiveCatalog.Objectives, Offered(playerId, day, player.Level), rows);
        return new ObjectiveClaimResult(ObjectiveActionError.None, ObjectiveFailure.None, objective, player, levelUp, view);
    }

    private IReadOnlyList<Objective> Offered(Guid playerId, DateOnly day, int level) =>
        _rules.SelectFor(_content.ObjectiveCatalog.Objectives, playerId, day, level);

    private async Task<Dictionary<string, PlayerDailyObjective>> LoadRowsAsync(
        Guid playerId, DateOnly day, CancellationToken cancellationToken) =>
        await _db.PlayerDailyObjectives
            .Where(d => d.PlayerId == playerId && d.UtcDate == day)
            .ToDictionaryAsync(d => d.ObjectiveId, StringComparer.Ordinal, cancellationToken);
}
