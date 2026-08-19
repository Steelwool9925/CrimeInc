namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// The result of asking to buy an item: either a refusal with a reason, or what the purchase cost and
/// left the player holding.
/// </summary>
/// <remarks>
/// Same shape as <see cref="Jobs.JobAttemptResult"/> — one type with a <see cref="Failure"/>
/// discriminator rather than an exception, because being unable to afford something is expected play.
/// </remarks>
public sealed record ItemPurchaseResult
{
    private ItemPurchaseResult(InventoryFailure failure, long cashSpent, int quantityOwned)
    {
        Failure = failure;
        CashSpent = cashSpent;
        QuantityOwned = quantityOwned;
    }

    /// <summary>The refusal reason, or <see cref="InventoryFailure.None"/> on success.</summary>
    public InventoryFailure Failure { get; }

    /// <summary>Cash deducted, equal to the item's price. Zero on refusal.</summary>
    public long CashSpent { get; }

    /// <summary>Copies the player holds after the purchase. Zero on refusal.</summary>
    public int QuantityOwned { get; }

    /// <summary>Whether the purchase went through.</summary>
    public bool Succeeded => Failure == InventoryFailure.None;

    /// <summary>Builds a refusal.</summary>
    public static ItemPurchaseResult Refused(InventoryFailure failure)
    {
        if (failure == InventoryFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new ItemPurchaseResult(failure, 0, 0);
    }

    /// <summary>Builds a success.</summary>
    public static ItemPurchaseResult Success(long cashSpent, int quantityOwned) =>
        new(InventoryFailure.None, cashSpent, quantityOwned);
}

/// <summary>
/// The result of asking to equip or unequip an item: either a refusal with a reason, or which items
/// changed state.
/// </summary>
/// <remarks>
/// One type covers both directions so a client has a single shape to parse. Equipping reports the item
/// put on, plus whatever it displaced from that slot; unequipping reports only the item taken off.
/// </remarks>
public sealed record ItemEquipResult
{
    private ItemEquipResult(InventoryFailure failure, string? equippedItemId, string? unequippedItemId)
    {
        Failure = failure;
        EquippedItemId = equippedItemId;
        UnequippedItemId = unequippedItemId;
    }

    /// <summary>The refusal reason, or <see cref="InventoryFailure.None"/> on success.</summary>
    public InventoryFailure Failure { get; }

    /// <summary>The item now worn, or <see langword="null"/> when the action was an unequip or a refusal.</summary>
    public string? EquippedItemId { get; }

    /// <summary>
    /// The item taken off — the displaced incumbent on an equip, or the target on an unequip.
    /// <see langword="null"/> when the slot was empty or the action was refused.
    /// </summary>
    public string? UnequippedItemId { get; }

    /// <summary>Whether the action went through.</summary>
    public bool Succeeded => Failure == InventoryFailure.None;

    /// <summary>Builds a refusal.</summary>
    public static ItemEquipResult Refused(InventoryFailure failure)
    {
        if (failure == InventoryFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new ItemEquipResult(failure, null, null);
    }

    /// <summary>Builds a successful equip, naming the incumbent it displaced if there was one.</summary>
    public static ItemEquipResult Equipped(string equippedItemId, string? unequippedItemId) =>
        new(InventoryFailure.None, equippedItemId, unequippedItemId);

    /// <summary>Builds a successful unequip.</summary>
    public static ItemEquipResult Unequipped(string unequippedItemId) =>
        new(InventoryFailure.None, null, unequippedItemId);
}
