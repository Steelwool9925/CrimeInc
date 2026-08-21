using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /jobs (T2.1 — the catalog as offered to one player).
/// Requires the Postgres container on port 5433 with the <c>underworld_test</c> database.
/// </summary>
public class JobEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public JobEndpointTests(CustomWebApplicationFactory factory)
    {
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Creates a fresh guest and returns its device token.</summary>
    private async Task<string> NewGuestTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/session", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<JsonDocument> GetJobsAsync(string deviceToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        request.Headers.Add("Authorization", $"Bearer {deviceToken}");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // -------------------------------------------------------------------------
    // Authentication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_jobs_without_authorization_header_returns_401()
    {
        // The catalog is player-specific (mastery + affordability), so unlike /content/pack it must
        // not be anonymous.
        var response = await _client.GetAsync("/jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_jobs_with_an_unknown_token_returns_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Catalog shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_jobs_returns_the_crime_catalog_with_player_context()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);
        var root = doc.RootElement;

        Assert.Equal(PlayerDefaults.StartingLevel, root.GetProperty("level").GetInt32());
        Assert.Equal(PlayerDefaults.EnergyMax, root.GetProperty("currentEnergy").GetInt32());

        var jobs = root.GetProperty("jobs").EnumerateArray().ToList();
        Assert.NotEmpty(jobs);

        // Every entry carries the full contract the client renders from.
        foreach (var job in jobs)
        {
            Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("name").GetString()));
            Assert.True(job.GetProperty("energyCost").GetInt32() >= 1);
            Assert.True(job.GetProperty("levelRequirement").GetInt32() >= 1);
            Assert.True(job.GetProperty("cashMax").GetInt64() >= job.GetProperty("cashMin").GetInt64());
            Assert.True(job.TryGetProperty("mastery", out _));
            Assert.True(job.TryGetProperty("canAttempt", out _));
        }
    }

    [Fact]
    public async Task Get_jobs_reports_zero_mastery_for_a_brand_new_player()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var mastery = job.GetProperty("mastery");
            Assert.Equal(0, mastery.GetProperty("completions").GetInt32());
            Assert.Equal(0, mastery.GetProperty("rank").GetInt32());
            Assert.True(mastery.GetProperty("maxRank").GetInt32() >= 0);

            // A never-attempted job still reports a countdown, so the client needs no null branch.
            Assert.Equal(JsonValueKind.Number, mastery.GetProperty("completionsUntilNextRank").ValueKind);
        }
    }

    // -------------------------------------------------------------------------
    // Affordability and gating
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_new_player_can_attempt_at_least_one_job()
    {
        // A fresh install showing an entirely locked list would be a broken first experience.
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        Assert.Contains(
            doc.RootElement.GetProperty("jobs").EnumerateArray(),
            j => j.GetProperty("canAttempt").GetBoolean());
    }

    [Fact]
    public async Task Jobs_above_the_players_level_are_locked_with_reason_level()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        var overLevel = doc.RootElement.GetProperty("jobs").EnumerateArray()
            .Where(j => j.GetProperty("levelRequirement").GetInt32() > PlayerDefaults.StartingLevel)
            .ToList();

        Assert.NotEmpty(overLevel); // the crime pack must exercise the level gate at all
        Assert.All(overLevel, j =>
        {
            Assert.False(j.GetProperty("canAttempt").GetBoolean());
            Assert.Equal("level", j.GetProperty("unavailableReason").GetString());
        });
    }

    [Fact]
    public async Task An_attemptable_job_reports_a_null_unavailable_reason()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            if (!job.GetProperty("canAttempt").GetBoolean())
                continue;

            Assert.Equal(JsonValueKind.Null, job.GetProperty("unavailableReason").ValueKind);
        }
    }

    [Fact]
    public async Task Level_gate_takes_precedence_over_energy_in_the_reason()
    {
        // A job that fails both checks must report "level": it is the permanent reason, and telling
        // the player to wait for energy on a job they cannot unlock yet would be misleading.
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        var lockedAndUnaffordable = doc.RootElement.GetProperty("jobs").EnumerateArray()
            .Where(j => j.GetProperty("levelRequirement").GetInt32() > PlayerDefaults.StartingLevel
                        && j.GetProperty("energyCost").GetInt32() > PlayerDefaults.EnergyMax)
            .ToList();

        Assert.All(lockedAndUnaffordable, j =>
            Assert.Equal("level", j.GetProperty("unavailableReason").GetString()));
    }

    // -------------------------------------------------------------------------
    // Isolation between players
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_players_each_see_their_own_mastery()
    {
        // Both are fresh, so both read zero — the point is that the endpoint scopes the mastery
        // query by player rather than returning whatever rows exist.
        var firstToken = await NewGuestTokenAsync();
        var secondToken = await NewGuestTokenAsync();

        using var first = await GetJobsAsync(firstToken);
        using var second = await GetJobsAsync(secondToken);

        int FirstJobCompletions(JsonDocument doc) =>
            doc.RootElement.GetProperty("jobs")[0].GetProperty("mastery").GetProperty("completions").GetInt32();

        Assert.Equal(0, FirstJobCompletions(first));
        Assert.Equal(0, FirstJobCompletions(second));
    }

    // -------------------------------------------------------------------------
    // Risk: requirements and live odds (T3R.5)
    // -------------------------------------------------------------------------

    /// <summary>Finds one job in the listing by its content id.</summary>
    private static JsonElement Job(JsonDocument doc, string id) =>
        doc.RootElement.GetProperty("jobs").EnumerateArray().Single(j => j.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Each_job_reports_its_stat_requirements()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);
        var corner = Job(doc, "shake-down-corner-store");

        Assert.Equal(3, corner.GetProperty("attackRequirement").GetInt32());
        Assert.Equal(2, corner.GetProperty("defenseRequirement").GetInt32());
    }

    [Fact]
    public async Task Each_job_reports_the_callers_live_success_chance()
    {
        // A fresh 5/5 player against a 3/2 requirement: five points of surplus at 1% over the 0.85
        // base. The figure is computed by the same Job.SuccessChanceFor the attempt rolls against,
        // so what is displayed cannot drift from what is rolled.
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        Assert.Equal(0.90, Job(doc, "shake-down-corner-store").GetProperty("successChance").GetDouble(), precision: 10);
    }

    [Fact]
    public async Task The_reported_chance_is_capped_at_the_packs_ceiling()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
            Assert.True(job.GetProperty("successChance").GetDouble() <= 0.95);
    }

    /// <summary>Performs the cheap job once.</summary>
    private async Task DoCheapJobAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/jobs/shake-down-corner-store/do");
        request.Headers.Add("Authorization", $"Bearer {token}");
        await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_job_above_the_players_stats_reports_the_stats_reason()
    {
        // Run Numbers needs 6 attack and a fresh player has 5 — the one job in the pack a starting
        // player is a single point short of, and the CP3R demo.
        //
        // The player has to reach level 2 first, because the level gate outranks the stats gate and
        // would otherwise be the reason reported. Ten runs of the cheap job does it: the test host
        // pins the RNG to always-minimum, so every attempt succeeds.
        var token = await NewGuestTokenAsync();
        for (int i = 0; i < 10; i++)
            await DoCheapJobAsync(token);

        using var doc = await GetJobsAsync(token);
        var numbers = Job(doc, "run-numbers-for-the-bookie");

        Assert.Equal(2, doc.RootElement.GetProperty("level").GetInt32());
        Assert.False(numbers.GetProperty("canAttempt").GetBoolean());
        Assert.Equal("stats", numbers.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task The_level_reason_still_outranks_the_stats_reason()
    {
        // Precedence is level -> stats -> energy, so a job failing both reports the level.
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);
        var transport = Job(doc, "rob-an-armored-transport");

        Assert.Equal("level", transport.GetProperty("unavailableReason").GetString());
    }
}
