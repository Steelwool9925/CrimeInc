namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// A player's mastery standing on one job, as returned by GET /jobs.
/// </summary>
/// <param name="Completions">Times this player has completed the job.</param>
/// <param name="Rank">Current mastery rank, starting at 0.</param>
/// <param name="MaxRank">Rank ceiling, supplied by the content pack.</param>
/// <param name="CompletionsUntilNextRank">
///     Completions still needed for the next rank, or <see langword="null"/> at max rank. Drives the
///     client's mastery bar without the client needing to know the threshold rule.
/// </param>
public record JobMasteryState(int Completions, int Rank, int MaxRank, int? CompletionsUntilNextRank);

/// <summary>
/// One job as offered to the calling player: the content definition plus that player's mastery and
/// whether they can attempt it right now.
/// </summary>
/// <param name="Id">Content id; the route value for POST /jobs/{id}/do.</param>
/// <param name="Name">Player-facing name from the content pack.</param>
/// <param name="Description">Player-facing flavour text from the content pack.</param>
/// <param name="Area">Presentational grouping key.</param>
/// <param name="EnergyCost">Energy consumed per attempt.</param>
/// <param name="LevelRequirement">Minimum level required to attempt.</param>
/// <param name="CashMin">Lowest cash payout, inclusive.</param>
/// <param name="CashMax">Highest cash payout, inclusive.</param>
/// <param name="Xp">Experience granted per completion.</param>
/// <param name="Mastery">This player's standing on this job.</param>
/// <param name="CanAttempt">
///     Whether the player may attempt it at this instant, given their level and lazily-regenned
///     energy. Computed server-side so the client never re-implements the rule.
/// </param>
/// <param name="UnavailableReason">
///     Why <paramref name="CanAttempt"/> is false — <c>"level"</c> or <c>"energy"</c> — or
///     <see langword="null"/> when it is true. A stable token, not display text: the label is the
///     client's to localise.
/// </param>
public record JobDto(
    string Id,
    string Name,
    string Description,
    string Area,
    int EnergyCost,
    int LevelRequirement,
    long CashMin,
    long CashMax,
    long Xp,
    JobMasteryState Mastery,
    bool CanAttempt,
    string? UnavailableReason);

/// <summary>
/// What one successful job attempt produced, as returned by POST /jobs/{id}/do.
/// </summary>
/// <param name="JobId">The job performed.</param>
/// <param name="EnergySpent">Energy deducted.</param>
/// <param name="CashEarned">Cash rolled server-side from the job's inclusive range.</param>
/// <param name="XpEarned">Experience granted.</param>
/// <param name="Mastery">Mastery standing after this attempt.</param>
/// <param name="MasteryRankedUp">Whether this attempt crossed a mastery rank threshold.</param>
public record JobResultDto(
    string JobId,
    int EnergySpent,
    long CashEarned,
    long XpEarned,
    JobMasteryState Mastery,
    bool MasteryRankedUp);

/// <summary>Response body for GET /jobs.</summary>
/// <param name="Jobs">Every job in the active pack, in content-declared order.</param>
/// <param name="CurrentEnergy">
///     The player's lazily-regenned energy at request time, so the client can render affordability
///     without a second call to /me.
/// </param>
/// <param name="Level">The player's level at request time, for the same reason.</param>
public record JobListResponse(IReadOnlyList<JobDto> Jobs, int CurrentEnergy, int Level);

/// <summary>Response body for POST /jobs/{id}/do.</summary>
/// <remarks>
/// Carries the complete post-action player projection alongside the result, so the client does not
/// follow every job with a GET /me. At one tap per second that halves the request volume this loop
/// generates, and it removes the window where the UI shows a payout against stale meters.
/// </remarks>
/// <param name="Result">What the attempt produced.</param>
/// <param name="Player">The player's full state after the attempt was applied.</param>
/// <param name="LevelUp">
///     Level-up detail when this attempt crossed one or more XP thresholds; <see langword="null"/>
///     otherwise.
/// </param>
public record DoJobResponse(JobResultDto Result, MeDto Player, LevelUpDto? LevelUp);
