namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// The loadout slot an <see cref="Item"/> occupies. A player may have at most one item equipped per
/// slot, which is what makes equipping a choice rather than an accumulation.
/// </summary>
/// <remarks>
/// Slots are an <b>engine</b> concept, not content: the aggregation in <see cref="Loadout"/> and the
/// exclusivity rule in <see cref="InventoryService"/> both need to enumerate them, so a pack cannot
/// invent a new one. Content chooses which slot each item fills (<c>items.json</c> → <c>slot</c>),
/// and the loader rejects a value that is not named here rather than silently defaulting.
/// </remarks>
public enum ItemSlot
{
    /// <summary>Offensive gear. Contributes mostly attack.</summary>
    Weapon,

    /// <summary>Protective gear. Contributes mostly defense.</summary>
    Armor,

    /// <summary>A ride. Contributes to both, and is the flavour slot for a crime theme.</summary>
    Vehicle
}
