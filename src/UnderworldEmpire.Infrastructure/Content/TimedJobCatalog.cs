using System.Collections.Frozen;
using UnderworldEmpire.Domain.Timed;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated pool of timed-job appointments. Immutable and shared as a singleton;
/// lookups by id go through a <see cref="FrozenDictionary{TKey,TValue}"/> because a start or
/// collect resolves one per request.
/// </summary>
public sealed class TimedJobCatalog
{
    private readonly FrozenDictionary<string, TimedJob> _byId;

    /// <param name="timedJobs">Validated appointments, in the order content declared them.</param>
    public TimedJobCatalog(IReadOnlyList<TimedJob> timedJobs)
    {
        TimedJobs = timedJobs;
        _byId = timedJobs.ToFrozenDictionary(j => j.Id, StringComparer.Ordinal);
    }

    /// <summary>Every appointment in the pool, in content-declared order.</summary>
    public IReadOnlyList<TimedJob> TimedJobs { get; }

    /// <summary>Finds an appointment by content id (ordinal), or <see langword="null"/>.</summary>
    public TimedJob? Find(string timedJobId) =>
        _byId.TryGetValue(timedJobId, out var timedJob) ? timedJob : null;
}
