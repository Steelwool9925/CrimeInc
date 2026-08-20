namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// The result of asking to buy a property: either a refusal with a reason, or what the purchase cost
/// and left the player holding.
/// </summary>
/// <remarks>
/// Same shape as <see cref="Items.ItemPurchaseResult"/> — one type with a <see cref="Failure"/>
/// discriminator rather than an exception, because being unable to afford something is expected play.
/// </remarks>
public sealed record PropertyPurchaseResult
{
    private PropertyPurchaseResult(PropertyFailure failure, long cashSpent, int quantityOwned)
    {
        Failure = failure;
        CashSpent = cashSpent;
        QuantityOwned = quantityOwned;
    }

    /// <summary>The refusal reason, or <see cref="PropertyFailure.None"/> on success.</summary>
    public PropertyFailure Failure { get; }

    /// <summary>Cash deducted, equal to the property's price. Zero on refusal.</summary>
    public long CashSpent { get; }

    /// <summary>Units the player holds after the purchase. Zero on refusal.</summary>
    public int QuantityOwned { get; }

    /// <summary>Whether the purchase went through.</summary>
    public bool Succeeded => Failure == PropertyFailure.None;

    /// <summary>Builds a refusal.</summary>
    public static PropertyPurchaseResult Refused(PropertyFailure failure)
    {
        if (failure == PropertyFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new PropertyPurchaseResult(failure, 0, 0);
    }

    /// <summary>Builds a success.</summary>
    public static PropertyPurchaseResult Success(long cashSpent, int quantityOwned) =>
        new(PropertyFailure.None, cashSpent, quantityOwned);
}

/// <summary>
/// The result of asking to collect a property's takings: either a refusal with a reason, or what was
/// paid out and where that leaves the holding.
/// </summary>
/// <remarks>
/// <see cref="PendingAfter"/> and <see cref="Cap"/> ride along with the payout because the client draws
/// its "filling up again" bar from both. Asking for them in a second call would render a figure that is
/// already a moment out of date, and would cost a second round trip on the most-tapped button in the
/// screen.
/// </remarks>
public sealed record PropertyCollectResult
{
    private PropertyCollectResult(PropertyFailure failure, long cashCollected, long pendingAfter, long cap)
    {
        Failure = failure;
        CashCollected = cashCollected;
        PendingAfter = pendingAfter;
        Cap = cap;
    }

    /// <summary>The refusal reason, or <see cref="PropertyFailure.None"/> on success.</summary>
    public PropertyFailure Failure { get; }

    /// <summary>Cash paid into the player's pocket. Zero on refusal.</summary>
    public long CashCollected { get; }

    /// <summary>What is still pending immediately after the collection — normally zero.</summary>
    public long PendingAfter { get; }

    /// <summary>The most this holding can bank before income stops, given the units owned.</summary>
    public long Cap { get; }

    /// <summary>Whether the collection went through.</summary>
    public bool Succeeded => Failure == PropertyFailure.None;

    /// <summary>Builds a refusal.</summary>
    public static PropertyCollectResult Refused(PropertyFailure failure)
    {
        if (failure == PropertyFailure.None)
            throw new ArgumentException("A refusal needs a reason other than None.", nameof(failure));

        return new PropertyCollectResult(failure, 0, 0, 0);
    }

    /// <summary>Builds a success.</summary>
    public static PropertyCollectResult Collected(long cashCollected, long pendingAfter, long cap) =>
        new(PropertyFailure.None, cashCollected, pendingAfter, cap);
}
