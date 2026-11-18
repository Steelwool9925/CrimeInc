namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// One timed-job appointment as offered to the calling player: the content definition plus
/// whether they can start it right now.
/// </summary>
/// <param name="Id">Content id; the route value for POST /timed-jobs/{id}/start.</param>
/// <param name="Name">Player-facing name from the content pack.</param>
/// <param name="Description">Player-facing flavour text from the content pack.</param>
/// <param name="DurationSeconds">How long the appointment runs once started.</param>
/// <param name="EnergyCost">Energy consumed at start.</param>
/// <param name="LevelRequirement">Minimum level required to start.</param>
/// <param name="CashMin">Lowest cash payout, inclusive. Rolled at collect, not at start.</param>
/// <param name="CashMax">Highest cash payout, inclusive.</param>
/// <param name="Xp">Experience granted on collect.</param>
/// <param name="CanStart">
///     Whether the player may start it right now. Computed server-side so the client never
///     reimplements the rule.
/// </param>
/// <param name="UnavailableReason">
///     Why <see cref="CanStart"/> is false — <c>"alreadyRunning"</c>, <c>"level"</c> or
///     <c>"energy"</c> — or <see langword="null"/> when it is true. A stable token, not display
///     text.
/// </param>
public record TimedJobDto(
    string Id,
    string Name,
    string Description,
    int DurationSeconds,
    int EnergyCost,
    int LevelRequirement,
    long CashMin,
    long CashMax,
    long Xp,
    bool CanStart,
    string? UnavailableReason);

/// <summary>The calling player's one running appointment, if any — there is at most one at Tier 1.</summary>
/// <param name="TimedJobId">The appointment running.</param>
/// <param name="Name">Its player-facing name, so the client need not look it up against the catalog.</param>
/// <param name="StartedAtUtc">When it was started.</param>
/// <param name="RemainingSeconds">
///     Seconds until it is ready, floored at zero. A client runs a local countdown from this
///     rather than polling, the same approach <c>meter_card.dart</c> uses for meter regen.
/// </param>
/// <param name="Ready">Whether it can be collected right now.</param>
public record TimedJobRunningDto(
    string TimedJobId,
    string Name,
    DateTimeOffset StartedAtUtc,
    int RemainingSeconds,
    bool Ready);

/// <summary>Response body for GET /timed-jobs.</summary>
/// <param name="TimedJobs">Every appointment in the active pack, in content-declared order.</param>
/// <param name="Running">The player's running appointment, or <see langword="null"/> if their slot is free.</param>
/// <param name="CurrentEnergy">The player's lazily-regenned energy at request time.</param>
/// <param name="Level">The player's level at request time.</param>
public record TimedJobListResponse(
    IReadOnlyList<TimedJobDto> TimedJobs,
    TimedJobRunningDto? Running,
    int CurrentEnergy,
    int Level);

/// <summary>Response body for POST /timed-jobs/{id}/start.</summary>
/// <param name="Running">The newly running appointment.</param>
/// <param name="Player">The player's full state after energy was spent.</param>
public record StartTimedJobResponse(TimedJobRunningDto Running, MeDto Player);

/// <summary>What one collected appointment paid out, as returned by POST /timed-jobs/collect.</summary>
/// <param name="TimedJobId">The appointment that was collected.</param>
/// <param name="CashEarned">Cash rolled server-side from the appointment's inclusive range.</param>
/// <param name="XpEarned">The appointment's XP.</param>
public record TimedJobCollectResultDto(string TimedJobId, long CashEarned, long XpEarned);

/// <summary>Response body for POST /timed-jobs/collect.</summary>
/// <param name="Result">What the collect produced.</param>
/// <param name="Player">The player's full state after the payout, so the client needs no follow-up GET /me.</param>
/// <param name="LevelUp">Level-up detail when the XP grant crossed a threshold; <see langword="null"/> otherwise.</param>
public record CollectTimedJobResponse(TimedJobCollectResultDto Result, MeDto Player, LevelUpDto? LevelUp);
