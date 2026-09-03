using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for claiming a persistent account on a device's player.</summary>
public static class AccountEndpoints
{
    /// <summary>Registers the account endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /account/register — claims the calling device's own player with a username and
        // password. There is no username/password in the URL: the caller is already
        // authenticated (via device token) as the player being claimed.
        app.MapPost("/account/register", [Authorize] async (
            RegisterAccountRequest request,
            HttpContext ctx,
            GameDbContext db) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
            if (player is null)
                return Results.NotFound();

            if (player.Username is not null)
            {
                return Results.Problem(
                    detail: "This device's player already has an account.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "alreadyClaimed" });
            }

            // Canonicalise before validating, so what's validated is exactly what gets stored.
            // request.Username/Password are declared as non-nullable `string`, but that
            // annotation is compile-time only — System.Text.Json's minimal-API body binder does
            // not enforce it at runtime, so a request that omits the field (or sends JSON `null`)
            // deserializes it to an actual null here. `?.ToLowerInvariant()` plus the `is null`
            // arms below turn that into the same ordinary usernameInvalid/passwordTooWeak
            // refusal every other malformed value gets, instead of an unhandled
            // NullReferenceException surfacing as a raw 500.
            var lowered = request.Username?.ToLowerInvariant();
            var password = request.Password;

            if (lowered is null || !AccountCredentials.IsValidUsername(lowered))
            {
                return Results.Problem(
                    detail: $"Username must be {AccountCredentials.MinUsernameLength}-" +
                        $"{AccountCredentials.MaxUsernameLength} characters, each a lowercase " +
                        "letter, digit, underscore or hyphen.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "usernameInvalid" });
            }

            if (password is null || !AccountCredentials.IsValidPassword(password))
            {
                return Results.Problem(
                    detail: $"Password must be at least {AccountCredentials.MinPasswordLength} " +
                        "characters.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "passwordTooWeak" });
            }

            player.Username = lowered;
            player.PasswordHash = PasswordHasher.Hash(password);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
            {
                // The check above raced with another request claiming the same username first —
                // exactly the check-then-insert race the unique index exists to catch.
                return Results.Problem(
                    detail: "That username is already in use.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "usernameTaken" });
            }

            return Results.Ok(PlayerProjection.ToMeDto(player, DateTimeOffset.UtcNow));
        })
        .WithName("PostAccountRegister")
        .WithTags("Account");

        // POST /account/login — exchanges a username/password for the account's device token.
        // Anonymous by design: this is how a caller obtains a bearer token in the first place.
        app.MapPost("/account/login", [AllowAnonymous] async (
            LoginRequest request,
            GameDbContext db) =>
        {
            // request.Username/Password are non-nullable `string` at compile time only — a
            // request that omits either field deserializes it to a real null at runtime (STJ
            // does not enforce C#'s nullable-reference-type annotations), so both are handled
            // as null-safe values feeding the same single refusal path below, never dereferenced
            // directly.
            var lowered = request.Username?.ToLowerInvariant();
            var password = request.Password;

            var player = lowered is null
                ? null
                : await db.Players.FirstOrDefaultAsync(p => p.Username == lowered);

            // Unknown username, missing username/password, missing hash, or wrong password must
            // all be indistinguishable to the caller — one shared refusal path, not separately
            // worded branches that could drift apart later.
            if (player is null || player.PasswordHash is null || password is null ||
                !PasswordHasher.Verify(password, player.PasswordHash))
            {
                return Results.Problem(
                    detail: "Invalid username or password.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "invalidCredentials" });
            }

            return Results.Ok(new LoginResponse(player.DeviceToken, player.Id));
        })
        .RequireRateLimiting("login")
        .WithName("PostAccountLogin")
        .WithTags("Account");

        return app;
    }
}
