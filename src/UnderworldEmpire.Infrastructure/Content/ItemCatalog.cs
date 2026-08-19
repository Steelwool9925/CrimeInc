using System.Collections.Frozen;
using UnderworldEmpire.Domain.Items;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated item catalog: every <see cref="Item"/> in the active pack. Immutable and shared
/// as a singleton for the process lifetime.
/// </summary>
/// <remarks>
/// Same shape and rationale as <see cref="JobCatalog"/> — a <see cref="FrozenDictionary{TKey,TValue}"/>
/// with an ordinal comparer, because every hot path (the store, a purchase, an equip, and the stat
/// aggregation behind <c>GET /me</c>) resolves an item by id, and the collection is built once at startup
/// and then only read.
/// </remarks>
public sealed class ItemCatalog
{
    private readonly FrozenDictionary<string, Item> _byId;

    /// <param name="items">Validated items, in the order content declared them.</param>
    public ItemCatalog(IReadOnlyList<Item> items)
    {
        Items = items;
        _byId = items.ToFrozenDictionary(i => i.Id, StringComparer.Ordinal);
    }

    /// <summary>Every item, in content-declared order. Content controls display order; the engine does not sort.</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>
    /// Finds an item by content id. Comparison is <see cref="StringComparer.Ordinal"/> — ids are content
    /// keys, not user-facing text, so a case-insensitive match would let a second spelling of every id
    /// resolve.
    /// </summary>
    /// <returns>The item, or <see langword="null"/> when no item carries that id.</returns>
    public Item? Find(string itemId) =>
        _byId.TryGetValue(itemId, out var item) ? item : null;
}
