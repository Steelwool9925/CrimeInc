namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// One player's holding of one item: how many copies they own, and whether it is the copy currently
/// worn in its slot. Persisted per (player, item) pair, the same shape as
/// <see cref="Jobs.PlayerJobMastery"/>.
/// </summary>
/// <remarks>
/// <para>
/// A row is created empty and then filled by <see cref="AddCopies"/>, so the caller can create-or-fetch
/// without deciding the quantity, exactly as the mastery row is created empty and then advanced by
/// <c>RecordCompletion</c>.
/// </para>
/// <para>
/// <see cref="Equipped"/> is a flag on the holding rather than a pointer from the player, because the
/// slot an item occupies is <b>content</b>: the engine cannot store "the weapon slot's item id" without
/// duplicating a fact that <c>items.json</c> already owns and that a retune may change. The cost is that
/// one-equipped-item-per-slot cannot be a database constraint — <see cref="ItemSlot"/> is not a column
/// here — so it is enforced by <see cref="InventoryService.Equip"/> and asserted by <see cref="Loadout"/>.
/// </para>
/// <para>
/// Item stats are deliberately <b>not</b> copied onto this row. A retune of <c>items.json</c> must move
/// every player's numbers; a snapshot taken at purchase time would freeze balance into player data.
/// </para>
/// </remarks>
public class PlayerItem
{
    /// <summary>Owning player. Half of the composite primary key.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Content id of the item. Half of the composite primary key.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>How many copies the player owns. Zero on a freshly created row.</summary>
    public int Quantity { get; private set; }

    /// <summary>Whether this item is the one worn in its slot.</summary>
    public bool Equipped { get; private set; }

    /// <summary>Whether the player owns at least one copy.</summary>
    public bool IsOwned => Quantity > 0;

    /// <summary>
    /// Copies the player could spend without giving up the one they are wearing.
    /// </summary>
    /// <remarks>
    /// Items a job uses are consumed, but they are not the player's equipped gear: the worn copy is
    /// never the spent one. A worn item therefore sets one copy aside, and what a job's item gate and
    /// its odds count is only the rest. Without this, wearing a switchblade to meet a stat gate and
    /// then running the job that needs one spends the very weapon that met the gate, and the player
    /// loses its stats on the first attempt.
    /// </remarks>
    public int SpareQuantity => Equipped && IsOwned ? Quantity - 1 : Quantity;

    /// <summary>Adds <paramref name="count"/> copies to the holding.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is not positive.</exception>
    public void AddCopies(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Copies added must be >= 1.");

        Quantity += count;
    }

    /// <summary>
    /// Marks this holding as the item worn in its slot. Idempotent.
    /// </summary>
    /// <remarks>
    /// Owning a copy is the only precondition checked here. Level gating and slot exclusivity need the
    /// item definition and the player's other holdings, so they live in <see cref="InventoryService"/>;
    /// this guard exists because "equipped something I do not own" is nonsense in any caller.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the player owns no copies.</exception>
    public void Equip()
    {
        if (!IsOwned)
            throw new InvalidOperationException("Cannot equip an item the player does not own.");

        Equipped = true;
    }

    /// <summary>Stops wearing this item, keeping the copies owned. Idempotent.</summary>
    public void Unequip() => Equipped = false;

    /// <summary>
    /// Removes <paramref name="count"/> copies from the holding — a one-time-use consumable spend.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="AddCopies"/>. A job's item-quantity gate and the equip/loadout system
    /// ask independent questions about the same row (see <c>Job.ItemRequirements</c>'s remarks), so
    /// spending *some* copies leaves the item worn.
    /// <para>
    /// Spending the **last** copy takes it off. This used to leave the row reporting as equipped with
    /// zero copies, which kept adding the item's attack and defense to the player's loadout for good:
    /// once a job needed only one copy (<c>hijack-a-delivery-truck</c> went from hold-two to hold-one),
    /// the ordinary way to meet its stat gate — wear the switchblade, then run the job — spent the worn
    /// copy and kept its +7 attack. Nothing equipped can be unowned, because <see cref="Equip"/> refuses
    /// an item the player does not hold; this keeps the same invariant on the way out.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="count"/> is not positive, or exceeds <see cref="Quantity"/>.
    /// </exception>
    public void Consume(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Copies consumed must be >= 1.");
        if (count > Quantity)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                $"Cannot consume {count} copies from a holding of {Quantity}.");

        Quantity -= count;

        if (Quantity == 0)
            Equipped = false;
    }
}
