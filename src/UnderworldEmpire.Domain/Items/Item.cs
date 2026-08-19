namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// A single item definition as the domain sees it, projected from the content pack's
/// <c>items.json</c>. Immutable: the catalog is loaded once at startup and never mutated.
/// </summary>
/// <remarks>
/// Every number here is a tunable balance value supplied by content, never hard-coded in the engine
/// — see <c>content/crime/README.md</c>. The loader validates raw content and collects all errors
/// before constructing any <see cref="Item"/>, so the guards below are a safety net for programmatic
/// construction (tests, future generators) rather than the primary validation path. Same division of
/// labour as <see cref="Jobs.Job"/>.
/// </remarks>
public sealed record Item
{
    /// <summary>Stable content id, unique within the pack. Used as the route value and the inventory key.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing item name, supplied by content.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing flavour text, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Which loadout slot this item occupies. One equipped item per slot.</summary>
    public required ItemSlot Slot { get; init; }

    /// <summary>
    /// Minimum player level permitted to buy or equip this item; always &gt;= 1.
    /// </summary>
    /// <remarks>
    /// Gates equipping as well as buying, so gear bought before a level reset (or granted by a
    /// future reward path) cannot be worn early.
    /// </remarks>
    public required int LevelRequirement { get; init; }

    /// <summary>Cash cost to buy one copy. Fixed, not rolled — a shop price the player can plan against.</summary>
    public required long Price { get; init; }

    /// <summary>Attack added to the player's base attack while this item is equipped.</summary>
    public int Attack { get; init; }

    /// <summary>Defense added to the player's base defense while this item is equipped.</summary>
    public int Defense { get; init; }

    /// <summary>
    /// Validates the invariants the engine relies on. Called by the content loader after its own
    /// error-collecting pass, and directly by tests that build an <see cref="Item"/> by hand.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Id"/> or <see cref="Name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric invariant is violated.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Item id must not be blank.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Item '{Id}' has a blank name.", nameof(Name));
        if (LevelRequirement < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelRequirement), LevelRequirement,
                $"Item '{Id}' must require at least level 1.");
        if (Price < 0)
            throw new ArgumentOutOfRangeException(nameof(Price), Price,
                $"Item '{Id}' cannot cost negative cash.");

        // Bonuses are additive and non-negative: a "cursed" item that subtracts attack would make
        // the loadout total non-monotonic, so equipping more gear could quietly weaken a player.
        // If a debuff item is ever wanted it needs its own concept, not a negative bonus.
        if (Attack < 0)
            throw new ArgumentOutOfRangeException(nameof(Attack), Attack,
                $"Item '{Id}' cannot grant negative attack.");
        if (Defense < 0)
            throw new ArgumentOutOfRangeException(nameof(Defense), Defense,
                $"Item '{Id}' cannot grant negative defense.");
    }
}
