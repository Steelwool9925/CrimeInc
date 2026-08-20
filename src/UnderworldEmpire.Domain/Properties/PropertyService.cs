namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// The rules of owning income property: what a player may buy, and what collecting pays them. Pure game
/// logic with no I/O — it mutates the objects handed to it and returns what happened. Persisting the
/// result is the caller's job.
/// </summary>
/// <remarks>
/// Stateless and RNG-free, like <see cref="Items.InventoryService"/> and unlike
/// <see cref="Jobs.JobService"/>: a property's price and its hourly income are figures the player plans
/// against over hours of real time, so rolling either would make the whole point of the mechanic — a
/// predictable trickle you come back for — unreadable.
/// </remarks>
public sealed class PropertyService
{
    /// <summary>
    /// Buys one unit of <paramref name="property"/> for <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// On success this mutates <paramref name="player"/> (cash down) and <paramref name="holding"/> (one
    /// more unit, income settled to <paramref name="nowUtc"/>). On refusal <b>nothing is mutated</b> —
    /// every check runs before the first write.
    /// </remarks>
    /// <param name="player">The buyer; mutated on success.</param>
    /// <param name="property">The property definition from content.</param>
    /// <param name="holding">
    ///     This player's holding for this property — freshly created when they own none yet. Mutated on
    ///     success.
    /// </param>
    /// <param name="nowUtc">The instant of the purchase; the income clock starts (or settles) here.</param>
    public PropertyPurchaseResult Purchase(
        Player player,
        Property property,
        PlayerProperty holding,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(holding);

        // ---- Validate before mutating anything ----

        var obstacle = CheckPurchase(player, property);
        if (obstacle != PropertyFailure.None)
            return PropertyPurchaseResult.Refused(obstacle);

        // ---- Apply ----

        player.SpendCash(property.Price);

        // AddUnits settles first, so the income the existing units earned at the old rate is banked
        // before the new one applies. A fresh holding simply starts its clock here.
        holding.AddUnits(property, 1, nowUtc);

        return PropertyPurchaseResult.Success(property.Price, holding.Quantity);
    }

    /// <summary>
    /// Asks what, if anything, stops <paramref name="player"/> buying <paramref name="property"/> right
    /// now, without buying it. Returns <see cref="PropertyFailure.None"/> when nothing does.
    /// </summary>
    /// <remarks>
    /// Exists so the property listing and the purchase itself cannot disagree: <see cref="Purchase"/>
    /// refuses on exactly this answer, so a client told it may buy something will not then be rejected.
    /// Same arrangement as <see cref="Items.InventoryService.CheckPurchase"/>.
    /// <para>
    /// Level is reported ahead of cash because it is the permanent obstacle.
    /// </para>
    /// </remarks>
    public PropertyFailure CheckPurchase(Player player, Property property)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(property);

        if (player.Level < property.LevelRequirement)
            return PropertyFailure.LevelTooLow;

        if (player.Cash < property.Price)
            return PropertyFailure.InsufficientCash;

        return PropertyFailure.None;
    }

    /// <summary>
    /// Collects everything <paramref name="holding"/> has earned, paying it into the player's cash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A collection that would pay nothing is <b>refused rather than applied</b>. Paying zero would be
    /// harmless to the balance but not to the holding: settling resets the clock, so a player tapping
    /// the button every second would keep discarding their own part-earned coin. Refusing early leaves
    /// the holding untouched, which is what makes repeated taps free.
    /// </para>
    /// <para>
    /// The cash is credited here rather than returned for the caller to apply, because a payout that a
    /// caller could forget to bank is a bug waiting to be written. The holding and the player are
    /// mutated together and persisted together.
    /// </para>
    /// </remarks>
    /// <param name="player">The owner; credited on success.</param>
    /// <param name="property">The property definition from content.</param>
    /// <param name="holding">This player's holding; emptied on success.</param>
    /// <param name="nowUtc">The instant to collect at.</param>
    public PropertyCollectResult Collect(
        Player player,
        Property property,
        PlayerProperty holding,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(holding);

        // ---- Validate before mutating anything ----

        var obstacle = CheckCollect(property, holding, nowUtc);
        if (obstacle != PropertyFailure.None)
            return PropertyCollectResult.Refused(obstacle);

        // ---- Apply ----

        long collected = holding.CollectAt(property, nowUtc);
        player.AddCash(collected);

        return PropertyCollectResult.Collected(
            collected,
            holding.PendingAt(property, nowUtc),
            holding.CapAt(property));
    }

    /// <summary>
    /// Asks what, if anything, stops <paramref name="holding"/> being collected right now, without
    /// collecting it. Returns <see cref="PropertyFailure.None"/> when nothing does.
    /// </summary>
    /// <remarks>
    /// The read-only twin of <see cref="Collect"/>, which refuses on exactly this answer — so the
    /// property listing and the collect button cannot disagree, and a client told why it may not
    /// collect gets the same token the write path would have returned. Same arrangement as
    /// <see cref="CheckPurchase"/> and <see cref="Items.InventoryService.CheckEquip"/>.
    /// <para>
    /// Takes no <see cref="Player"/>: collecting your own takings has no level or cash gate. It is
    /// the one property question that is about the holding alone.
    /// </para>
    /// <para>
    /// <b>Asking must stay free.</b> This reads through <see cref="PlayerProperty.PendingAt"/> rather
    /// than settling, so the listing — which a polling client calls repeatedly — never advances the
    /// income clock and never discards the part-earned coin.
    /// </para>
    /// </remarks>
    /// <param name="property">The property definition from content.</param>
    /// <param name="holding">This player's holding for that property.</param>
    /// <param name="nowUtc">The instant to evaluate at.</param>
    public PropertyFailure CheckCollect(Property property, PlayerProperty holding, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(holding);

        if (!holding.IsOwned)
            return PropertyFailure.NotOwned;

        if (holding.PendingAt(property, nowUtc) < 1)
            return PropertyFailure.NothingToCollect;

        return PropertyFailure.None;
    }
}
