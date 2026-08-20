namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// The rules of owning gear: what a player may buy, what they may wear, and what wearing it displaces.
/// Pure game logic with no I/O — it mutates the objects handed to it and returns what happened.
/// Persisting the result is the caller's job.
/// </summary>
/// <remarks>
/// Deliberately unlike <see cref="Jobs.JobService"/> in one respect: nothing here is rolled. A shop price
/// and a stat bonus are figures the player plans against, so both come straight from content. There is no
/// <c>IRandomSource</c> to inject and the service is stateless.
/// </remarks>
public sealed class InventoryService
{
    /// <summary>
    /// Buys one copy of <paramref name="item"/> for <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// On success this mutates <paramref name="player"/> (cash down) and <paramref name="holding"/> (one
    /// more copy). On refusal <b>nothing is mutated</b> — every check runs before the first write, so a
    /// refused purchase cannot leave a player charged for nothing.
    /// <para>
    /// Buying does not equip. Wearing new gear is a separate, explicit choice: a cheap item bought to sell
    /// on or to hold should not silently displace something better.
    /// </para>
    /// </remarks>
    /// <param name="player">The buyer; mutated on success.</param>
    /// <param name="item">The item definition from content.</param>
    /// <param name="holding">
    ///     This player's holding for this item — freshly created when they own none yet. Mutated on success.
    /// </param>
    public ItemPurchaseResult Purchase(Player player, Item item, PlayerItem holding)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holding);

        // ---- Validate before mutating anything ----

        var obstacle = CheckPurchase(player, item);
        if (obstacle != InventoryFailure.None)
            return ItemPurchaseResult.Refused(obstacle);

        // ---- Apply ----

        player.SpendCash(item.Price);
        holding.AddCopies(1);

        return ItemPurchaseResult.Success(item.Price, holding.Quantity);
    }

    /// <summary>
    /// Asks what, if anything, stops <paramref name="player"/> buying <paramref name="item"/> right now,
    /// without buying it. Returns <see cref="InventoryFailure.None"/> when nothing does.
    /// </summary>
    /// <remarks>
    /// Exists so the store listing and the purchase itself cannot disagree: <see cref="Purchase"/> refuses
    /// on exactly this answer, so a client told it may buy something will not then be rejected. The
    /// alternative — re-deriving the gate where the list is projected — is a second copy of the rule, free
    /// to drift the moment either side is retuned.
    /// <para>
    /// Level is reported ahead of cash because it is the permanent obstacle: it tells the player the thing
    /// that will not fix itself by earning more. Same precedence as a job refusal.
    /// </para>
    /// </remarks>
    public InventoryFailure CheckPurchase(Player player, Item item)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(item);

        if (player.Level < item.LevelRequirement)
            return InventoryFailure.LevelTooLow;

        if (player.Cash < item.Price)
            return InventoryFailure.InsufficientCash;

        return InventoryFailure.None;
    }

    /// <summary>
    /// Asks what, if anything, stops <paramref name="player"/> wearing <paramref name="item"/> right now,
    /// without wearing it. Returns <see cref="InventoryFailure.None"/> when nothing does.
    /// </summary>
    /// <remarks>
    /// The read-only counterpart to <see cref="Equip"/>, which refuses on exactly this answer — see the
    /// remarks on <see cref="CheckPurchase"/> for why the rule is asked rather than restated.
    /// <para>
    /// Not owning the item is reported first: a player who does not have it has nothing to act on, so
    /// naming a level gate they also fail would send them to earn levels for gear they still would not
    /// hold. Displacing the slot's incumbent is not an obstacle — that is what equipping does.
    /// </para>
    /// </remarks>
    public InventoryFailure CheckEquip(Player player, Item item, PlayerItem holding)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holding);

        if (!holding.IsOwned)
            return InventoryFailure.NotOwned;

        // Gating equipping as well as buying means gear obtained by any future route — a reward, a drop,
        // a rebalanced level curve — still cannot be worn early.
        if (player.Level < item.LevelRequirement)
            return InventoryFailure.LevelTooLow;

        if (holding.Equipped)
            return InventoryFailure.AlreadyEquipped;

        return InventoryFailure.None;
    }

    /// <summary>
    /// Wears <paramref name="item"/>, taking off whatever already occupied its slot.
    /// </summary>
    /// <remarks>
    /// The incumbent is found from <paramref name="equipped"/> rather than passed in directly, so the
    /// caller cannot get slot exclusivity wrong by nominating the wrong row. That invariant has no database
    /// constraint behind it — <see cref="ItemSlot"/> is content, not a column — which makes this method the
    /// only thing standing between a player and a double-counted gear bonus.
    /// </remarks>
    /// <param name="player">The wearer. Not mutated: equipping costs nothing.</param>
    /// <param name="item">The definition of the item to wear.</param>
    /// <param name="holding">This player's holding for <paramref name="item"/>; mutated on success.</param>
    /// <param name="equipped">
    ///     The player's holdings paired with their definitions — the equipped ones are what matter, and
    ///     anything unworn, in another slot, or <paramref name="holding"/> itself is ignored. Passing the
    ///     whole inventory is therefore safe, just wasteful.
    /// </param>
    public ItemEquipResult Equip(
        Player player,
        Item item,
        PlayerItem holding,
        IEnumerable<OwnedItem> equipped)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(equipped);

        // ---- Validate before mutating anything ----

        var obstacle = CheckEquip(player, item, holding);
        if (obstacle != InventoryFailure.None)
            return ItemEquipResult.Refused(obstacle);

        // Filter on Equipped as well as slot, so handing this method every holding rather than only the
        // worn ones cannot make it report displacing something the player was not wearing. Compare by item
        // id, not reference: the caller may have loaded the same row twice.
        var incumbent = equipped.FirstOrDefault(o =>
            o.Holding.Equipped &&
            o.Item.Slot == item.Slot &&
            !string.Equals(o.Holding.ItemId, holding.ItemId, StringComparison.Ordinal));

        // ---- Apply ----

        incumbent?.Holding.Unequip();
        holding.Equip();

        return ItemEquipResult.Equipped(holding.ItemId, incumbent?.Holding.ItemId);
    }

    /// <summary>
    /// Takes off <paramref name="holding"/>, keeping the copies owned.
    /// </summary>
    /// <remarks>
    /// Needs no item definition or player: an unequip cannot be gated on anything, since a player who has
    /// out-levelled nothing and paid nothing is simply choosing to wear less.
    /// </remarks>
    public ItemEquipResult Unequip(PlayerItem holding)
    {
        ArgumentNullException.ThrowIfNull(holding);

        if (!holding.Equipped)
            return ItemEquipResult.Refused(InventoryFailure.NotEquipped);

        holding.Unequip();

        return ItemEquipResult.Unequipped(holding.ItemId);
    }
}
