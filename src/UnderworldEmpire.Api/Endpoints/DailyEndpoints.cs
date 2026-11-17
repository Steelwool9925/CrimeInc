using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for the daily login streak.</summary>
public static class DailyEndpoints
{
    /// <summary>Registers the daily endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapDailyEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /daily — today's claimable state. Writes nothing.
        app.MapGet("/daily", [Authorize] async (
            HttpContext ctx,
            DailyActionService daily) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var now = DateTimeOffset.UtcNow;

            var status = await daily.ViewAsync(playerId, now);
            if (status is null)
                return Results.NotFound();

            return Results.Ok(new DailyStatusResponse(
                status.Claimable, status.CurrentStreak, status.LongestStreak,
                ToDto(status.NextReward), UtcDay.NextStart(now)));
        })
        .WithName("GetDaily")
        .WithTags("Daily");

        // POST /daily/claim — take today's reward, exactly once per UTC day.
        app.MapPost("/daily/claim", [Authorize] async (
            HttpContext ctx,
            DailyActionService daily,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var now = DateTimeOffset.UtcNow;

            var result = await daily.ClaimAsync(playerId, now);

            switch (result.Error)
            {
                case DailyActionError.UnknownPlayer:
                    return Results.NotFound();

                case DailyActionError.ConcurrencyConflict:
                    // Not retried: the other request may have been the claim that paid.
                    return Results.Problem(
                        detail: "Another request modified your state first. Re-read and try again.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            if (!result.WasApplied)
            {
                return Results.Problem(
                    detail: "You already claimed today's reward.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "alreadyClaimedToday" });
            }

            return Results.Ok(new ClaimDailyResponse(
                Reward: ToDto(result.Reward!),
                CurrentStreak: result.Streak!.CurrentStreak,
                LongestStreak: result.Streak.LongestStreak,
                Player: PlayerProjection.ToMeDto(result.Player!, now, result.Streak),
                LevelUp: LevelUpProjection.ToDto(result.LevelUp, content.JobCatalog)));
        })
        .WithName("ClaimDaily")
        .WithTags("Daily");

        return app;
    }

    /// <summary>
    /// Reads <paramref name="playerId"/>'s daily-login streak row, or <see langword="null"/> when
    /// they have never claimed. Shared by every endpoint that builds a <see cref="MeDto"/> via
    /// <see cref="PlayerProjection.ToMeDto"/> and needs the streak to pass it, not just the daily
    /// endpoints themselves.
    /// </summary>
    public static Task<DailyStreak?> LoadStreakAsync(
        GameDbContext db, Guid playerId, CancellationToken cancellationToken = default) =>
        db.DailyStreaks.AsNoTracking().FirstOrDefaultAsync(d => d.PlayerId == playerId, cancellationToken);

    private static DailyRewardDto ToDto(DailyReward reward) => new(reward.Day, reward.Cash, reward.Xp);
}
