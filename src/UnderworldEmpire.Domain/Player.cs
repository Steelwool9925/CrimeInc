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

    /// <summary>Action-gated resource; regenerates at <see cref="MeterRates.Energy"/>.</summary>
    public Meter Energy { get; set; } =
        Meter.Full(PlayerDefaults.EnergyMax, DateTimeOffset.UtcNow);

    /// <summary>Combat-gated resource; regenerates at <see cref="MeterRates.Stamina"/>.</summary>
    public Meter Stamina { get; set; } =
        Meter.Full(PlayerDefaults.StaminaMax, DateTimeOffset.UtcNow);

    /// <summary>Vitality resource; regenerates at <see cref="MeterRates.Health"/>.</summary>
    public Meter Health { get; set; } =
        Meter.Full(PlayerDefaults.HealthMax, DateTimeOffset.UtcNow);
}
