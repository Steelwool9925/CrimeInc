using System.Collections.Frozen;
using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated pool of daily objectives. Immutable and shared as a singleton; lookups by id
/// go through a <see cref="FrozenDictionary{TKey,TValue}"/> because a claim resolves one per request.
/// </summary>
public sealed class ObjectiveCatalog
{
    private readonly FrozenDictionary<string, Objective> _byId;

    /// <param name="objectives">Validated objectives, in the order content declared them.</param>
    public ObjectiveCatalog(IReadOnlyList<Objective> objectives)
    {
        Objectives = objectives;
        _byId = objectives.ToFrozenDictionary(o => o.Id, StringComparer.Ordinal);
    }

    /// <summary>Every objective in the pool, in content-declared order.</summary>
    public IReadOnlyList<Objective> Objectives { get; }

    /// <summary>Finds an objective by content id (ordinal), or <see langword="null"/>.</summary>
    public Objective? Find(string objectiveId) =>
        _byId.TryGetValue(objectiveId, out var objective) ? objective : null;
}
