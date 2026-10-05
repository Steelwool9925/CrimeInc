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
/// One item-quantity gate on a job, from the calling player's point of view — content plus this
/// player's current holding, so the client never needs a follow-up call to explain a lock.
/// </summary>
/// <param name="ItemId">Content id of the required weapon or armor item.</param>
/// <param name="MinQuantity">Spare copies the job needs. Every attempt uses all of them up, win or lose.</param>
/// <param name="MaxExtras">
/// The most extra copies the player may commit on top of <see cref="MinQuantity"/> to improve the odds;
/// zero when this item cannot be committed beyond the requirement. Each extra is spent as well.
/// </param>
/// <param name="Held">
/// How many <b>spare</b> copies this player has: the stack minus the one they are wearing, since a worn
/// item is never spent.
/// </param>
/// <param name="Met">Whether <see cref="Held"/> meets <see cref="MinQuantity"/>.</param>
public record JobItemRequirementDto(string ItemId, int MinQuantity, int MaxExtras, int Held, bool Met);

/// <summary>
/// The optional request body of POST /jobs/{id}/do. Absent, or with no extras, the attempt uses only
/// what the job requires.
/// </summary>
/// <param name="Extras">
/// Extra copies to commit beyond the requirement, keyed by item id. Each is spent win or lose and adds
/// the job's <c>extraSuccessChance</c> to the odds. The server decides the resulting odds; the client
/// only says how many it is willing to spend.
/// </param>
public record DoJobRequest(Dictionary<string, int>? Extras);

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
/// <param name="AttackRequirement">Minimum effective attack — base plus equipped gear.</param>
/// <param name="DefenseRequirement">Minimum effective defense.</param>
/// <param name="ItemRequirements">
///     Item-quantity gates this job additionally requires, alongside the stat gate. Empty for the
///     jobs that opt into no item gate — every job shipped before this field existed.
/// </param>
/// <param name="SuccessChance">
///     This caller's live chance of success, in <c>[0, 1]</c>, already capped by the pack ceiling.
///     Computed by the same <c>Job.SuccessChanceFor</c> the attempt rolls against, so the odds shown
///     on a row cannot drift from the odds it actually gets. Reported even for a job the caller
///     cannot attempt, so the client can show what clearing the gate would buy.
/// </param>
/// <param name="UnavailableReason">
///     Why <paramref name="CanAttempt"/> is false — <c>"level"</c>, <c>"stats"</c>, <c>"items"</c> or
///     <c>"energy"</c> — or <see langword="null"/> when it is true. A stable token, not display
///     text: the label is the client's to localise.
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
    int AttackRequirement,
    int DefenseRequirement,
    IReadOnlyList<JobItemRequirementDto> ItemRequirements,
    double SuccessChance,
    double ExtraSuccessChance,
    JobMasteryState Mastery,
    bool CanAttempt,
    string? UnavailableReason);

/// <summary>
/// What one <b>applied</b> job attempt produced, as returned by POST /jobs/{id}/do — successful or
/// not.
/// </summary>
/// <remarks>
/// A failed attempt arrives here, as a <c>200</c>, not as a <c>409</c>. It is an outcome rather than
/// a refusal: energy was spent and the player's state changed, so there is something to report.
/// <c>409</c> stays reserved for the three gates — <c>level</c>, <c>stats</c>, <c>energy</c> — which
/// spend nothing at all.
/// </remarks>
/// <param name="JobId">The job attempted.</param>
/// <param name="Succeeded">Whether the attempt came off. False on a failed-but-applied attempt.</param>
/// <param name="EnergySpent">Energy deducted. Spent on both branches.</param>
/// <param name="CashEarned">
///     Cash rolled server-side from the job's inclusive range, or the pack's consolation fraction of
///     that same roll on a failure.
/// </param>
/// <param name="XpEarned">The job's XP, or the consolation fraction of it on a failure.</param>
/// <param name="HealthLost">Health deducted; always 0 on a success.</param>
/// <param name="Mastery">
///     Mastery standing after this attempt. Unchanged on a failure — mastery counts completions, not
///     attempts.
/// </param>
/// <param name="MasteryRankedUp">Whether this attempt crossed a mastery rank threshold.</param>
/// <param name="ItemsConsumed">
///     One entry per item requirement the job gates on, spent on this attempt — win or lose. Empty
///     for the jobs that opt into no item gate.
/// </param>
public record JobResultDto(
    string JobId,
    bool Succeeded,
    int EnergySpent,
    long CashEarned,
    long XpEarned,
    int HealthLost,
    JobMasteryState Mastery,
    bool MasteryRankedUp,
    IReadOnlyList<ItemConsumptionDto> ItemsConsumed);

/// <summary>One item requirement's consumption from one applied job attempt, as returned on the wire.</summary>
/// <param name="ItemId">The item consumed.</param>
/// <param name="QuantityConsumed">Copies spent this attempt. Always 1 in this first pass.</param>
/// <param name="QuantityRemaining">Copies left in the holding after this attempt.</param>
public record ItemConsumptionDto(string ItemId, int QuantityConsumed, int QuantityRemaining);

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
