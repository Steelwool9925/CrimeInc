using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Tests;

/// <summary>
/// A scripted <see cref="IRandomSource"/> so payout tests assert exact figures rather than ranges.
/// </summary>
/// <remarks>
/// Two modes. <see cref="Sequence"/> returns each supplied value in turn, clamped into the requested
/// range. <see cref="AlwaysMin"/> / <see cref="AlwaysMax"/> pin the roll to a boundary, which is how
/// the tests prove the inclusive contract: a range of 15..30 must be able to return both 15 and 30.
/// </remarks>
public sealed class FakeRandomSource : IRandomSource
{
    private readonly Queue<long>? _scripted;
    private readonly Mode _mode;

    private enum Mode { Scripted, Min, Max }

    private FakeRandomSource(Mode mode, IEnumerable<long>? values = null)
    {
        _mode = mode;
        _scripted = values is null ? null : new Queue<long>(values);
    }

    /// <summary>Returns the supplied values in order. Throws if exhausted — a silent wrap-around would hide a bug.</summary>
    public static FakeRandomSource Sequence(params long[] values) => new(Mode.Scripted, values);

    /// <summary>Always rolls the low end of the range.</summary>
    public static FakeRandomSource AlwaysMin() => new(Mode.Min);

    /// <summary>Always rolls the high end of the range.</summary>
    public static FakeRandomSource AlwaysMax() => new(Mode.Max);

    /// <summary>Number of rolls taken. Lets a test assert that exactly one roll happened per attempt.</summary>
    public int RollCount { get; private set; }

    /// <summary>The bounds of the most recent roll, for asserting the range the caller asked for.</summary>
    public (long Min, long Max)? LastRange { get; private set; }

    /// <summary>
    /// Every roll's bounds, in order. Since M3R an attempt takes two or three rolls, so
    /// <see cref="LastRange"/> alone can no longer say which range the *first* roll used.
    /// </summary>
    public List<(long Min, long Max)> Ranges { get; } = [];

    public long NextInclusive(long minInclusive, long maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), maxInclusive,
                $"maxInclusive must be >= minInclusive ({minInclusive}).");

        RollCount++;
        LastRange = (minInclusive, maxInclusive);
        Ranges.Add((minInclusive, maxInclusive));

        return _mode switch
        {
            Mode.Min => minInclusive,
            Mode.Max => maxInclusive,
            _ => NextScripted(minInclusive, maxInclusive)
        };
    }

    private long NextScripted(long minInclusive, long maxInclusive)
    {
        if (_scripted is null || _scripted.Count == 0)
            throw new InvalidOperationException(
                "FakeRandomSource ran out of scripted values — the code under test rolled more often than expected.");

        return Math.Clamp(_scripted.Dequeue(), minInclusive, maxInclusive);
    }
}
