using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for guest identity and session management (T1.2).</summary>
public static class SessionEndpoints
{
    /// <summary>Registers the session endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /session — creates or resumes a guest session.
        app.MapPost("/session", [AllowAnonymous] async (
            SessionRequest? request,
            GameDbContext db) =>
        {
            var now = DateTimeOffset.UtcNow;

            // Try to resume an existing session when the caller supplies a token.
            if (!string.IsNullOrEmpty(request?.DeviceToken))
            {
                var existing = await db.Players
                    .FirstOrDefaultAsync(p => p.DeviceToken == request.DeviceToken);

                if (existing is not null)
                    return Results.Ok(new SessionResponse(existing.DeviceToken, existing.Id));
            }

            // No match or no token supplied — create a new player.
            var player = new Player
            {
                CreatedAtUtc = now,
                DeviceToken = DeviceTokenGenerator.Generate(),
                Energy = Meter.Full(PlayerDefaults.EnergyMax, now),
                Stamina = Meter.Full(PlayerDefaults.StaminaMax, now),
                Health = Meter.Full(PlayerDefaults.HealthMax, now),
                Cash = PlayerDefaults.StartingCash,
                PremiumCurrency = PlayerDefaults.StartingPremiumCurrency
            };

            db.Players.Add(player);
            await db.SaveChangesAsync();

            return Results.Ok(new SessionResponse(player.DeviceToken, player.Id));
        })
        .WithName("PostSession")
        .WithTags("Session");

        // GET /whoami — auth probe; returns the authenticated player's id.
        app.MapGet("/whoami", [Authorize] (HttpContext ctx) =>
        {
            var playerId = ctx.User.GetPlayerId();
            return Results.Ok(new { playerId });
        })
        .WithName("WhoAmI")
        .WithTags("Session");

        return app;
    }
}
