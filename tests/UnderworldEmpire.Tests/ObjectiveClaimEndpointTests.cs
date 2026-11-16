using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Crediting, claiming and the properties of both, over real HTTP against real Postgres. Requires the
/// container on port 5433.
/// </summary>
/// <remarks>
/// Which objectives a player is offered is a hash of their id, so tests that need a particular one
/// seed a player whose (randomly generated) id is offered it, rather than hoping.
/// </remarks>
public class ObjectiveClaimEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CornerStore = "corner-store-regular"; // shake-down-corner-store x5, level 1, $110
    private const string Cheap = "shake-down-corner-store";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ObjectiveClaimEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<(Guid Id, string Token)> SeedOfferedAsync(
        string objectiveId, int level = 1, long cash = 0, long xp = 0) =>
        ObjectiveTestSupport.SeedOfferedAsync(_factory, objectiveId, level, cash, xp);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private Task<HttpResponseMessage> DoJobAsync(string token, string jobId) =>
        SendAsync(HttpMethod.Post, $"/jobs/{jobId}/do", token);

    private Task<HttpResponseMessage> ClaimAsync(string token, string objectiveId) =>
        SendAsync(HttpMethod.Post, $"/objectives/{objectiveId}/claim", token);

    private async Task<long> CashAsync(string token)
    {
        using var me = await BodyAsync(await SendAsync(HttpMethod.Get, "/me", token));
        return me.RootElement.GetProperty("cash").GetInt64();
    }

    private static JsonElement Objective(JsonElement objectives, string id) =>
        objectives.EnumerateArray().Single(o => o.GetProperty("id").GetString() == id);

    private async Task<int> RowCountAsync(Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return await db.PlayerDailyObjectives.CountAsync(d => d.PlayerId == playerId);
    }

    private static List<string> IdsOf(JsonElement objectives) =>
        objectives.EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToList();

    private async Task<List<string>> TodaysIdsAsync(string token)
    {
        using var view = await BodyAsync(await SendAsync(HttpMethod.Get, "/objectives", token));
        return IdsOf(view.RootElement.GetProperty("objectives"));
    }

    /// <summary>Performs the cheap job <paramref name="times"/> times and returns the last response body.</summary>
    private async Task<JsonDocument> RunCheapJobAsync(string token, int times)
    {
        JsonDocument? last = null;
        for (int i = 0; i < times; i++)
        {
            last?.Dispose();
            var response = await DoJobAsync(token, Cheap);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            last = await BodyAsync(response);
        }

        return last!;
    }

    // -------------------------------------------------------------------------
    // Crediting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Winning_jobs_moves_the_bar_and_the_do_job_response_carries_it()
    {
        var (_, token) = await SeedOfferedAsync(CornerStore);

        using var after = await RunCheapJobAsync(token, 2);

        var objective = Objective(after.RootElement.GetProperty("objectives"), CornerStore);
        Assert.Equal(2, objective.GetProperty("progress").GetInt64());
        Assert.False(objective.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task A_failed_attempt_advances_nothing_but_still_fixes_the_days_set()
    {
        var (id, token) = await SeedOfferedAsync(CornerStore);
        var before = await TodaysIdsAsync(token);

        using (_factory.Random.Scripted(10_000, 30, 1))
        {
            var response = await DoJobAsync(token, Cheap);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await BodyAsync(response);

            Assert.False(body.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
            var objectives = body.RootElement.GetProperty("objectives");
            Assert.Equal(before, IdsOf(objectives));
            Assert.All(objectives.EnumerateArray(), o => Assert.Equal(0, o.GetProperty("progress").GetInt64()));
        }

        // One zero-progress row per offered objective: that is what fixes the set.
        Assert.Equal(DailyObjectiveService.ObjectivesPerDay, await RowCountAsync(id));
    }

    [Fact]
    public async Task Reading_objectives_writes_nothing()
    {
        var (id, token) = await SeedOfferedAsync(CornerStore);

        for (int i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/objectives", token)).StatusCode);

        Assert.Equal(0, await RowCountAsync(id));
    }

    [Fact]
    public async Task Collecting_from_a_property_moves_a_collection_objective()
    {
        var (id, token) = await SeedOfferedAsync("collect-rent", level: 3, cash: 5_000);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/properties/street-corner/buy", token)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.PlayerProperties
                .Where(h => h.PlayerId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.AccruedAtUtc, DateTimeOffset.UtcNow.AddHours(-1)));
        }

        var response = await SendAsync(HttpMethod.Post, "/properties/street-corner/collect", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await BodyAsync(response);

        var objective = Objective(body.RootElement.GetProperty("objectives"), "collect-rent");
        Assert.Equal(1, objective.GetProperty("progress").GetInt64());
        Assert.True(objective.GetProperty("complete").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // Claiming
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_finished_objective_pays_its_reward_exactly_once()
    {
        var (_, token) = await SeedOfferedAsync(CornerStore);
        using var last = await RunCheapJobAsync(token, 5);
        Assert.True(Objective(last.RootElement.GetProperty("objectives"), CornerStore).GetProperty("complete").GetBoolean());
        long before = await CashAsync(token);

        var first = await ClaimAsync(token, CornerStore);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var body = await BodyAsync(first);
        long reward = body.RootElement.GetProperty("claimed").GetProperty("rewardCash").GetInt64();

        Assert.Equal(before + reward, body.RootElement.GetProperty("player").GetProperty("cash").GetInt64());
        Assert.True(Objective(body.RootElement.GetProperty("objectives"), CornerStore).GetProperty("claimed").GetBoolean());

        var second = await ClaimAsync(token, CornerStore);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var problem = await BodyAsync(second);
        Assert.Equal("alreadyClaimed", problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(before + reward, await CashAsync(token));
    }

    [Fact]
    public async Task Claiming_before_the_target_is_refused_and_pays_nothing()
    {
        var (_, token) = await SeedOfferedAsync(CornerStore);
        using var _ = await RunCheapJobAsync(token, 2);
        long before = await CashAsync(token);

        var response = await ClaimAsync(token, CornerStore);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await BodyAsync(response);
        Assert.Equal("notComplete", problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(before, await CashAsync(token));
    }

    [Fact]
    public async Task Claiming_an_objective_never_offered_is_refused()
    {
        var (_, token) = await SeedOfferedAsync(CornerStore);

        // Level 8 only; a level-1 player can never be offered it.
        var response = await ClaimAsync(token, "armored-score");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await BodyAsync(response);
        Assert.Equal("notToday", problem.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Claiming_an_unknown_objective_is_a_404()
    {
        var (_, token) = await SeedOfferedAsync(CornerStore);

        Assert.Equal(HttpStatusCode.NotFound, (await ClaimAsync(token, "no-such-objective")).StatusCode);
    }

    [Fact]
    public async Task Claiming_needs_a_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimAsync(token: null!, CornerStore)).StatusCode);
    }

    [Fact]
    public async Task A_level_up_later_in_the_day_does_not_change_the_set_and_progress_stays_claimable()
    {
        var (id, token) = await SeedOfferedAsync(CornerStore);
        using var _ = await RunCheapJobAsync(token, 5);
        var fixedSet = await TodaysIdsAsync(token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.Players.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Level, 12));
        }

        Assert.Equal(fixedSet, await TodaysIdsAsync(token));
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(token, CornerStore)).StatusCode);
        Assert.Equal(fixedSet, await TodaysIdsAsync(token));
    }

    [Fact]
    public async Task The_first_action_fixes_the_set_even_when_it_levels_the_player_up()
    {
        // 95 xp + a 10 xp win crosses the level 2 threshold at 100 on the very first action.
        var (_, token) = await SeedOfferedAsync(CornerStore, xp: 95);
        var before = await TodaysIdsAsync(token);

        var response = await DoJobAsync(token, Cheap);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await BodyAsync(response);

        Assert.Equal(2, body.RootElement.GetProperty("player").GetProperty("level").GetInt32());
        Assert.Equal(before, IdsOf(body.RootElement.GetProperty("objectives")));
        Assert.Equal(before, await TodaysIdsAsync(token));
    }

    [Fact]
    public async Task Yesterdays_progress_and_claim_do_not_carry_into_today()
    {
        var (id, token) = await SeedOfferedAsync(CornerStore);
        var objective = _factory.Services.GetRequiredService<IContentPackProvider>().ObjectiveCatalog.Find(CornerStore)!;
        var yesterday = UtcDay.Of(DateTimeOffset.UtcNow).AddDays(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var row = new PlayerDailyObjective { PlayerId = id, UtcDate = yesterday, ObjectiveId = CornerStore };
            for (int i = 0; i < objective.Target; i++)
                row.Apply(objective, ObjectiveEvent.JobWon(Cheap, 15));
            row.Claim(objective);
            db.PlayerDailyObjectives.Add(row);
            await db.SaveChangesAsync();
        }

        // Today starts clean: nothing claimed, nothing counted, and reading wrote nothing.
        using (var view = await BodyAsync(await SendAsync(HttpMethod.Get, "/objectives", token)))
        {
            var today = Objective(view.RootElement.GetProperty("objectives"), CornerStore);
            Assert.Equal(0, today.GetProperty("progress").GetInt64());
            Assert.False(today.GetProperty("claimed").GetBoolean());
        }
        Assert.Equal(1, await RowCountAsync(id));

        // A win today counts from one, on today's own row, and the claim is not already taken.
        using var after = await RunCheapJobAsync(token, 1);
        Assert.Equal(1, Objective(after.RootElement.GetProperty("objectives"), CornerStore).GetProperty("progress").GetInt64());
        Assert.Equal(1 + DailyObjectiveService.ObjectivesPerDay, await RowCountAsync(id));
        Assert.Equal(HttpStatusCode.Conflict, (await ClaimAsync(token, CornerStore)).StatusCode);
    }
}
