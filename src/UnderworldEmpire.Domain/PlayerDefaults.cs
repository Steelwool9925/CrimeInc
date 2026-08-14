namespace UnderworldEmpire.Domain;

/// <summary>
/// Default capacity constants for new <see cref="Player"/> instances.
/// New players start with every meter at full. Adjust these (and balance
/// via <see cref="MeterRates"/>) as game-design requirements evolve.
/// </summary>
public static class PlayerDefaults
{
    /// <summary>Default maximum Energy for a new player.</summary>
    public const int EnergyMax = 10;

    /// <summary>Default maximum Stamina for a new player.</summary>
    public const int StaminaMax = 5;

    /// <summary>Default maximum Health for a new player.</summary>
    public const int HealthMax = 100;
}
