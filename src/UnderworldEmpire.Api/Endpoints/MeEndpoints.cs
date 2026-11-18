using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

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
            GameDbContext db,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();

            // Read-only: AsNoTracking avoids change-tracker work on a pure projection.
            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);

            if (player is null)
                return Results.NotFound();

            // A second read, only for the one field group that needs it: whether an appointment
            // is running. Null for the common case of nothing running.
            var timedJob = await db.PlayerTimedJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.PlayerId == playerId);
            var timedJobContent = timedJob is not null
                ? content.TimedJobCatalog.Find(timedJob.TimedJobId)
                : null;

            return Results.Ok(PlayerProjection.ToMeDto(player, DateTimeOffset.UtcNow, timedJob, timedJobContent));
        })
        .WithName("GetMe")
        .WithTags("Player");

        return app;
    }
}
