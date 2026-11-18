using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Domain.Timed;

/// <summary>What one collected appointment paid out.</summary>
/// <param name="TimedJobId">The appointment that was collected.</param>
/// <param name="CashEarned">Cash rolled from the job's inclusive range at collect time.</param>
/// <param name="XpEarned">The job's XP. Fixed, not rolled.</param>
public record TimedJobOutcome(string TimedJobId, long CashEarned, long XpEarned);

/// <summary>The result of asking to start an appointment: either a refusal with a reason, or the new running row.</summary>
public sealed record TimedJobStartResult
{
    private TimedJobStartResult(TimedJobFailure failure, PlayerTimedJob? running)
    {
        Failure = failure;
        Running = running;
    }

    /// <summary>The refusal reason, or <see cref="TimedJobFailure.None"/> on success.</summary>
    public TimedJobFailure Failure { get; }

    /// <summary>The newly running appointment; <see langword="null"/> on refusal.</summary>
    public PlayerTimedJob? Running { get; }

    /// <summary>Whether the start was applied — that is, not refused at a gate.</summary>
    public bool WasApplied => Failure == TimedJobFailure.None;

    /// <summary>Builds a refusal. Nothing was spent.</summary>
    public static TimedJobStartResult Refused(TimedJobFailure failure)
    {
        if (failure == TimedJobFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new TimedJobStartResult(failure, null);
    }

    /// <summary>Builds a started appointment. Energy was spent.</summary>
    public static TimedJobStartResult Applied(PlayerTimedJob running) =>
        new(TimedJobFailure.None, running);
}

/// <summary>The result of asking to collect the running appointment: either a refusal, or a payout.</summary>
public sealed record TimedJobCollectResult
{
    private TimedJobCollectResult(TimedJobFailure failure, TimedJobOutcome? outcome, LevelUpOutcome? levelUp)
    {
        Failure = failure;
        Outcome = outcome;
        LevelUp = levelUp;
    }

    /// <summary>The refusal reason, or <see cref="TimedJobFailure.None"/> on success.</summary>
    public TimedJobFailure Failure { get; }

    /// <summary>What the collect produced; <see langword="null"/> on refusal.</summary>
    public TimedJobOutcome? Outcome { get; }

    /// <summary>What the XP grant did to the player's level. Non-null whenever the collect was applied.</summary>
    public LevelUpOutcome? LevelUp { get; }

    /// <summary>Whether the collect was applied — that is, not refused.</summary>
    public bool WasApplied => Failure == TimedJobFailure.None;

    /// <summary>Builds a refusal. Nothing was paid, and the running row is untouched.</summary>
    public static TimedJobCollectResult Refused(TimedJobFailure failure)
    {
        if (failure == TimedJobFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new TimedJobCollectResult(failure, null, null);
    }

    /// <summary>Builds a collected payout. The running row is cleared by the caller.</summary>
    public static TimedJobCollectResult Applied(TimedJobOutcome outcome, LevelUpOutcome levelUp) =>
        new(TimedJobFailure.None, outcome, levelUp);
}

/// <summary>
/// The rules of starting and collecting a timed job (an "appointment"): what is allowed, what it
/// costs, and what it pays. Pure game logic with no I/O — it mutates the objects handed to it and
/// returns what happened. Persisting the result is the caller's job.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Jobs.JobService"/>, split into two methods because start produces no payout
/// to report while collect does — one shared result type would carry fields that are always null
/// on one side or the other.
/// </remarks>
public sealed class TimedJobService
{
    private readonly IRandomSource _random;

    public TimedJobService(IRandomSource random)
    {
        _random = random;
    }

    /// <summary>
    /// Starts <paramref name="job"/> for <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// Refusal order is <c>alreadyRunning -&gt; level -&gt; energy</c>: a slot already occupied is
    /// checked first since it is the one reason that blocks starting anything at all, ahead of
    /// reasons specific to this one appointment. Nothing is mutated on a refusal.
    /// </remarks>
    /// <param name="player">The acting player; mutated (energy spent) only when the start is applied.</param>
    /// <param name="job">The appointment definition from content.</param>
    /// <param name="existing">
    ///     The player's current running appointment, or <see langword="null"/> if their one slot is
    ///     free. Non-null refuses the start regardless of what it is for — there is exactly one slot
    ///     at Tier 1.
    /// </param>
    /// <param name="nowUtc">The instant to evaluate energy regeneration against, and the start timestamp on success.</param>
    public TimedJobStartResult Start(
        Player player,
        TimedJob job,
        PlayerTimedJob? existing,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(job);

        if (existing is not null)
            return TimedJobStartResult.Refused(TimedJobFailure.AlreadyRunning);

        if (player.Level < job.LevelRequirement)
            return TimedJobStartResult.Refused(TimedJobFailure.LevelTooLow);

        int currentEnergy = player.Energy.CurrentAt(nowUtc, MeterRates.Energy);
        if (currentEnergy < job.EnergyCost)
            return TimedJobStartResult.Refused(TimedJobFailure.InsufficientEnergy);

        // SpendAt re-resolves the current value itself and resets the regen clock, same as a
        // normal job's energy spend.
        player.Energy = player.Energy.SpendAt(job.EnergyCost, nowUtc, MeterRates.Energy);

        return TimedJobStartResult.Applied(new PlayerTimedJob
        {
            PlayerId = player.Id,
            TimedJobId = job.Id,
            StartedAtUtc = nowUtc
        });
    }

    /// <summary>
    /// Collects the player's running appointment, if one exists and is ready.
    /// </summary>
    /// <remarks>
    /// Tier 1 always succeeds — "rolled at collect" means the cash amount within
    /// <c>[CashMin, CashMax]</c> is rolled now, not a pass/fail chance. There is no health cost and
    /// nothing to consume; every appointment that reaches here pays.
    /// </remarks>
    /// <param name="player">The acting player; mutated (cash and XP granted) only when the collect is applied.</param>
    /// <param name="job">
    ///     The content definition for <paramref name="running"/>'s appointment, or
    ///     <see langword="null"/> when nothing is running — the caller has nothing to resolve a job
    ///     from in that case, and the result is the same refusal either way.
    /// </param>
    /// <param name="running">The player's current running appointment, or <see langword="null"/> if none.</param>
    /// <param name="nowUtc">The instant to evaluate readiness against.</param>
    public TimedJobCollectResult Collect(
        Player player,
        TimedJob? job,
        PlayerTimedJob? running,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (running is null || job is null || !running.IsReadyAt(nowUtc, TimeSpan.FromSeconds(job.DurationSeconds)))
            return TimedJobCollectResult.Refused(TimedJobFailure.NotReady);

        long cashEarned = _random.NextInclusive(job.CashMin, job.CashMax);
        long xpEarned = job.Xp;

        player.AddCash(cashEarned);

        // Same ordering reason as JobService.Attempt: XP (and the level-up/meter-refill it can
        // trigger) is applied last so the caller persists everything from this one attempt together.
        var levelUp = player.ApplyXpAndLevelUp(xpEarned, nowUtc);

        return TimedJobCollectResult.Applied(new TimedJobOutcome(job.Id, cashEarned, xpEarned), levelUp);
    }
}
