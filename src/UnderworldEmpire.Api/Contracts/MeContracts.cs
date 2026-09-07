namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// Snapshot of a single resource meter for the GET /me response.
/// All values reflect the lazy-regenned current state at request time — nothing is persisted.
/// </summary>
/// <param name="Current">The current (lazy-regenned) value.</param>
/// <param name="Max">The meter's maximum capacity.</param>
/// <param name="RegenSecondsPerPoint">Seconds required to regen one point.</param>
/// <param name="SecondsUntilNextPoint">
///     Seconds until the next whole point accrues, or <see langword="null"/> when the meter is full.
/// </param>
public record MeterState(int Current, int Max, int RegenSecondsPerPoint, int? SecondsUntilNextPoint);

/// <summary>Response body for GET /me — a complete projection of the authenticated player.</summary>
/// <param name="PlayerId">The stable player identifier.</param>
/// <param name="Energy">Current energy meter state.</param>
/// <param name="Stamina">Current stamina meter state.</param>
/// <param name="Health">Current health meter state.</param>
/// <param name="Cash">Soft in-game currency balance.</param>
/// <param name="PremiumCurrency">Hard premium currency balance.</param>
/// <param name="Level">Current level (leveling logic is M2).</param>
/// <param name="Xp">Accumulated experience points.</param>
/// <param name="SkillPoints">Unspent skill points available for allocation (M2+).</param>
/// <param name="Attack">Current attack stat.</param>
/// <param name="Defense">Current defense stat.</param>
/// <param name="Username">The player's claimed username, or <see langword="null"/> for a guest player who hasn't claimed an account.</param>
public record MeDto(
    Guid PlayerId,
    MeterState Energy,
    MeterState Stamina,
    MeterState Health,
    long Cash,
    long PremiumCurrency,
    int Level,
    long Xp,
    int SkillPoints,
    int Attack,
    int Defense,
    string? Username);
