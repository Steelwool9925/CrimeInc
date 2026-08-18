using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Infrastructure.Randomness;

/// <summary>
/// The production <see cref="IRandomSource"/>, backed by <see cref="Random.Shared"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Random.Shared"/> is thread-safe and needs no seeding or locking, which suits a
/// singleton registration on a server handling concurrent requests.
/// </para>
/// <para>
/// Deliberately <b>not</b> a cryptographic RNG. A payout roll only needs to be unpredictable to a
/// player who cannot observe it at all — the roll happens server-side and only its result is
/// returned. <c>DeviceTokenGenerator</c> uses <c>RandomNumberGenerator</c> because a bearer
/// credential genuinely must resist guessing; a cash payout does not, and paying the crypto-RNG
/// cost per roll would buy nothing.
/// </para>
/// </remarks>
public sealed class SystemRandomSource : IRandomSource
{
    /// <inheritdoc />
    public long NextInclusive(long minInclusive, long maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), maxInclusive,
                $"maxInclusive must be >= minInclusive ({minInclusive}).");

        // NextInt64 takes an exclusive upper bound, so widen by one to honour the inclusive
        // contract. At long.MaxValue that increment would overflow; there the exclusive call is
        // used instead, making that single value unreachable. Content payout ranges are orders of
        // magnitude smaller, so this branch is unreachable in practice — it exists so the method
        // cannot overflow rather than to be relied on.
        return maxInclusive == long.MaxValue
            ? Random.Shared.NextInt64(minInclusive, maxInclusive)
            : Random.Shared.NextInt64(minInclusive, maxInclusive + 1);
    }
}
