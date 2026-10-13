using System.Collections.Frozen;
using UnderworldEmpire.Domain.Jobs;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated job catalog: every <see cref="Job"/> in the active pack, plus the
/// pack-wide mastery thresholds. Immutable and shared as a singleton for the process lifetime.
/// </summary>
/// <remarks>
/// Lookups go through a <see cref="FrozenDictionary{TKey,TValue}"/> rather than scanning a list.
/// Both hot paths — <c>GET /jobs</c> and every do-job — resolve a job by id, so an O(n) scan would
/// sit directly in the request path for no reason. <c>FrozenDictionary</c> is the right shape for a
/// collection built once at startup and then only read: it pays a higher construction cost for
/// faster reads, which is exactly this lifecycle.
/// </remarks>
public sealed class JobCatalog
{
    private readonly FrozenDictionary<string, Job> _byId;

    /// <param name="jobs">Validated jobs, in the order content declared them.</param>
    /// <param name="completionsPerRank">Completions required per mastery rank.</param>
    /// <param name="maxRank">Mastery rank ceiling.</param>
    /// <param name="masteryPayoutBonusPerRank">Fraction added to cash and XP per mastery rank held.</param>
    /// <param name="levelPayoutBonusPerLevel">Fraction added to cash and XP per level held past the first.</param>
    /// <param name="maxSuccessChance">Pack-wide ceiling on any job's success chance.</param>
    /// <param name="consolationCashFraction">Fraction of the rolled cash paid on a failed attempt.</param>
    /// <param name="consolationXpFraction">Fraction of the job's XP granted on a failed attempt.</param>
    public JobCatalog(
        IReadOnlyList<Job> jobs,
        int completionsPerRank,
        int maxRank,
        double masteryPayoutBonusPerRank,
        double levelPayoutBonusPerLevel,
        double maxSuccessChance,
        double consolationCashFraction,
        double consolationXpFraction)
    {
        Jobs = jobs;
        CompletionsPerRank = completionsPerRank;
        MaxRank = maxRank;
        MasteryPayoutBonusPerRank = masteryPayoutBonusPerRank;
        LevelPayoutBonusPerLevel = levelPayoutBonusPerLevel;
        MaxSuccessChance = maxSuccessChance;
        ConsolationCashFraction = consolationCashFraction;
        ConsolationXpFraction = consolationXpFraction;
        _byId = jobs.ToFrozenDictionary(j => j.Id, StringComparer.Ordinal);
    }

    /// <summary>Every job, in content-declared order. Content controls display order; the engine does not sort.</summary>
    public IReadOnlyList<Job> Jobs { get; }

    /// <summary>Completions required to gain one mastery rank.</summary>
    public int CompletionsPerRank { get; }

    /// <summary>Highest attainable mastery rank.</summary>
    public int MaxRank { get; }

    /// <summary>Fraction added to a job's cash and XP for each mastery rank held; 0 when the pack sets none.</summary>
    public double MasteryPayoutBonusPerRank { get; }

    /// <summary>Fraction added to a job's cash and XP per level held past the first; 0 when the pack sets none.</summary>
    public double LevelPayoutBonusPerLevel { get; }

    /// <summary>
    /// Ceiling on any job's success chance. No job is ever a certainty, however much gear is worn.
    /// </summary>
    /// <remarks>
    /// Pack-wide rather than per-job so the "nothing is certain" rule cannot be forgotten on one row.
    /// <see cref="Job.SuccessChanceFor"/> takes it as an argument for the same reason the mastery
    /// thresholds are passed to <c>JobService</c>: the domain stays free of content loading.
    /// </remarks>
    public double MaxSuccessChance { get; }

    /// <summary>Fraction of the rolled cash paid out on a failed attempt.</summary>
    public double ConsolationCashFraction { get; }

    /// <summary>Fraction of the job's XP granted on a failed attempt.</summary>
    public double ConsolationXpFraction { get; }

    /// <summary>
    /// Finds a job by content id. Comparison is <see cref="StringComparer.Ordinal"/> — ids are
    /// content keys, not user-facing text, so a case-insensitive match would let
    /// <c>/jobs/BOOST-A-PARKED-CAR</c> resolve and quietly admit a second spelling of every id.
    /// </summary>
    /// <returns>The job, or <see langword="null"/> when no job carries that id.</returns>
    public Job? Find(string jobId) =>
        _byId.TryGetValue(jobId, out var job) ? job : null;
}
