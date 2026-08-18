namespace UnderworldEmpire.Domain.Randomness;

/// <summary>
/// The server's source of randomness for game outcomes. Exists so that
/// <see cref="UnderworldEmpire.Domain"/> stays free of I/O and non-determinism while payout rolls
/// remain exactly assertable in tests: production supplies a real RNG, tests supply a scripted
/// sequence.
/// </summary>
/// <remarks>
/// All rolls happen server-side. The client never sees, seeds or influences a roll — that is the
/// anti-cheat rule the whole architecture is built around.
/// </remarks>
public interface IRandomSource
{
    /// <summary>
    /// Returns a value between <paramref name="minInclusive"/> and <paramref name="maxInclusive"/>,
    /// <b>both ends included</b>.
    /// </summary>
    /// <remarks>
    /// Two deliberate choices:
    /// <list type="bullet">
    /// <item>
    ///     The upper bound is <b>inclusive</b>, because content payout ranges in <c>jobs.json</c>
    ///     are written as inclusive. An off-by-one here would silently make each job's best
    ///     outcome unreachable — a bug no test would notice without asserting the exact boundary.
    /// </item>
    /// <item>
    ///     The type is <see cref="long"/> to match <c>Player.Cash</c> and <c>Job.CashMin/CashMax</c>.
    ///     An <see cref="int"/> signature would force a narrowing cast at every payout site.
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="minInclusive">Lower bound, included in the result range.</param>
    /// <param name="maxInclusive">Upper bound, included in the result range.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxInclusive"/> is less than <paramref name="minInclusive"/>.
    /// </exception>
    long NextInclusive(long minInclusive, long maxInclusive);
}
