namespace UnderworldEmpire.Domain;

/// <summary>
/// A game account. Deliberately minimal at scaffold time (S0.2) — just enough to
/// produce the first EF migration. Extended in M1 (S1.1) with resource meters
/// (energy / stamina / health); device-token identity, currencies, and level/XP/stats
/// follow in later sessions.
/// </summary>
public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// The guest bearer credential. Always server-generated via
    /// <c>DeviceTokenGenerator</c>; clients never supply their own token.
    /// </summary>
    public string DeviceToken { get; set; } = string.Empty;

    /// <summary>Action-gated resource; regenerates at <see cref="MeterRates.Energy"/>.</summary>
    public Meter Energy { get; set; } =
        Meter.Full(PlayerDefaults.EnergyMax, DateTimeOffset.UtcNow);

    /// <summary>Combat-gated resource; regenerates at <see cref="MeterRates.Stamina"/>.</summary>
    public Meter Stamina { get; set; } =
        Meter.Full(PlayerDefaults.StaminaMax, DateTimeOffset.UtcNow);

    /// <summary>Vitality resource; regenerates at <see cref="MeterRates.Health"/>.</summary>
    public Meter Health { get; set; } =
        Meter.Full(PlayerDefaults.HealthMax, DateTimeOffset.UtcNow);

    /// <summary>Soft in-game currency. Display label supplied by the content pack.</summary>
    public long Cash { get; set; } = PlayerDefaults.StartingCash;

    /// <summary>Hard premium currency. Display label supplied by the content pack.</summary>
    public long PremiumCurrency { get; set; } = PlayerDefaults.StartingPremiumCurrency;

    /// <summary>Increases <see cref="Cash"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void AddCash(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        Cash += amount;
    }

    /// <summary>Decreases <see cref="Cash"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="amount"/> exceeds <see cref="Cash"/>.</exception>
    public void SpendCash(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        if (amount > Cash)
            throw new InvalidOperationException("insufficient cash");
        Cash -= amount;
    }

    /// <summary>Increases <see cref="PremiumCurrency"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void AddPremiumCurrency(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        PremiumCurrency += amount;
    }

    /// <summary>Decreases <see cref="PremiumCurrency"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="amount"/> exceeds <see cref="PremiumCurrency"/>.</exception>
    public void SpendPremiumCurrency(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        if (amount > PremiumCurrency)
            throw new InvalidOperationException("insufficient premium currency");
        PremiumCurrency -= amount;
    }
}
