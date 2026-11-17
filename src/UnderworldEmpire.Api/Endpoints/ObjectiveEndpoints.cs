using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for daily objectives.</summary>
public static class ObjectiveEndpoints
{
    /// <summary>Registers the objective endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapObjectiveEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /objectives - today's set for the calling player. Until the player's first action of the
        // day the set is computed from (player, UTC date, level) and nothing is stored; that first
        // action stores a row per offered objective, which fixes the set for the rest of the day. So
        // this performs no writes, and there is no scheduled job to run at midnight.
        app.MapGet("/objectives", [Authorize] async (
            HttpContext ctx,
            GameDbContext db,
            DailyObjectiveActionService objectives) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var level = await db.Players
                .AsNoTracking()
                .Where(p => p.Id == playerId)
                .Select(p => (int?)p.Level)
                .FirstOrDefaultAsync();

            if (level is null)
                return Results.NotFound();

            var now = DateTimeOffset.UtcNow;
            var view = await objectives.ViewAsync(playerId, level.Value, now);

            return Results.Ok(new ObjectivesResponse(
                UtcDay.Of(now).ToString("yyyy-MM-dd"), UtcDay.NextStart(now), ToDtos(view)!));
        })
        .WithName("GetObjectives")
        .WithTags("Objectives");

        // POST /objectives/{id}/claim - take the reward for a finished objective, exactly once.
        app.MapPost("/objectives/{id}/claim", [Authorize] async (
            string id,
            HttpContext ctx,
            DailyObjectiveActionService objectives,
            IContentPackProvider content,
            GameDbContext db) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var now = DateTimeOffset.UtcNow;

            var result = await objectives.ClaimAsync(playerId, id, now);

            switch (result.Error)
            {
                case ObjectiveActionError.UnknownObjective:
                    return Results.Problem(
                        detail: $"No objective with id '{id}' exists in the active content pack.",
                        statusCode: StatusCodes.Status404NotFound);

                case ObjectiveActionError.UnknownPlayer:
                    return Results.NotFound();

                case ObjectiveActionError.ConcurrencyConflict:
                    // Not retried: the other request may have been the claim that paid.
                    return Results.Problem(
                        detail: "Another request modified your state first. Re-read and try again.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            if (!result.WasApplied)
            {
                var (reason, detail) = result.Failure switch
                {
                    ObjectiveFailure.NotToday => ("notToday", "That objective is not one of yours today."),
                    ObjectiveFailure.NotComplete => ("notComplete", "That objective is not finished yet."),
                    ObjectiveFailure.AlreadyClaimed => ("alreadyClaimed", "You already took that reward today."),
                    _ => ("unknown", "The objective could not be claimed.")
                };

                return Results.Problem(
                    detail: detail,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = reason });
            }

            var all = ToDtos(result.Objectives)!;
            var streak = await DailyEndpoints.LoadStreakAsync(db, playerId);
            return Results.Ok(new ClaimObjectiveResponse(
                Claimed: all.First(o => o.Id == id),
                Player: PlayerProjection.ToMeDto(result.Player!, now, streak),
                LevelUp: LevelUpProjection.ToDto(result.LevelUp, content.JobCatalog),
                Objectives: all));
        })
        .WithName("ClaimObjective")
        .WithTags("Objectives");

        return app;
    }

    internal static string TypeToken(ObjectiveType type) =>
        char.ToLowerInvariant(type.ToString()[0]) + type.ToString()[1..];

    internal static ObjectiveDto ToDto(ObjectiveState state)
    {
        var o = state.Objective;
        var capped = Math.Min(state.Progress, o.Target);
        return new ObjectiveDto(
            o.Id, o.Name, o.Description, TypeToken(o.Type), o.Target, o.JobId,
            capped, capped >= o.Target, state.Claimed, o.RewardCash, o.RewardXp);
    }

    /// <summary>Projects a view to the wire, passing a missing view through as <see langword="null"/>.</summary>
    internal static IReadOnlyList<ObjectiveDto>? ToDtos(IReadOnlyList<ObjectiveState>? view) =>
        view?.Select(ToDto).ToList();
}
