using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// Builds the wire projection of a <see cref="Player"/>. Shared by GET /me and by the action
/// endpoints that return post-action state.
/// </summary>
/// <remarks>
/// Extracted from <c>MeEndpoints</c> when POST /jobs/{id}/do began returning the same shape. The
/// alternative — a second copy of the regen countdown arithmetic — is the kind of duplication that
/// drifts silently: the two would agree until one was tuned, and the disagreement would surface as a
/// countdown that jumps when the player does a job.
/// </remarks>
public static class PlayerProjection
{
    /// <summary>Projects <paramref name="player"/> as of <paramref name="nowUtc"/>.</summary>
    public static MeDto ToMeDto(Player player, DateTimeOffset nowUtc) =>
        new(
            PlayerId: player.Id,
            Energy: ToMeterState(player.Energy, nowUtc, MeterRates.Energy),
            Stamina: ToMeterState(player.Stamina, nowUtc, MeterRates.Stamina),
            Health: ToMeterState(player.Health, nowUtc, MeterRates.Health),
            Cash: player.Cash,
            PremiumCurrency: player.PremiumCurrency,
            Level: player.Level,
            Xp: player.Xp,
            SkillPoints: player.SkillPoints,
            Attack: player.Attack,
            Defense: player.Defense);

    /// <summary>
    /// Builds a <see cref="MeterState"/> for <paramref name="meter"/>, computing the lazily-regenned
    /// current value and the seconds until the next whole point accrues.
    /// </summary>
    public static MeterState ToMeterState(Meter meter, DateTimeOffset nowUtc, TimeSpan regenPerPoint)
    {
        int current = meter.CurrentAt(nowUtc, regenPerPoint);
        int regenSeconds = (int)regenPerPoint.TotalSeconds;

        int? secondsUntilNext = null;
        if (current < meter.Max)
        {
            // Ticks elapsed since the last store snapshot (or zero if the clock went backwards).
            long elapsedTicks = Math.Max(0L, (nowUtc - meter.StoredAtUtc).Ticks);
            long regenTicks = regenPerPoint.Ticks;
            // How many ticks have been consumed by already-earned whole points?
            long ticksConsumedByWhole = (elapsedTicks / regenTicks) * regenTicks;
            long ticksIntoCurrentPoint = elapsedTicks - ticksConsumedByWhole;
            long ticksRemaining = regenTicks - ticksIntoCurrentPoint;
            secondsUntilNext = (int)Math.Ceiling(TimeSpan.FromTicks(ticksRemaining).TotalSeconds);
        }

        return new MeterState(current, meter.Max, regenSeconds, secondsUntilNext);
    }
}
