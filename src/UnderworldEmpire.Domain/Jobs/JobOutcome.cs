using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// Pack-wide risk rules, applied to every job in the pack.
/// </summary>
/// <remarks>
/// Passed into <see cref="JobService.Attempt"/> rather than read from a catalog, the same seam the
/// mastery thresholds use: the catalog lives in infrastructure, and the domain stays free of
/// content loading.
/// </remarks>
/// <param name="MaxSuccessChance">Ceiling on any job's success chance. No job is ever certain.</param>
/// <param name="ConsolationCashFraction">Fraction of the rolled cash paid on a failed attempt.</param>
/// <param name="ConsolationXpFraction">Fraction of the job's XP granted on a failed attempt.</param>
public readonly record struct JobRiskRules(
    double MaxSuccessChance,
    double ConsolationCashFraction,
    double ConsolationXpFraction);

/// <summary>
/// What one job attempt spent from one item requirement — always 1 unit for Tier 1's fixed rate,
/// win or lose.
/// </summary>
/// <param name="ItemId">The item consumed.</param>
/// <param name="QuantityConsumed">Copies spent this attempt. Always 1 in this first pass.</param>
/// <param name="QuantityRemaining">Copies left in the holding after this attempt.</param>
public readonly record struct ItemConsumption(string ItemId, int QuantityConsumed, int QuantityRemaining);

/// <summary>
/// What one <b>applied</b> job attempt produced — successful or not. Every figure is the server's
/// decision; the client receives this, it never proposes it.
/// </summary>
/// <remarks>
/// Since M3R this describes a failed attempt as well as a successful one. A failure is not a
/// refusal: the energy was spent and the player's state changed, so there is an outcome to report.
/// The fields differ in what they carry rather than whether they are present — on a failure
/// <paramref name="CashEarned"/> and <paramref name="XpEarned"/> hold the consolation fractions,
/// <paramref name="Completions"/> is unchanged because mastery does not advance, and
/// <paramref name="HealthLost"/> is the only field that can be non-zero.
/// </remarks>
/// <param name="JobId">The job that was attempted.</param>
/// <param name="Succeeded">Whether the attempt succeeded. False on a failed-but-applied attempt.</param>
/// <param name="EnergySpent">Energy deducted, equal to the job's cost. Spent on both branches.</param>
/// <param name="CashEarned">
///     Cash rolled from the job's inclusive range on a success, or the consolation fraction of that
///     same roll on a failure.
/// </param>
/// <param name="XpEarned">
///     The job's XP on a success, or the consolation fraction of it on a failure.
/// </param>
/// <param name="HealthLost">Health deducted; always 0 on a success.</param>
/// <param name="Completions">The player's total completions of this job after this attempt.</param>
/// <param name="MasteryRank">Mastery rank after this attempt.</param>
/// <param name="MasteryRankedUp">Whether this attempt crossed a mastery rank threshold.</param>
/// <param name="CompletionsUntilNextRank">
///     Completions still needed for the next rank, or <see langword="null"/> at max rank.
/// </param>
/// <param name="ItemsConsumed">
///     One entry per item requirement the job gates on, spent on this attempt — win or lose. Empty
///     for the jobs that opt into no item gate, which is every job shipped before this field existed.
/// </param>
public record JobOutcome(
    string JobId,
    bool Succeeded,
    int EnergySpent,
    long CashEarned,
    long XpEarned,
    int HealthLost,
    int Completions,
    int MasteryRank,
    bool MasteryRankedUp,
    int? CompletionsUntilNextRank,
    IReadOnlyList<ItemConsumption> ItemsConsumed);

/// <summary>
/// The result of asking to perform a job: either a refusal with a reason, or an outcome.
/// </summary>
/// <remarks>
/// Modelled as one type with a <see cref="Failure"/> discriminator rather than by throwing, because
/// "not enough energy" is an ordinary, expected answer in this game loop — a player taps a job they
/// cannot afford constantly. Exceptions are reserved for genuine faults.
/// </remarks>
public sealed record JobAttemptResult
{
    private JobAttemptResult(JobFailure failure, JobOutcome? outcome, LevelUpOutcome? levelUp)
    {
        Failure = failure;
        Outcome = outcome;
        LevelUp = levelUp;
    }

    /// <summary>The refusal reason, or <see cref="JobFailure.None"/> on success.</summary>
    public JobFailure Failure { get; }

    /// <summary>What happened if the attempt was applied; <see langword="null"/> on refusal.</summary>
    public JobOutcome? Outcome { get; }

    /// <summary>
    /// What the XP grant did to the player's level. Non-null whenever the attempt was applied —
    /// check <see cref="LevelUpOutcome.LeveledUp"/> to see whether a threshold was actually crossed.
    /// A failed attempt still grants consolation XP, so it can still level a player.
    /// </summary>
    public LevelUpOutcome? LevelUp { get; }

    /// <summary>
    /// Whether the attempt was <b>applied</b> — that is, not refused at a gate.
    /// </summary>
    /// <remarks>
    /// Deliberately not called <c>Succeeded</c>. Since M3R those are different questions: an applied
    /// attempt may still have failed its roll, and the two have different answers on the wire — a
    /// refusal is a 409 that spent nothing, while a failed attempt is a 200 that spent energy. Ask
    /// <see cref="JobOutcome.Succeeded"/> for whether the job itself came off.
    /// </remarks>
    public bool WasApplied => Failure == JobFailure.None;

    /// <summary>Builds a refusal. Nothing was spent.</summary>
    public static JobAttemptResult Refused(JobFailure failure)
    {
        if (failure == JobFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new JobAttemptResult(failure, null, null);
    }

    /// <summary>
    /// Builds an applied attempt — successful or failed. Both spent energy and changed state.
    /// </summary>
    public static JobAttemptResult Applied(JobOutcome outcome, LevelUpOutcome levelUp) =>
        new(JobFailure.None, outcome, levelUp);
}
