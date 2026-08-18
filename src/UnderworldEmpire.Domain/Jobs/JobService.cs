using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// The rules of performing a job: what is allowed, what it costs, and what it pays. Pure game logic
/// with no I/O — it mutates the objects handed to it and returns what happened. Persisting the
/// result is the caller's job.
/// </summary>
/// <remarks>
/// This is where the server's authority over the game loop actually lives. Every figure the player
/// receives is decided here from content plus a server-side roll; nothing is taken from the request.
/// </remarks>
public sealed class JobService
{
    private readonly IRandomSource _random;

    public JobService(IRandomSource random)
    {
        _random = random;
    }

    /// <summary>
    /// Attempts <paramref name="job"/> for <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// On success this mutates <paramref name="player"/> (energy down, cash and XP up) and
    /// <paramref name="mastery"/> (one more completion). On refusal <b>nothing is mutated</b> —
    /// checks run to completion before the first write, so a refused attempt cannot leave a player
    /// half-charged.
    /// </remarks>
    /// <param name="player">The acting player; mutated on success.</param>
    /// <param name="job">The job definition from content.</param>
    /// <param name="mastery">This player's mastery row for this job; mutated on success.</param>
    /// <param name="completionsPerRank">Content-supplied completions per mastery rank.</param>
    /// <param name="maxRank">Content-supplied mastery rank ceiling.</param>
    /// <param name="nowUtc">The instant to evaluate energy regeneration against.</param>
    public JobAttemptResult Attempt(
        Player player,
        Job job,
        PlayerJobMastery mastery,
        int completionsPerRank,
        int maxRank,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mastery);

        // ---- Validate before mutating anything ----

        // Level is checked first so a job the player cannot yet unlock reports the permanent reason
        // rather than "come back when your energy refills". Same precedence as GET /jobs.
        if (player.Level < job.LevelRequirement)
            return JobAttemptResult.Refused(JobFailure.LevelTooLow);

        int currentEnergy = player.Energy.CurrentAt(nowUtc, MeterRates.Energy);
        if (currentEnergy < job.EnergyCost)
            return JobAttemptResult.Refused(JobFailure.InsufficientEnergy);

        // ---- Apply ----

        // SpendAt re-resolves the current value itself and resets the regen clock, so partial
        // progress toward the next point is deliberately forfeited on a spend.
        player.Energy = player.Energy.SpendAt(job.EnergyCost, nowUtc, MeterRates.Energy);

        long cashEarned = _random.NextInclusive(job.CashMin, job.CashMax);
        player.AddCash(cashEarned);

        // Levelling is applied here, inside the same attempt, so the caller persists the level, the
        // granted skill points and the refilled meters in the one SaveChanges as everything else.
        var levelUp = player.ApplyXpAndLevelUp(job.Xp, nowUtc);

        bool rankedUp = mastery.RecordCompletion(completionsPerRank, maxRank);

        return JobAttemptResult.Success(new JobOutcome(
            JobId: job.Id,
            EnergySpent: job.EnergyCost,
            CashEarned: cashEarned,
            XpEarned: job.Xp,
            Completions: mastery.Completions,
            MasteryRank: mastery.Rank,
            MasteryRankedUp: rankedUp,
            CompletionsUntilNextRank: mastery.CompletionsUntilNextRank(completionsPerRank, maxRank)),
            levelUp);
    }
}
