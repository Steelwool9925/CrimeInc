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
    public JobCatalog(IReadOnlyList<Job> jobs, int completionsPerRank, int maxRank)
    {
        Jobs = jobs;
        CompletionsPerRank = completionsPerRank;
        MaxRank = maxRank;
        _byId = jobs.ToFrozenDictionary(j => j.Id, StringComparer.Ordinal);
    }

    /// <summary>Every job, in content-declared order. Content controls display order; the engine does not sort.</summary>
    public IReadOnlyList<Job> Jobs { get; }

    /// <summary>Completions required to gain one mastery rank.</summary>
    public int CompletionsPerRank { get; }

    /// <summary>Highest attainable mastery rank.</summary>
    public int MaxRank { get; }

    /// <summary>
    /// Finds a job by content id. Comparison is <see cref="StringComparer.Ordinal"/> — ids are
    /// content keys, not user-facing text, so a case-insensitive match would let
    /// <c>/jobs/BOOST-A-PARKED-CAR</c> resolve and quietly admit a second spelling of every id.
    /// </summary>
    /// <returns>The job, or <see langword="null"/> when no job carries that id.</returns>
    public Job? Find(string jobId) =>
        _byId.TryGetValue(jobId, out var job) ? job : null;
}
