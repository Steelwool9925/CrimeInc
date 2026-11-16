namespace UnderworldEmpire.Domain.Daily;

/// <summary>
/// The one definition of "today" for everything that resets daily: the UTC calendar date. Server-side
/// and clock-independent of the client, so two daily features can never disagree on when a day starts.
/// </summary>
public static class UtcDay
{
    /// <summary>The UTC calendar date containing <paramref name="instant"/>.</summary>
    public static DateOnly Of(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);

    /// <summary>The instant the next UTC day begins, for a "resets in" countdown.</summary>
    public static DateTimeOffset NextStart(DateTimeOffset instant) =>
        new(Of(instant).AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
