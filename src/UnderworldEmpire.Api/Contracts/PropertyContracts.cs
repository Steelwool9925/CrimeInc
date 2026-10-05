namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// One catalog property as offered to the calling player: the content definition, that player's
/// holding, what it has earned, and whether they may buy or collect right now.
/// </summary>
/// <remarks>
/// <see cref="Pending"/> and <see cref="Cap"/> are computed at request time from elapsed time and are
/// <b>not</b> persisted by the read — see the remarks on <c>PropertyEndpoints</c>. They are a snapshot
/// that is stale the moment it is sent, which is exactly why <see cref="IncomePerHour"/> rides along:
/// a client can animate the bar forward between polls without inventing the rate.
/// </remarks>
/// <param name="Id">Content id; the route value for the buy/collect endpoints.</param>
/// <param name="Name">Player-facing name from the content pack.</param>
/// <param name="Description">Player-facing flavour text from the content pack.</param>
/// <param name="LevelRequirement">Minimum level required to buy.</param>
/// <param name="Price">Cash cost of one unit. Fixed, never rolled.</param>
/// <param name="IncomePerHour">
///     What <b>one unit</b> earns per hour, straight from content. Per unit rather than per holding so
///     the figure on an unowned property still means something in the store.
/// </param>
/// <param name="AccrualCapMinutes">How long one unit takes to fill, in minutes.</param>
/// <param name="Quantity">Units the player owns; zero when they own none.</param>
/// <param name="MaxUnits">
///     The most units one player can hold, or <see langword="null"/> when there is no limit. Sent so
///     the card can say "2 of 3" without the client knowing the rule.
/// </param>
/// <param name="Pending">Cash collectable at this instant, across every unit owned.</param>
/// <param name="Cap">
///     The ceiling <paramref name="Pending"/> is filling, scaled by units owned. Zero when none are
///     owned, so a client never draws a bar against a ceiling the player has not bought into.
/// </param>
/// <param name="CanBuy">Whether a purchase would succeed at this instant. Computed server-side.</param>
/// <param name="BuyUnavailableReason">
///     Why <paramref name="CanBuy"/> is false — <c>"level"</c> or <c>"cash"</c> — or
///     <see langword="null"/> when it is true. A stable token, not display text.
/// </param>
/// <param name="CanCollect">Whether a collection would pay out at this instant.</param>
/// <param name="CollectUnavailableReason">
///     Why <paramref name="CanCollect"/> is false — <c>"notOwned"</c> or <c>"nothingToCollect"</c> — or
///     <see langword="null"/> when it is true.
/// </param>
public record PropertyDto(
    string Id,
    string Name,
    string Description,
    int LevelRequirement,
    long Price,
    long IncomePerHour,
    int AccrualCapMinutes,
    int Quantity,
    int? MaxUnits,
    long Pending,
    long Cap,
    bool CanBuy,
    string? BuyUnavailableReason,
    bool CanCollect,
    string? CollectUnavailableReason);

/// <summary>Response body for GET /properties.</summary>
/// <remarks>
/// Carries the <b>whole catalog</b>, not only the rows the player owns, for the same reason
/// <see cref="InventoryResponse"/> does: the store half and the holdings half of the Properties screen
/// are two views of one list, so a single call serves both and they cannot disagree about a property.
/// </remarks>
/// <param name="Properties">Every property in the active pack, in content-declared order.</param>
/// <param name="Cash">
///     The cash the affordability gating was computed against, so the client can render prices without
///     a second call to /me.
/// </param>
/// <param name="Level">The player's level at request time, for the same reason.</param>
public record PropertyListResponse(
    IReadOnlyList<PropertyDto> Properties,
    long Cash,
    int Level);

/// <summary>Response body for POST /properties/{id}/buy.</summary>
/// <param name="PropertyId">The property bought.</param>
/// <param name="CashSpent">Cash deducted, equal to the property's price.</param>
/// <param name="QuantityOwned">Units the player holds after the purchase.</param>
/// <param name="Pending">
///     Cash collectable immediately after the purchase. Normally what the existing units had already
///     banked — buying settles them at the old rate first, so a new unit never back-pays.
/// </param>
/// <param name="Cap">The ceiling after the purchase, scaled by the new quantity.</param>
/// <param name="Player">
///     The player's full state after the purchase, so the client does not follow every buy with a
///     GET /me. Same rationale as <see cref="BuyItemResponse"/>.
/// </param>
public record BuyPropertyResponse(
    string PropertyId,
    long CashSpent,
    int QuantityOwned,
    long Pending,
    long Cap,
    MeDto Player);

/// <summary>Response body for POST /properties/{id}/collect.</summary>
/// <param name="PropertyId">The property collected from.</param>
/// <param name="CashCollected">Cash paid into the player's pocket.</param>
/// <param name="Pending">
///     What is still pending straight after the collection — normally zero. Sent so the client can
///     redraw the "filling up again" bar without a second call.
/// </param>
/// <param name="Cap">The ceiling that bar is filling toward.</param>
/// <param name="Player">The player's full state after the collection — the figure CP3 watches rise.</param>
public record CollectPropertyResponse(
    string PropertyId,
    long CashCollected,
    long Pending,
    long Cap,
    MeDto Player);
