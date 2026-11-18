using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Timed;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for timed-job ("appointment") start/collect (T-overnight-timed-jobs M1).</summary>
public static class TimedJobEndpoints
{
    /// <summary>Registers the timed-job endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapTimedJobEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /timed-jobs — the catalog as offered to the calling player, plus their one running
        // slot if occupied.
        app.MapGet("/timed-jobs", [Authorize] async (
            HttpContext ctx,
            GameDbContext db,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);
            if (player is null)
                return Results.NotFound();

            var running = await db.PlayerTimedJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.PlayerId == playerId);

            var now = DateTimeOffset.UtcNow;
            int currentEnergy = player.Energy.CurrentAt(now, MeterRates.Energy);

            var catalog = content.TimedJobCatalog;
            var timedJobs = new List<TimedJobDto>(catalog.TimedJobs.Count);
            foreach (var job in catalog.TimedJobs)
            {
                // Precedence is alreadyRunning -> level -> energy: a slot already occupied blocks
                // starting ANY appointment, so it is the most informative reason and is checked
                // first, ahead of reasons specific to this one row.
                string? unavailableReason = running is not null
                    ? "alreadyRunning"
                    : player.Level < job.LevelRequirement
                        ? "level"
                        : currentEnergy < job.EnergyCost
                            ? "energy"
                            : null;

                timedJobs.Add(new TimedJobDto(
                    Id: job.Id,
                    Name: job.Name,
                    Description: job.Description,
                    DurationSeconds: job.DurationSeconds,
                    EnergyCost: job.EnergyCost,
                    LevelRequirement: job.LevelRequirement,
                    CashMin: job.CashMin,
                    CashMax: job.CashMax,
                    Xp: job.Xp,
                    CanStart: unavailableReason is null,
                    UnavailableReason: unavailableReason));
            }

            TimedJobRunningDto? runningDto = null;
            if (running is not null)
            {
                var runningJob = catalog.Find(running.TimedJobId);
                if (runningJob is not null)
                {
                    var duration = TimeSpan.FromSeconds(runningJob.DurationSeconds);
                    runningDto = new TimedJobRunningDto(
                        TimedJobId: running.TimedJobId,
                        Name: runningJob.Name,
                        StartedAtUtc: running.StartedAtUtc,
                        RemainingSeconds: running.RemainingAt(now, duration),
                        Ready: running.IsReadyAt(now, duration));
                }
            }

            return Results.Ok(new TimedJobListResponse(timedJobs, runningDto, currentEnergy, player.Level));
        })
        .WithName("GetTimedJobs")
        .WithTags("TimedJobs");

        // POST /timed-jobs/{id}/start — spend energy and begin the countdown.
        app.MapPost("/timed-jobs/{id}/start", [Authorize] async (
            string id,
            HttpContext ctx,
            TimedJobActionService timedJobActions,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var now = DateTimeOffset.UtcNow;

            var result = await timedJobActions.StartAsync(playerId, id, now);

            switch (result.Error)
            {
                case TimedJobActionError.UnknownTimedJob:
                    return Results.Problem(
                        detail: $"No timed job with id '{id}' exists in the active content pack.",
                        statusCode: StatusCodes.Status404NotFound);

                case TimedJobActionError.UnknownPlayer:
                    return Results.NotFound();

                case TimedJobActionError.ConcurrencyConflict:
                    return Results.Problem(
                        detail: "Another request modified your state first. Re-read and try again.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            if (!result.WasApplied)
            {
                var (reason, detail) = result.Failure switch
                {
                    TimedJobFailure.AlreadyRunning => ("alreadyRunning", "You already have an appointment running."),
                    TimedJobFailure.LevelTooLow => ("level", "Your level is too low for this appointment."),
                    TimedJobFailure.InsufficientEnergy => ("energy", "You do not have enough energy for this appointment."),
                    _ => ("unknown", "The appointment could not be started.")
                };

                return Results.Problem(
                    detail: detail,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = reason });
            }

            // The job and the exact row just persisted are both fully known here without a
            // re-read: StartAsync always sets StartedAtUtc to the `now` it was given.
            var job = content.TimedJobCatalog.Find(id)!;
            var runningRow = new PlayerTimedJob { PlayerId = playerId, TimedJobId = id, StartedAtUtc = now };

            var running = new TimedJobRunningDto(
                TimedJobId: id,
                Name: job.Name,
                StartedAtUtc: now,
                RemainingSeconds: job.DurationSeconds,
                Ready: false);

            return Results.Ok(new StartTimedJobResponse(
                running,
                PlayerProjection.ToMeDto(result.Player!, now, runningRow, job)));
        })
        .WithName("StartTimedJob")
        .WithTags("TimedJobs");

        // POST /timed-jobs/collect — no route id: there is exactly one slot, nothing to disambiguate.
        app.MapPost("/timed-jobs/collect", [Authorize] async (
            HttpContext ctx,
            TimedJobActionService timedJobActions,
            IContentPackProvider content) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var now = DateTimeOffset.UtcNow;

            var result = await timedJobActions.CollectAsync(playerId, now);

            switch (result.Error)
            {
                case TimedJobActionError.UnknownPlayer:
                    return Results.NotFound();

                case TimedJobActionError.ConcurrencyConflict:
                    return Results.Problem(
                        detail: "Another request modified your state first. Re-read and try again.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            if (!result.WasApplied)
            {
                // The only domain refusal here is NotReady — covers both "nothing is running" and
                // "running but not finished yet", since the manifest defines no separate token for
                // the first case.
                return Results.Problem(
                    detail: "Nothing is ready to collect.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["reason"] = "notReady" });
            }

            var outcome = result.Outcome!;
            var levelUp = LevelUpProjection.ToDto(result.LevelUp, content.JobCatalog);

            return Results.Ok(new CollectTimedJobResponse(
                Result: new TimedJobCollectResultDto(outcome.TimedJobId, outcome.CashEarned, outcome.XpEarned),
                Player: PlayerProjection.ToMeDto(result.Player!, now),
                LevelUp: levelUp));
        })
        .WithName("CollectTimedJob")
        .WithTags("TimedJobs");

        return app;
    }
}
