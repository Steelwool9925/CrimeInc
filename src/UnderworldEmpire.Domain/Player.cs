namespace UnderworldEmpire.Domain;

/// <summary>
/// A game account. Deliberately minimal at scaffold time (S0.2) — just enough to
/// produce the first EF migration. Extended in M1 with device-token identity,
/// resource meters (energy/stamina/health), currencies, and level/XP/stats.
/// </summary>
public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; init; }
}
