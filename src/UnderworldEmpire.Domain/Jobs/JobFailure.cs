namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// Why a job attempt was refused, or <see cref="None"/> when it succeeded.
/// </summary>
/// <remarks>
/// The names map onto the same token vocabulary <c>GET /jobs</c> reports in
/// <c>JobDto.UnavailableReason</c> (<c>"level"</c>, <c>"energy"</c>), so a client can handle a
/// refusal from either endpoint with one code path.
/// </remarks>
public enum JobFailure
{
    /// <summary>The attempt succeeded.</summary>
    None = 0,

    /// <summary>The player's level is below the job's requirement. A permanent refusal until they level.</summary>
    LevelTooLow,

    /// <summary>
    /// The player's <b>effective</b> attack or defense is below the job's minimum. Reported as
    /// <c>"stats"</c>.
    /// </summary>
    /// <remarks>
    /// A gate, not a penalty: nothing is spent, exactly like <see cref="LevelTooLow"/>. Unlike a
    /// level, this one can be cleared without levelling at all — equipping gear raises effective
    /// stats, which is the point of reading the loadout rather than the base stat.
    /// </remarks>
    StatsTooLow,

    /// <summary>
    /// The player holds less than the minimum quantity of one or more of the job's required items.
    /// Reported as <c>"items"</c>.
    /// </summary>
    /// <remarks>
    /// A gate, not a penalty: nothing is spent, exactly like <see cref="StatsTooLow"/>. Sits
    /// alongside the stat gate rather than folded into it — a player can meet stats and fail items,
    /// or the reverse, and these are different questions about the same job.
    /// </remarks>
    InsufficientItems,

    /// <summary>The player has less energy than the job costs. A temporary refusal — energy regenerates.</summary>
    InsufficientEnergy
}
