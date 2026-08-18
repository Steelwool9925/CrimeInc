using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
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

            // Read-only: AsNoTracking avoids change-tracker work on a pure projection.
            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);

            if (player is null)
                return Results.NotFound();

            return Results.Ok(PlayerProjection.ToMeDto(player, DateTimeOffset.UtcNow));
        })
        .WithName("GetMe")
        .WithTags("Player");

        return app;
    }
}
