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

    /// <summary>
    /// The definitions of everything in <paramref name="holdings"/> that the player is wearing.
    /// </summary>
    /// <remarks>
    /// The bridge between persisted holdings (which store only an id) and the domain's
    /// <see cref="Loadout"/> (which needs resolved definitions). Lives here rather than in each caller
    /// because <c>GET /inventory</c> and every write path need the same answer, and two copies would be
    /// free to disagree about the case below.
    /// <para>
    /// A holding whose id no longer resolves — content dropped the item between the purchase and now — is
    /// <b>skipped</b>, not treated as an error. The row survives, so a restored item comes back with its
    /// history, but an item that no longer exists scores nothing. Throwing instead would lock the player
    /// out of their own inventory screen over a content edit they had no part in.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Item> ResolveEquipped(IEnumerable<PlayerItem> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);

        var equipped = new List<Item>();
        foreach (var holding in holdings)
        {
            if (!holding.Equipped)
                continue;

            if (Find(holding.ItemId) is { } definition)
                equipped.Add(definition);
        }

        return equipped;
    }
}
