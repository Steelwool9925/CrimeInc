using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /jobs (T2.1 — the catalog as offered to one player).
/// Requires the Postgres container on port 5433 with the <c>underworld_test</c> database.
/// </summary>
public class JobEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public JobEndpointTests(CustomWebApplicationFactory factory)
    {
        factory.MigrateDb();
        _factory = factory;
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

        Assert.Equal(1, corner.GetProperty("attackRequirement").GetInt32());
        Assert.Equal(1, corner.GetProperty("defenseRequirement").GetInt32());
    }

    [Fact]
    public async Task Each_job_reports_the_callers_live_success_chance()
    {
        // A fresh 1/1 player sits exactly on the first job 1/1 requirement, so there is no surplus
        // and the figure is the job base chance. Computed by the same Job.SuccessChanceFor the
        // attempt rolls against, so what is displayed cannot drift from what is rolled.
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);

        // A fresh guest is 1/1 against the starter job's 1/1 requirement, so there is no surplus
        // bonus and the reported chance is the pack's base value exactly.
        Assert.Equal(0.80, Job(doc, "shake-down-corner-store").GetProperty("successChance").GetDouble(), precision: 10);
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

    // -------------------------------------------------------------------------
    // Item-quantity gate read-path parity (Tier 1 consumable item gate, M3)
    // -------------------------------------------------------------------------

    private const string ItemGatedJob = "hijack-a-delivery-truck";
    private const string GateItemId = "switchblade";

    private async Task<Guid> PlayerIdForAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var player = await db.Players.AsNoTracking().FirstAsync(p => p.DeviceToken == token);
        return player.Id;
    }

    /// <summary>Sets level and stats directly — these tests are about the item gate, not the grind.</summary>
    private async Task SetLevelAndStatsAsync(Guid playerId, int level, int attack, int defense)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var player = await db.Players.FirstAsync(p => p.Id == playerId);
        player.Level = level;
        player.Attack = attack;
        player.Defense = defense;
        await db.SaveChangesAsync();
    }

    private async Task GiveItemAsync(Guid playerId, string itemId, int quantity)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var holding = await db.PlayerItems.FindAsync(playerId, itemId);
        if (holding is null)
        {
            holding = new PlayerItem { PlayerId = playerId, ItemId = itemId };
            db.PlayerItems.Add(holding);
        }
        holding.AddCopies(quantity);
        await db.SaveChangesAsync();
    }

    /// <summary>A fresh guest, already at the item-gated job's level and stat requirements.</summary>
    private async Task<(string Token, Guid PlayerId)> ReadyForItemGatedJobAsync(int switchbladeQuantity = 0)
    {
        var token = await NewGuestTokenAsync();
        var playerId = await PlayerIdForAsync(token);
        await SetLevelAndStatsAsync(playerId, level: 3, attack: 9, defense: 7);
        if (switchbladeQuantity > 0)
            await GiveItemAsync(playerId, GateItemId, switchbladeQuantity);
        return (token, playerId);
    }

    [Fact]
    public async Task A_job_with_no_item_requirements_reports_an_empty_array()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await GetJobsAsync(token);
        var corner = Job(doc, "shake-down-corner-store");

        Assert.Empty(corner.GetProperty("itemRequirements").EnumerateArray());
    }

    [Fact]
    public async Task An_item_gated_job_reports_its_requirement_content_and_the_callers_holding()
    {
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 1);

        using var doc = await GetJobsAsync(token);
        var requirement = Job(doc, ItemGatedJob).GetProperty("itemRequirements")[0];

        Assert.Equal(GateItemId, requirement.GetProperty("itemId").GetString());
        Assert.Equal(2, requirement.GetProperty("minQuantity").GetInt32());
        Assert.Equal(1, requirement.GetProperty("held").GetInt32());
        Assert.False(requirement.GetProperty("met").GetBoolean());
    }

    [Fact]
    public async Task Holding_the_minimum_quantity_marks_the_requirement_met()
    {
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 2);

        using var doc = await GetJobsAsync(token);
        var requirement = Job(doc, ItemGatedJob).GetProperty("itemRequirements")[0];

        Assert.True(requirement.GetProperty("met").GetBoolean());
    }

    [Fact]
    public async Task Never_holding_the_item_reports_zero_held_not_a_missing_field()
    {
        // No PlayerItems row at all, as opposed to a row with a low quantity.
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        using var doc = await GetJobsAsync(token);
        var requirement = Job(doc, ItemGatedJob).GetProperty("itemRequirements")[0];

        Assert.Equal(0, requirement.GetProperty("held").GetInt32());
        Assert.False(requirement.GetProperty("met").GetBoolean());
    }

    [Fact]
    public async Task An_item_gated_job_below_the_minimum_is_locked_with_reason_items()
    {
        // Level and stats already cleared, so the item gate is the only reason left.
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 1);

        using var doc = await GetJobsAsync(token);
        var hijack = Job(doc, ItemGatedJob);

        Assert.False(hijack.GetProperty("canAttempt").GetBoolean());
        Assert.Equal("items", hijack.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task Holding_enough_of_the_required_item_unlocks_the_job()
    {
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 2);

        using var doc = await GetJobsAsync(token);
        var hijack = Job(doc, ItemGatedJob);

        Assert.True(hijack.GetProperty("canAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, hijack.GetProperty("unavailableReason").ValueKind);
    }

    [Fact]
    public async Task The_stats_reason_still_outranks_the_items_reason()
    {
        // Precedence is level -> stats -> items -> energy: a player short on both stats and items
        // must be told the stats reason, the one that takes longer to fix.
        var token = await NewGuestTokenAsync();
        var playerId = await PlayerIdForAsync(token);
        await SetLevelAndStatsAsync(playerId, level: 3, attack: 0, defense: 0); // fails stats, no items

        using var doc = await GetJobsAsync(token);
        var hijack = Job(doc, ItemGatedJob);

        Assert.Equal("stats", hijack.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task The_items_reason_outranks_the_energy_reason()
    {
        // A player with plenty of energy but short on items must see "items", not "energy" — items
        // sits ahead of energy in the precedence order (both are "go buy something" fixes).
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        using var doc = await GetJobsAsync(token);
        var hijack = Job(doc, ItemGatedJob);

        Assert.Equal(PlayerDefaults.EnergyMax, doc.RootElement.GetProperty("currentEnergy").GetInt32());
        Assert.Equal("items", hijack.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task The_reported_chance_includes_the_item_surplus_term()
    {
        // Exactly at the stat requirement (no stat surplus), holding 3 above the item minimum of 2:
        // 0.65 base + 3 * 0.01 item-surplus rate = 0.68. Computed by the same Job.SuccessChanceFor
        // the attempt would roll against, so this figure cannot drift from what an attempt gets.
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        using var doc = await GetJobsAsync(token);

        Assert.Equal(0.68, Job(doc, ItemGatedJob).GetProperty("successChance").GetDouble(), precision: 10);
    }

    [Fact]
    public async Task The_reported_chance_ignores_item_surplus_when_the_item_gate_is_not_met()
    {
        // Below the minimum: SuccessChanceFor still reports a figure (GET /jobs shows odds even for a
        // job the caller cannot attempt), but the item-surplus term is necessarily zero or negative-
        // floored since there is no surplus to speak of below the floor itself.
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        using var doc = await GetJobsAsync(token);

        Assert.Equal(0.65, Job(doc, ItemGatedJob).GetProperty("successChance").GetDouble(), precision: 10);
    }
}
