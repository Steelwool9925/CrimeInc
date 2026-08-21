using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for the job catalog and job execution (T2.1 / T2.2).</summary>
public static class JobEndpoints
{
    /// <summary>Registers the job endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /jobs — the catalog as offered to the calling player, with their mastery and
        // affordability folded in. Authenticated (unlike GET /content/pack) precisely because the
        // response is player-specific.
        app.MapGet("/jobs", [Authorize] async (
            HttpContext ctx,
            GameDbContext db,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();

            // Read-only projection, so both reads are AsNoTracking — no change tracker overhead and
            // no risk of an accidental write from a GET.
            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);

            if (player is null)
                return Results.NotFound();

            var catalog = content.JobCatalog;

            // One query for this player's mastery rows, keyed for O(1) lookup while projecting.
            // Loading only this player's rows (not a join per job) keeps GET /jobs at two queries
            // total regardless of how many jobs the pack defines.
            var masteryByJobId = await db.PlayerJobMasteries
                .AsNoTracking()
                .Where(m => m.PlayerId == playerId)
                .ToDictionaryAsync(m => m.JobId, StringComparer.Ordinal);

            // The stat gate and the reported odds both read effective stats, so the equipped
            // holdings are resolved here too. A third query on this route, and the reason equipping
            // a weapon visibly changes the job list.
            var equipped = await db.PlayerItems
                .AsNoTracking()
                .Where(h => h.PlayerId == playerId && h.Equipped)
                .ToListAsync();

            var loadout = Loadout.For(player, content.ItemCatalog.ResolveEquipped(equipped));

            var now = DateTimeOffset.UtcNow;
            int currentEnergy = player.Energy.CurrentAt(now, MeterRates.Energy);

            var jobs = new List<JobDto>(catalog.Jobs.Count);
            foreach (var job in catalog.Jobs)
            {
                masteryByJobId.TryGetValue(job.Id, out var mastery);

                int completions = mastery?.Completions ?? 0;
                int rank = mastery?.Rank ?? 0;
                int? untilNext = mastery is not null
                    ? mastery.CompletionsUntilNextRank(catalog.CompletionsPerRank, catalog.MaxRank)
                    : UntilNextRankForFreshJob(catalog);

                // Precedence is level -> stats -> energy, the same order JobService.Attempt refuses
                // in: the most permanent reason first, so a row failing several gates shows the one
                // that will take longest to clear. A mismatch here would let a row look attemptable
                // and then be refused for a reason the client never displayed.
                string? unavailableReason = player.Level < job.LevelRequirement
                    ? "level"
                    : !job.MeetsStatRequirements(loadout.TotalAttack, loadout.TotalDefense)
                        ? "stats"
                        : currentEnergy < job.EnergyCost
                            ? "energy"
                            : null;

                jobs.Add(new JobDto(
                    Id: job.Id,
                    Name: job.Name,
                    Description: job.Description,
                    Area: job.Area,
                    EnergyCost: job.EnergyCost,
                    LevelRequirement: job.LevelRequirement,
                    CashMin: job.CashMin,
                    CashMax: job.CashMax,
                    Xp: job.Xp,
                    AttackRequirement: job.AttackRequirement,
                    DefenseRequirement: job.DefenseRequirement,
                    // The same method the attempt rolls against — a second implementation here
                    // would let the displayed odds drift from the real ones.
                    SuccessChance: job.SuccessChanceFor(
                        loadout.TotalAttack, loadout.TotalDefense, catalog.MaxSuccessChance),
                    Mastery: new JobMasteryState(
                        Completions: completions,
                        Rank: rank,
                        MaxRank: catalog.MaxRank,
                        CompletionsUntilNextRank: untilNext),
                    CanAttempt: unavailableReason is null,
                    UnavailableReason: unavailableReason));
            }

            return Results.Ok(new JobListResponse(jobs, currentEnergy, player.Level));
        })
        .WithName("GetJobs")
        .WithTags("Jobs");

        // POST /jobs/{id}/do — perform a job. Every figure in the response is decided server-side;
        // the request carries no body at all, so there is nothing for a client to tamper with.
        app.MapPost("/jobs/{id}/do", [Authorize] async (
            string id,
            HttpContext ctx,
            JobActionService jobActions,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var result = await jobActions.PerformAsync(playerId, id, DateTimeOffset.UtcNow);

            switch (result.Error)
            {
                case JobActionError.UnknownJob:
                    return Results.Problem(
                        detail: $"No job with id '{id}' exists in the active content pack.",
                        statusCode: StatusCodes.Status404NotFound);

                case JobActionError.UnknownPlayer:
                    return Results.NotFound();

                case JobActionError.ConcurrencyConflict:
                    // Deliberately not retried — a retry would re-roll the payout and re-spend
                    // energy, which is the duplicate the concurrency token exists to prevent.
                    return Results.Problem(
                        detail: "Another request modified your state first. Re-read and try again.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            // Only a *refusal* lands here. A failed attempt was applied — energy spent, consolation
            // paid — so it carries on to the 200 below and reports itself through result.succeeded.
            if (!result.WasApplied)
            {
                // Same token vocabulary GET /jobs uses in unavailableReason, so a client handles a
                // refusal from either endpoint with one code path.
                var (reason, detail) = result.Failure switch
                {
                    JobFailure.LevelTooLow => ("level", "Your level is too low for this job."),
                    JobFailure.StatsTooLow => ("stats", "Your attack or defense is too low for this job."),
                    JobFailure.InsufficientEnergy => ("energy", "You do not have enough energy for this job."),
                    _ => ("unknown", "The job could not be performed.")
                };

                return Results.Problem(
                    detail: detail,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = reason });
            }

            var outcome = result.Outcome!;
            var catalog = content.JobCatalog;

            var levelUp = result.LevelUp is { LeveledUp: true } lvl
                ? new LevelUpDto(lvl.LevelsGained, lvl.NewLevel, lvl.SkillPointsGranted, lvl.MetersRefilled)
                : null;

            return Results.Ok(new DoJobResponse(
                Result: new JobResultDto(
                    JobId: outcome.JobId,
                    Succeeded: outcome.Succeeded,
                    EnergySpent: outcome.EnergySpent,
                    CashEarned: outcome.CashEarned,
                    XpEarned: outcome.XpEarned,
                    HealthLost: outcome.HealthLost,
                    Mastery: new JobMasteryState(
                        Completions: outcome.Completions,
                        Rank: outcome.MasteryRank,
                        MaxRank: catalog.MaxRank,
                        CompletionsUntilNextRank: outcome.CompletionsUntilNextRank),
                    MasteryRankedUp: outcome.MasteryRankedUp),
                Player: PlayerProjection.ToMeDto(result.Player!, DateTimeOffset.UtcNow),
                LevelUp: levelUp));
        })
        .WithName("DoJob")
        .WithTags("Jobs");

        return app;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Completions to the first rank for a job the player has never attempted. Kept separate from
    /// the persisted path so a never-attempted job reports the same shape as an in-progress one,
    /// rather than a null the client would have to special-case.
    /// </summary>
    private static int? UntilNextRankForFreshJob(JobCatalog catalog) =>
        catalog.MaxRank >= 1 ? catalog.CompletionsPerRank : null;
}
