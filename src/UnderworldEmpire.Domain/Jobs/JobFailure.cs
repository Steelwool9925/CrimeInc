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

    /// <summary>The player has less energy than the job costs. A temporary refusal — energy regenerates.</summary>
    InsufficientEnergy
}
