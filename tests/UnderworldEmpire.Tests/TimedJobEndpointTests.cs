using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /timed-jobs, POST /timed-jobs/{id}/start and
/// POST /timed-jobs/collect — the whole overnight-timed-jobs M1 loop over real HTTP against real
/// Postgres. Requires the container on port 5433.
/// </summary>
public class TimedJobEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>The 3-energy, level-1, 8-hour appointment from the crime pack.</summary>
    private const string CheapTimedJob = "run-a-late-night-shipment";

    /// <summary>The level-3, 24-hour appointment, used to exercise the level gate.</summary>
    private const string GatedTimedJob = "launder-a-nights-takings";

    public TimedJobEndpointTests(CustomWebApplicationFactory factory)
    {
        factory.MigrateDb();
        _factory = factory;
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> NewGuestTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/session", new { });
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<HttpResponseMessage> GetTimedJobsAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/timed-jobs");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> StartAsync(string token, string timedJobId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/timed-jobs/{timedJobId}/start");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CollectAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/timed-jobs/collect");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<Guid> PlayerIdForAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var player = await db.Players.AsNoTracking().FirstAsync(p => p.DeviceToken == token);
        return player.Id;
    }

    /// <summary>
    /// Rewrites the running row's <c>StartedAtUtc</c> directly in the database, so M1's 8h/24h
    /// content durations can be exercised without waiting for them — the same technique the plan's
    /// own validation gate calls for.
    /// </summary>
    private async Task BackdateRunningJobAsync(Guid playerId, TimeSpan backBy)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var row = await db.PlayerTimedJobs.FirstAsync(t => t.PlayerId == playerId);
        row.StartedAtUtc -= backBy;
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // GET /timed-jobs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GET_timed_jobs_lists_every_appointment_startable_for_a_fresh_player()
    {
        var token = await NewGuestTokenAsync();

        var response = await GetTimedJobsAsync(token);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var timedJobs = body.RootElement.GetProperty("timedJobs").EnumerateArray().ToList();
        Assert.Contains(timedJobs, j => j.GetProperty("id").GetString() == CheapTimedJob
            && j.GetProperty("canStart").GetBoolean());
        // The level-3 appointment is out of reach for a fresh level-1 guest.
        var gated = timedJobs.First(j => j.GetProperty("id").GetString() == GatedTimedJob);
        Assert.False(gated.GetProperty("canStart").GetBoolean());
        Assert.Equal("level", gated.GetProperty("unavailableReason").GetString());
        Assert.True(body.RootElement.GetProperty("running").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task GET_timed_jobs_reports_every_other_row_as_alreadyRunning_once_a_slot_is_occupied()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);

        var response = await GetTimedJobsAsync(token);
        var body = await BodyAsync(response);

        var timedJobs = body.RootElement.GetProperty("timedJobs").EnumerateArray().ToList();
        var other = timedJobs.First(j => j.GetProperty("id").GetString() != CheapTimedJob);
        Assert.False(other.GetProperty("canStart").GetBoolean());
        Assert.Equal("alreadyRunning", other.GetProperty("unavailableReason").GetString());

        var running = body.RootElement.GetProperty("running");
        Assert.Equal(CheapTimedJob, running.GetProperty("timedJobId").GetString());
        Assert.False(running.GetProperty("ready").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // POST /timed-jobs/{id}/start
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Starting_an_appointment_spends_exactly_its_energy_cost()
    {
        var token = await NewGuestTokenAsync();
        var meBefore = await BodyAsync(await GetMeAsync(token));
        int energyBefore = meBefore.RootElement.GetProperty("energy").GetProperty("current").GetInt32();

        var response = await StartAsync(token, CheapTimedJob);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(energyBefore - 3, body.RootElement.GetProperty("player")
            .GetProperty("energy").GetProperty("current").GetInt32());
        Assert.True(body.RootElement.GetProperty("player").GetProperty("timedJobRunning").GetBoolean());
        Assert.False(body.RootElement.GetProperty("player").GetProperty("timedJobReady").GetBoolean());
    }

    [Fact]
    public async Task Starting_a_second_appointment_while_one_is_running_is_refused()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);

        var response = await StartAsync(token, CheapTimedJob);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("alreadyRunning", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Starting_an_appointment_above_the_players_level_is_refused()
    {
        var token = await NewGuestTokenAsync();

        var response = await StartAsync(token, GatedTimedJob);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("level", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Starting_an_unknown_appointment_id_is_a_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await StartAsync(token, "no-such-appointment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_start_leaves_the_slot_free()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, GatedTimedJob); // refused: level

        var response = await GetTimedJobsAsync(token);
        var body = await BodyAsync(response);

        Assert.True(body.RootElement.GetProperty("running").ValueKind == JsonValueKind.Null);
    }

    // -------------------------------------------------------------------------
    // POST /timed-jobs/collect
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Collecting_with_nothing_running_is_refused_as_not_ready()
    {
        var token = await NewGuestTokenAsync();

        var response = await CollectAsync(token);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("notReady", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Collecting_before_the_duration_has_elapsed_is_refused()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);

        var response = await CollectAsync(token);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("notReady", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Collecting_an_8_hour_appointment_after_a_backdated_start_pays_out_and_frees_the_slot()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);
        var playerId = await PlayerIdForAsync(token);
        await BackdateRunningJobAsync(playerId, TimeSpan.FromHours(8));

        var response = await CollectAsync(token);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CheapTimedJob, body.RootElement.GetProperty("result").GetProperty("timedJobId").GetString());
        long cashEarned = body.RootElement.GetProperty("result").GetProperty("cashEarned").GetInt64();
        Assert.InRange(cashEarned, 180, 320);
        Assert.Equal(40, body.RootElement.GetProperty("result").GetProperty("xpEarned").GetInt64());
        Assert.False(body.RootElement.GetProperty("player").GetProperty("timedJobRunning").GetBoolean());

        // The slot is free again — a second start right afterward must succeed.
        var secondStart = await StartAsync(token, CheapTimedJob);
        Assert.Equal(HttpStatusCode.OK, secondStart.StatusCode);
    }

    [Fact]
    public async Task Collecting_a_24_hour_appointment_after_a_backdated_start_pays_out()
    {
        var token = await NewGuestTokenAsync();
        var playerId = await PlayerIdForAsync(token);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var player = await db.Players.FirstAsync(p => p.Id == playerId);
            player.Level = 3; // the 24h appointment requires level 3
            await db.SaveChangesAsync();
        }
        await StartAsync(token, GatedTimedJob);
        await BackdateRunningJobAsync(playerId, TimeSpan.FromHours(24));

        var response = await CollectAsync(token);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        long cashEarned = body.RootElement.GetProperty("result").GetProperty("cashEarned").GetInt64();
        Assert.InRange(cashEarned, 650, 1100);
    }

    [Fact]
    public async Task GET_me_reports_the_remaining_seconds_of_a_running_appointment()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);

        var me = await BodyAsync(await GetMeAsync(token));

        Assert.True(me.RootElement.GetProperty("timedJobRunning").GetBoolean());
        Assert.False(me.RootElement.GetProperty("timedJobReady").GetBoolean());
        int? remaining = me.RootElement.GetProperty("timedJobRemainingSeconds").GetInt32();
        Assert.InRange(remaining!.Value, 28790, 28800);
    }

    [Fact]
    public async Task GET_me_reports_ready_once_a_backdated_appointment_has_elapsed()
    {
        var token = await NewGuestTokenAsync();
        await StartAsync(token, CheapTimedJob);
        var playerId = await PlayerIdForAsync(token);
        await BackdateRunningJobAsync(playerId, TimeSpan.FromHours(8));

        var me = await BodyAsync(await GetMeAsync(token));

        Assert.True(me.RootElement.GetProperty("timedJobReady").GetBoolean());
        Assert.Equal(0, me.RootElement.GetProperty("timedJobRemainingSeconds").GetInt32());
    }
}
