namespace UnderworldEmpire.Domain;

/// <summary>
/// Default regeneration rates for each resource meter — one point per interval.
/// These are tunable balance values; content/config-driven delivery is planned for M2+.
/// </summary>
public static class MeterRates
{
    /// <summary>
    /// Energy regenerates one point every 90 seconds — a full 10-point bar refills from empty in
    /// 15 minutes, matching a first-time player's first session rather than making them wait out
    /// the original 5-minutes-a-point rate (50 minutes for a full refill).
    /// </summary>
    public static readonly TimeSpan Energy = TimeSpan.FromSeconds(90);

    /// <summary>Stamina regenerates one point every five minutes.</summary>
    public static readonly TimeSpan Stamina = TimeSpan.FromMinutes(5);

    /// <summary>Health regenerates one point every minute.</summary>
    public static readonly TimeSpan Health = TimeSpan.FromMinutes(1);
}
