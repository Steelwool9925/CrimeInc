using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// GET /daily and POST /daily/claim over real HTTP against real Postgres. Requires the container
/// on port 5433.
/// </summary>
public class DailyEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DailyEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Guid Id, string Token)> NewPlayerAsync()
    {
        using var session = await (await _client.PostAsJsonAsync("/session", new { })).Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(session);
        return (
            Guid.Parse(doc.RootElement.GetProperty("playerId").GetString()!),
            doc.RootElement.GetProperty("deviceToken").GetString()!);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private Task<HttpResponseMessage> GetDailyAsync(string token) => SendAsync(HttpMethod.Get, "/daily", token);

    private Task<HttpResponseMessage> ClaimAsync(string? token) => SendAsync(HttpMethod.Post, "/daily/claim", token);

    private DailyReward RewardForDay(int day)
    {
        var catalog = _factory.Services.GetRequiredService<IContentPackProvider>().DailyRewardCatalog;
        return new DailyRewardService().RewardFor(catalog.Rewards, day);
    }

    /// <summary>Backdates the player's streak row directly, bypassing the claim endpoint.</summary>
    private async Task BackdateAsync(Guid playerId, DateOnly lastClaimedUtcDate, int currentStreak, int longestStreak)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DailyStreaks" ("PlayerId", "LastClaimedUtcDate", "CurrentStreak", "LongestStreak")
            VALUES ({playerId}, {lastClaimedUtcDate}, {currentStreak}, {longestStreak})
            ON CONFLICT ("PlayerId") DO UPDATE SET
                "LastClaimedUtcDate" = {lastClaimedUtcDate},
                "CurrentStreak" = {currentStreak},
                "LongestStreak" = {longestStreak}
            """);
    }

    // -------------------------------------------------------------------------
    // GET /daily
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_daily_needs_a_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetDailyAsync(null!)).StatusCode);
    }

    [Fact]
    public async Task A_brand_new_player_is_claimable_at_day_one()
    {
        var (_, token) = await NewPlayerAsync();

        using var body = await BodyAsync(await GetDailyAsync(token));
        var root = body.RootElement;

        Assert.True(root.GetProperty("claimable").GetBoolean());
        Assert.Equal(0, root.GetProperty("currentStreak").GetInt32());
        Assert.Equal(0, root.GetProperty("longestStreak").GetInt32());
        Assert.Equal(1, root.GetProperty("nextReward").GetProperty("day").GetInt32());
    }

    [Fact]
    public async Task Reading_daily_writes_nothing()
    {
        var (id, token) = await NewPlayerAsync();

        for (int i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetDailyAsync(token)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        Assert.False(await db.DailyStreaks.AnyAsync(d => d.PlayerId == id));
    }

    // -------------------------------------------------------------------------
    // POST /daily/claim
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Claiming_needs_a_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimAsync(null)).StatusCode);
    }

    [Fact]
    public async Task The_first_claim_pays_day_one_and_sets_the_streak()
    {
        var (_, token) = await NewPlayerAsync();
        var reward = RewardForDay(1);

        var response = await ClaimAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await BodyAsync(response);
        var root = body.RootElement;

        Assert.Equal(1, root.GetProperty("reward").GetProperty("day").GetInt32());
        Assert.Equal(reward.Cash, root.GetProperty("reward").GetProperty("cash").GetInt64());
        Assert.Equal(1, root.GetProperty("currentStreak").GetInt32());
        Assert.Equal(1, root.GetProperty("longestStreak").GetInt32());
        Assert.Equal(reward.Cash, root.GetProperty("player").GetProperty("cash").GetInt64());
        Assert.Equal(1, root.GetProperty("player").GetProperty("dailyStreakCurrent").GetInt32());
        Assert.False(root.GetProperty("player").GetProperty("dailyClaimable").GetBoolean());
    }

    [Fact]
    public async Task A_second_claim_the_same_day_is_refused_and_pays_nothing()
    {
        var (_, token) = await NewPlayerAsync();
        using var first = await BodyAsync(await ClaimAsync(token));
        long cashAfterFirstClaim = first.RootElement.GetProperty("player").GetProperty("cash").GetInt64();

        var response = await ClaimAsync(token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await BodyAsync(response);
        Assert.Equal("alreadyClaimedToday", problem.RootElement.GetProperty("reason").GetString());

        using var me = await BodyAsync(await SendAsync(HttpMethod.Get, "/me", token));
        Assert.Equal(cashAfterFirstClaim, me.RootElement.GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Get_daily_reflects_the_claim_just_taken()
    {
        var (_, token) = await NewPlayerAsync();
        await ClaimAsync(token);

        using var body = await BodyAsync(await GetDailyAsync(token));
        var root = body.RootElement;

        Assert.False(root.GetProperty("claimable").GetBoolean());
        Assert.Equal(1, root.GetProperty("currentStreak").GetInt32());
    }

    [Fact]
    public async Task Claiming_the_day_after_a_claim_extends_the_streak()
    {
        var (id, token) = await NewPlayerAsync();
        var yesterday = UtcDay.Of(DateTimeOffset.UtcNow).AddDays(-1);
        await BackdateAsync(id, yesterday, currentStreak: 1, longestStreak: 1);
        var reward = RewardForDay(2);

        using var body = await BodyAsync(await ClaimAsync(token));
        var root = body.RootElement;

        Assert.Equal(2, root.GetProperty("currentStreak").GetInt32());
        Assert.Equal(2, root.GetProperty("longestStreak").GetInt32());
        Assert.Equal(reward.Cash, root.GetProperty("reward").GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Skipping_a_day_resets_the_streak_to_one()
    {
        var (id, token) = await NewPlayerAsync();
        var twoDaysAgo = UtcDay.Of(DateTimeOffset.UtcNow).AddDays(-2);
        await BackdateAsync(id, twoDaysAgo, currentStreak: 5, longestStreak: 5);

        using var body = await BodyAsync(await ClaimAsync(token));
        var root = body.RootElement;

        Assert.Equal(1, root.GetProperty("currentStreak").GetInt32());
        Assert.Equal(5, root.GetProperty("longestStreak").GetInt32()); // the record survives the reset
    }

    [Fact]
    public async Task A_streak_at_day_seven_or_beyond_pays_the_tables_last_entry()
    {
        var (id, token) = await NewPlayerAsync();
        var yesterday = UtcDay.Of(DateTimeOffset.UtcNow).AddDays(-1);
        await BackdateAsync(id, yesterday, currentStreak: 10, longestStreak: 10);
        var reward = RewardForDay(11); // clamps to the table's last entry

        using var body = await BodyAsync(await ClaimAsync(token));
        var root = body.RootElement;

        Assert.Equal(11, root.GetProperty("currentStreak").GetInt32());
        Assert.Equal(reward.Day, root.GetProperty("reward").GetProperty("day").GetInt32());
        Assert.Equal(reward.Cash, root.GetProperty("reward").GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Claiming_refills_energy_even_without_a_level_up()
    {
        var (id, token) = await NewPlayerAsync();
        // Spend energy first so a refill is actually observable.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var player = await db.Players.FirstAsync(p => p.Id == id);
            player.Energy = new UnderworldEmpire.Domain.Meter(player.Energy.Max, 0, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        using var body = await BodyAsync(await ClaimAsync(token));
        var energy = body.RootElement.GetProperty("player").GetProperty("energy");

        Assert.Equal(energy.GetProperty("max").GetInt32(), energy.GetProperty("current").GetInt32());
    }
}
