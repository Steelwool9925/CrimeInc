using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// What one successful job attempt produced. Every figure is the server's decision — the client
/// receives this, it never proposes it.
/// </summary>
/// <param name="JobId">The job that was completed.</param>
/// <param name="EnergySpent">Energy deducted, equal to the job's cost.</param>
/// <param name="CashEarned">Cash rolled from the job's inclusive range.</param>
/// <param name="XpEarned">Experience granted; fixed per job, not rolled.</param>
/// <param name="Completions">The player's total completions of this job after this attempt.</param>
/// <param name="MasteryRank">Mastery rank after this attempt.</param>
/// <param name="MasteryRankedUp">Whether this attempt crossed a mastery rank threshold.</param>
/// <param name="CompletionsUntilNextRank">
///     Completions still needed for the next rank, or <see langword="null"/> at max rank.
/// </param>
public record JobOutcome(
    string JobId,
    int EnergySpent,
    long CashEarned,
    long XpEarned,
    int Completions,
    int MasteryRank,
    bool MasteryRankedUp,
    int? CompletionsUntilNextRank);

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

    /// <summary>The outcome on success; <see langword="null"/> on refusal.</summary>
    public JobOutcome? Outcome { get; }

    /// <summary>
    /// What the XP grant did to the player's level. Non-null on success — check
    /// <see cref="LevelUpOutcome.LeveledUp"/> to see whether a threshold was actually crossed.
    /// </summary>
    public LevelUpOutcome? LevelUp { get; }

    /// <summary>Whether the attempt succeeded.</summary>
    public bool Succeeded => Failure == JobFailure.None;

    /// <summary>Builds a refusal.</summary>
    public static JobAttemptResult Refused(JobFailure failure)
    {
        if (failure == JobFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new JobAttemptResult(failure, null, null);
    }

    /// <summary>Builds a success.</summary>
    public static JobAttemptResult Success(JobOutcome outcome, LevelUpOutcome levelUp) =>
        new(JobFailure.None, outcome, levelUp);
}
