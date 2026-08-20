namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// A player's effective combat stats, split into what they own and what their gear adds.
/// </summary>
/// <remarks>
/// Both halves are sent rather than only the total, because "Attack 9" alone cannot tell a player that
/// four of it comes off the moment they unequip. The totals are computed server-side from the equipped
/// set on every read — nothing is stored, so a content retune moves every player's numbers at once.
/// </remarks>
/// <param name="BaseAttack">The player's own attack, before gear.</param>
/// <param name="ItemAttack">Attack contributed by equipped gear alone.</param>
/// <param name="TotalAttack">Effective attack: base plus gear.</param>
/// <param name="BaseDefense">The player's own defense, before gear.</param>
/// <param name="ItemDefense">Defense contributed by equipped gear alone.</param>
/// <param name="TotalDefense">Effective defense: base plus gear.</param>
public record LoadoutDto(
    int BaseAttack,
    int ItemAttack,
    int TotalAttack,
    int BaseDefense,
    int ItemDefense,
    int TotalDefense);

/// <summary>
/// One catalog item as offered to the calling player: the content definition, that player's holding,
/// and whether they may buy or wear it right now.
/// </summary>
/// <param name="Id">Content id; the route value for the buy/equip/unequip endpoints.</param>
/// <param name="Name">Player-facing name from the content pack.</param>
/// <param name="Description">Player-facing flavour text from the content pack.</param>
/// <param name="Slot">Loadout slot, lower-cased to match the vocabulary <c>items.json</c> uses.</param>
/// <param name="LevelRequirement">Minimum level required to buy or equip.</param>
/// <param name="Price">Cash cost of one copy. Fixed, never rolled.</param>
/// <param name="Attack">Attack this item adds while equipped.</param>
/// <param name="Defense">Defense this item adds while equipped.</param>
/// <param name="Quantity">Copies the player owns; zero when they own none.</param>
/// <param name="Equipped">Whether this is the item worn in its slot.</param>
/// <param name="CanBuy">Whether a purchase would succeed at this instant. Computed server-side.</param>
/// <param name="BuyUnavailableReason">
///     Why <paramref name="CanBuy"/> is false — <c>"level"</c> or <c>"cash"</c> — or
///     <see langword="null"/> when it is true. A stable token, not display text.
/// </param>
/// <param name="CanEquip">Whether an equip would succeed at this instant.</param>
/// <param name="EquipUnavailableReason">
///     Why <paramref name="CanEquip"/> is false — <c>"notOwned"</c>, <c>"level"</c> or
///     <c>"alreadyEquipped"</c> — or <see langword="null"/> when it is true.
/// </param>
public record InventoryItemDto(
    string Id,
    string Name,
    string Description,
    string Slot,
    int LevelRequirement,
    long Price,
    int Attack,
    int Defense,
    int Quantity,
    bool Equipped,
    bool CanBuy,
    string? BuyUnavailableReason,
    bool CanEquip,
    string? EquipUnavailableReason);

/// <summary>Response body for GET /inventory.</summary>
/// <remarks>
/// Carries the <b>whole catalog</b>, not only the rows the player owns. The store and the inventory are
/// two views of one list — an owned item still needs its price shown when a second copy is on offer, and
/// an unowned one still needs its stats shown before the player commits — so a single call serves both
/// screens and they cannot disagree about an item. Same shape as <see cref="JobListResponse"/>.
/// </remarks>
/// <param name="Items">Every item in the active pack, in content-declared order.</param>
/// <param name="Loadout">The player's effective stats, given what they currently wear.</param>
/// <param name="Cash">
///     The cash the affordability gating was computed against, so the client can render prices without a
///     second call to /me.
/// </param>
/// <param name="Level">The player's level at request time, for the same reason.</param>
public record InventoryResponse(
    IReadOnlyList<InventoryItemDto> Items,
    LoadoutDto Loadout,
    long Cash,
    int Level);

/// <summary>Response body for POST /items/{id}/buy.</summary>
/// <param name="ItemId">The item bought.</param>
/// <param name="CashSpent">Cash deducted, equal to the item's price.</param>
/// <param name="QuantityOwned">Copies the player holds after the purchase.</param>
/// <param name="Player">
///     The player's full state after the purchase, so the client does not follow every buy with a
///     GET /me. Same rationale as <see cref="DoJobResponse"/>.
/// </param>
/// <param name="Loadout">
///     Effective stats after the purchase. Buying does not equip, so these are unchanged — sent anyway so
///     the client has one place to read stats from regardless of which action it just took.
/// </param>
public record BuyItemResponse(
    string ItemId,
    long CashSpent,
    int QuantityOwned,
    MeDto Player,
    LoadoutDto Loadout);

/// <summary>Response body for POST /items/{id}/equip and POST /items/{id}/unequip.</summary>
/// <param name="EquippedItemId">The item now worn, or <see langword="null"/> after an unequip.</param>
/// <param name="UnequippedItemId">
///     The item taken off — the displaced incumbent on an equip, or the target on an unequip.
///     <see langword="null"/> when the slot was empty.
/// </param>
/// <param name="Player">The player's full state after the action.</param>
/// <param name="Loadout">Effective stats after the action — the figure CP3 watches rise.</param>
public record EquipItemResponse(
    string? EquippedItemId,
    string? UnequippedItemId,
    MeDto Player,
    LoadoutDto Loadout);
