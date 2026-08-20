using System.Collections.Frozen;
using UnderworldEmpire.Domain.Properties;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated property catalog: every <see cref="Property"/> in the active pack. Immutable
/// and shared as a singleton for the process lifetime.
/// </summary>
/// <remarks>
/// Same shape and rationale as <see cref="ItemCatalog"/> — a <see cref="FrozenDictionary{TKey,TValue}"/>
/// with an ordinal comparer, because every path that touches a property (the listing, a purchase, a
/// collection) resolves it by id, and the collection is built once at startup and then only read.
/// </remarks>
public sealed class PropertyCatalog
{
    private readonly FrozenDictionary<string, Property> _byId;

    /// <param name="properties">Validated properties, in the order content declared them.</param>
    public PropertyCatalog(IReadOnlyList<Property> properties)
    {
        Properties = properties;
        _byId = properties.ToFrozenDictionary(p => p.Id, StringComparer.Ordinal);
    }

    /// <summary>Every property, in content-declared order. Content controls display order; the engine does not sort.</summary>
    public IReadOnlyList<Property> Properties { get; }

    /// <summary>
    /// Finds a property by content id. Comparison is <see cref="StringComparer.Ordinal"/> — ids are
    /// content keys, not user-facing text, so a case-insensitive match would let a second spelling of
    /// every id resolve.
    /// </summary>
    /// <returns>The property, or <see langword="null"/> when no property carries that id.</returns>
    public Property? Find(string propertyId) =>
        _byId.TryGetValue(propertyId, out var property) ? property : null;
}
