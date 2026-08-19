namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// A player's effective combat stats: their base attack/defense plus the bonuses from everything they
/// have equipped. Pure and immutable — computed on read, never stored.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is cached onto <see cref="Player"/>. An effective-stat column would have to be recomputed on
/// every equip, unequip and content retune, and any missed path would leave a player permanently wrong.
/// Deriving it from the holdings makes the equipped set the single source of truth. Combat (T4.2) will
/// consume this rather than reaching for <see cref="Player.Attack"/> directly.
/// </para>
/// <para>
/// This takes resolved <see cref="Item"/> definitions rather than <see cref="PlayerItem"/> rows and a
/// catalog: the catalog lives in the infrastructure layer, so the caller resolves ids to definitions and
/// the domain stays free of content-loading concerns. Same seam as <c>JobService</c> taking mastery
/// thresholds as arguments instead of the catalog.
/// </para>
/// </remarks>
public sealed class Loadout
{
    private readonly Dictionary<ItemSlot, Item> _bySlot;

    private Loadout(int baseAttack, int baseDefense, IReadOnlyList<Item> equipped, Dictionary<ItemSlot, Item> bySlot)
    {
        BaseAttack = baseAttack;
        BaseDefense = baseDefense;
        Equipped = equipped;
        _bySlot = bySlot;

        // Summed once here rather than per property read: every caller wants both totals, and the
        // properties would otherwise walk the equipped list four times to answer one projection.
        ItemAttack = equipped.Sum(i => i.Attack);
        ItemDefense = equipped.Sum(i => i.Defense);
    }

    /// <summary>The player's own attack, before any gear.</summary>
    public int BaseAttack { get; }

    /// <summary>The player's own defense, before any gear.</summary>
    public int BaseDefense { get; }

    /// <summary>Everything equipped, in the order the caller supplied.</summary>
    public IReadOnlyList<Item> Equipped { get; }

    /// <summary>Attack contributed by equipped gear alone.</summary>
    public int ItemAttack { get; }

    /// <summary>Defense contributed by equipped gear alone.</summary>
    public int ItemDefense { get; }

    /// <summary>Effective attack: base plus gear. The figure CP3 watches rise.</summary>
    public int TotalAttack => BaseAttack + ItemAttack;

    /// <summary>Effective defense: base plus gear.</summary>
    public int TotalDefense => BaseDefense + ItemDefense;

    /// <summary>
    /// Builds the loadout for <paramref name="player"/> from the items they have equipped.
    /// </summary>
    /// <param name="player">The player whose base stats form the floor.</param>
    /// <param name="equippedItems">
    ///     Resolved definitions of the items currently equipped — at most one per <see cref="ItemSlot"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when two items occupy the same slot. That state is unreachable through the API —
    ///     <see cref="InventoryService.Equip"/> unequips the incumbent — and cannot be a database
    ///     constraint because the slot is content rather than a column, so it is checked here. Silently
    ///     summing both would hand the player double the gear bonus they are entitled to.
    /// </exception>
    public static Loadout For(Player player, IEnumerable<Item> equippedItems)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(equippedItems);

        var equipped = equippedItems.ToList();
        var bySlot = new Dictionary<ItemSlot, Item>(equipped.Count);

        foreach (var item in equipped)
        {
            if (bySlot.TryGetValue(item.Slot, out var incumbent))
                throw new InvalidOperationException(
                    $"Player {player.Id} has two items equipped in the {item.Slot} slot " +
                    $"('{incumbent.Id}' and '{item.Id}'). At most one item may occupy a slot.");

            bySlot.Add(item.Slot, item);
        }

        return new Loadout(player.Attack, player.Defense, equipped, bySlot);
    }

    /// <summary>The item worn in <paramref name="slot"/>, or <see langword="null"/> when the slot is empty.</summary>
    public Item? InSlot(ItemSlot slot) => _bySlot.GetValueOrDefault(slot);
}
