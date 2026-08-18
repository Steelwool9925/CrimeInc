using Microsoft.AspNetCore.Authorization;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for spending skill points (T2.3).</summary>
public static class SkillEndpoints
{
    /// <summary>Registers the skill endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /skills/allocate — spend one skill point on a stat.
        app.MapPost("/skills/allocate", [Authorize] async (
            AllocateSkillRequest? request,
            HttpContext ctx,
            ProgressionService progression) =>
        {
            var playerId = ctx.User.GetPlayerId();

            // Parse the target at the boundary. Case-insensitive because it arrives as display-ish
            // text from a client, but the domain only ever sees a validated enum.
            //
            // Matched against the declared names rather than via Enum.TryParse, which also accepts
            // the underlying numeric values — {"target":"0"} would otherwise resolve to Attack and
            // "1" to Defense, and Enum.IsDefined cannot reject them because both are defined. The
            // API contract is the names, so only the names are accepted.
            var requested = request?.Target?.Trim();
            var matchedName = Enum.GetNames<SkillTarget>()
                .FirstOrDefault(n => string.Equals(n, requested, StringComparison.OrdinalIgnoreCase));

            if (matchedName is null)
            {
                var allowed = string.Join(", ", Enum.GetNames<SkillTarget>().Select(n => n.ToLowerInvariant()));
                return Results.Problem(
                    detail: $"Unknown skill target '{request?.Target}'. Expected one of: {allowed}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var target = Enum.Parse<SkillTarget>(matchedName);

            var (error, player) = await progression.AllocateAsync(playerId, target);

            return error switch
            {
                SkillAllocationError.UnknownPlayer => Results.NotFound(),

                SkillAllocationError.NoSkillPoints => Results.Problem(
                    detail: "You have no unspent skill points.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "noSkillPoints" }),

                SkillAllocationError.ConcurrencyConflict => Results.Problem(
                    detail: "Another request modified your state first. Re-read and try again.",
                    statusCode: StatusCodes.Status409Conflict),

                _ => Results.Ok(new AllocateSkillResponse(
                    PlayerProjection.ToMeDto(player!, DateTimeOffset.UtcNow)))
            };
        })
        .WithName("AllocateSkill")
        .WithTags("Progression");

        return app;
    }
}
