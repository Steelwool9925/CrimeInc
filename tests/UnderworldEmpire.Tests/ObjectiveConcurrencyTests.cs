using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Simultaneous requests against one player's objectives, over real HTTP against real Postgres. Requires
/// the container on port 5433.
/// </summary>
/// <remarks>
/// Objective progress is written in the same transaction as the job or collection that earned it, and a
/// claim is guarded twice (the player row and the objective row each carry an <c>xmin</c> token). These
/// tests are what prove that holds when requests actually race: the first action of a day inserts the
/// day's rows, so two racing first actions both try to.
/// </remarks>
public class ObjectiveConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CornerStore = "corner-store-regular";
    private const string Cheap = "shake-down-corner-store";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ObjectiveConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<long> CashAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/me", token);
        var me = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return me.GetProperty("cash").GetInt64();
    }

    private async Task<List<PlayerDailyObjective>> RowsAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return await db.PlayerDailyObjectives.AsNoTracking().Where(d => d.PlayerId == playerId).ToListAsync();
    }

    [Fact]
    public async Task Simultaneous_first_actions_fix_the_day_once_and_count_only_what_was_applied()
    {
        var (id, token) = await ObjectiveTestSupport.SeedOfferedAsync(_factory, CornerStore);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendAsync(HttpMethod.Post, $"/jobs/{Cheap}/do", token)));

        // Racing first actions all try to insert the same three rows. The losers are refused (409), never
        // a server error, and nothing is left half-written.
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        int applied = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.InRange(applied, 1, 8);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var rows = await RowsAsync(id);
        Assert.Equal(DailyObjectiveService.ObjectivesPerDay, rows.Count);

        // The test host pins every roll to a win, so each applied attempt is exactly one corner-store win.
        // A lost update would show as fewer; a double count as more.
        var corner = rows.Single(r => r.ObjectiveId == CornerStore);
        Assert.Equal(Math.Min(applied, 5), corner.Progress);
    }

    [Fact]
    public async Task A_burst_of_simultaneous_claims_pays_once()
    {
        var (id, token) = await ObjectiveTestSupport.SeedOfferedAsync(_factory, CornerStore);
        var objective = _factory.Services.GetRequiredService<IContentPackProvider>().ObjectiveCatalog.Find(CornerStore)!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var row = new PlayerDailyObjective { PlayerId = id, UtcDate = UtcDay.Of(DateTimeOffset.UtcNow), ObjectiveId = CornerStore };
            for (int i = 0; i < objective.Target; i++)
                row.Apply(objective, ObjectiveEvent.JobWon(Cheap, 15));
            db.PlayerDailyObjectives.Add(row);
            await db.SaveChangesAsync();
        }

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendAsync(HttpMethod.Post, $"/objectives/{CornerStore}/claim", token)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(objective.RewardCash, await CashAsync(token));
    }

    [Fact]
    public async Task A_claim_racing_a_job_never_pays_twice_or_loses_the_job()
    {
        var (id, token) = await ObjectiveTestSupport.SeedOfferedAsync(_factory, CornerStore);
        var objective = _factory.Services.GetRequiredService<IContentPackProvider>().ObjectiveCatalog.Find(CornerStore)!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var row = new PlayerDailyObjective { PlayerId = id, UtcDate = UtcDay.Of(DateTimeOffset.UtcNow), ObjectiveId = CornerStore };
            for (int i = 0; i < objective.Target; i++)
                row.Apply(objective, ObjectiveEvent.JobWon(Cheap, 15));
            db.PlayerDailyObjectives.Add(row);
            await db.SaveChangesAsync();
        }

        var claim = SendAsync(HttpMethod.Post, $"/objectives/{CornerStore}/claim", token);
        var job = SendAsync(HttpMethod.Post, $"/jobs/{Cheap}/do", token);
        var responses = await Task.WhenAll(claim, job);

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

        // Whatever order they landed in, the reward is paid at most once, and the claim flag is never lost.
        var after = (await RowsAsync(id)).Single(r => r.ObjectiveId == CornerStore);
        if (responses[0].StatusCode == HttpStatusCode.OK)
            Assert.True(after.Claimed);
        else
            Assert.False(after.Claimed);
    }
}
