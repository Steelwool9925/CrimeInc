using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>Shared setup for the objective endpoint tests.</summary>
internal static class ObjectiveTestSupport
{
    /// <summary>
    /// Seeds a player at <paramref name="level"/> whose (randomly generated) id is offered
    /// <paramref name="objectiveId"/> today. Which objectives a player is offered is a hash of their id,
    /// so a test that needs a particular one picks an id that gets it, rather than hoping.
    /// </summary>
    internal static async Task<(Guid Id, string Token)> SeedOfferedAsync(
        CustomWebApplicationFactory factory, string objectiveId, int level = 1, long cash = 0, long xp = 0)
    {
        // The offer depends on the UTC day, so a test must not straddle midnight between choosing the id
        // and sending its first request. Close to midnight, wait it out.
        var untilMidnight = UtcDay.NextStart(DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow;
        if (untilMidnight < TimeSpan.FromSeconds(10))
            await Task.Delay(untilMidnight + TimeSpan.FromSeconds(1));

        var pool = factory.Services.GetRequiredService<IContentPackProvider>().ObjectiveCatalog.Objectives;
        var rules = new DailyObjectiveService();
        var today = UtcDay.Of(DateTimeOffset.UtcNow);

        Guid id;
        do { id = Guid.NewGuid(); }
        while (!rules.SelectFor(pool, id, today, level).Any(o => o.Id == objectiveId));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var now = DateTimeOffset.UtcNow;
        var token = DeviceTokenGenerator.Generate();
        db.Players.Add(new Player
        {
            Id = id,
            CreatedAtUtc = now,
            DeviceToken = token,
            Level = level,
            Xp = xp,
            Cash = cash,
            Energy = Meter.Full(PlayerDefaults.EnergyMax, now),
            Stamina = Meter.Full(PlayerDefaults.StaminaMax, now),
            Health = Meter.Full(PlayerDefaults.HealthMax, now)
        });
        await db.SaveChangesAsync();
        return (id, token);
    }
}
