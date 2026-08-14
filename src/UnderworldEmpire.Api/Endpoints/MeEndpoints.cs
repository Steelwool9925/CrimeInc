using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for the authenticated player's own profile (T1.5).</summary>
public static class MeEndpoints
{
    /// <summary>Registers the /me endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /me — returns a complete projection of the calling player.
        // Meters are lazy-regenned at request time; nothing is persisted.
        app.MapGet("/me", [Authorize] async (
            HttpContext ctx,
            GameDbContext db) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
            if (player is null)
                return Results.NotFound();

            var now = DateTimeOffset.UtcNow;

            return Results.Ok(new MeDto(
                PlayerId: player.Id,
                Energy: BuildMeterState(player.Energy, now, MeterRates.Energy),
                Stamina: BuildMeterState(player.Stamina, now, MeterRates.Stamina),
                Health: BuildMeterState(player.Health, now, MeterRates.Health),
                Cash: player.Cash,
                PremiumCurrency: player.PremiumCurrency,
                Level: player.Level,
                Xp: player.Xp,
                SkillPoints: player.SkillPoints,
                Attack: player.Attack,
                Defense: player.Defense));
        })
        .WithName("GetMe")
        .WithTags("Player");

        return app;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="MeterState"/> for the given <paramref name="meter"/>, computing the
    /// lazy-regenned current value and the seconds until the next whole point accrues.
    /// </summary>
    private static MeterState BuildMeterState(Meter meter, DateTimeOffset now, TimeSpan regenPerPoint)
    {
        int current = meter.CurrentAt(now, regenPerPoint);
        int regenSeconds = (int)regenPerPoint.TotalSeconds;

        int? secondsUntilNext = null;
        if (current < meter.Max)
        {
            // Ticks elapsed since the last store snapshot (or zero if clock went backwards).
            long elapsedTicks = Math.Max(0L, (now - meter.StoredAtUtc).Ticks);
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
