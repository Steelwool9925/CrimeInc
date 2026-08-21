using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Tests;

/// <summary>
/// The <see cref="IRandomSource"/> the test host registers: a singleton whose behaviour a test can
/// swap for the duration of one case.
/// </summary>
/// <remarks>
/// <para>
/// The host builds its service provider once per test class, so the RNG cannot be registered
/// per-test. Without this, an endpoint test could only ever see whichever branch the fixed source
/// produced — and since M3R that means the failure branch would have no endpoint coverage at all,
/// because the default source always succeeds.
/// </para>
/// <para>
/// Safe because xUnit runs the cases within one class sequentially and each class gets its own
/// factory via <c>IClassFixture</c>. A test that swaps the inner source must put it back, which is
/// what <see cref="Scripted"/> is for.
/// </para>
/// </remarks>
public sealed class SwitchableRandomSource : IRandomSource
{
    /// <summary>
    /// The source actually consulted. Defaults to always-minimum, which makes the success roll 1 —
    /// clearing any chance above zero — and pins every payout to its floor.
    /// </summary>
    public IRandomSource Inner { get; set; } = FakeRandomSource.AlwaysMin();

    public long NextInclusive(long minInclusive, long maxInclusive) =>
        Inner.NextInclusive(minInclusive, maxInclusive);

    /// <summary>
    /// Points this source at a scripted sequence until the returned scope is disposed, then restores
    /// whatever was there before.
    /// </summary>
    public IDisposable Scripted(params long[] values)
    {
        var previous = Inner;
        Inner = FakeRandomSource.Sequence(values);
        return new Restore(this, previous);
    }

    private sealed class Restore(SwitchableRandomSource owner, IRandomSource previous) : IDisposable
    {
        public void Dispose() => owner.Inner = previous;
    }
}
