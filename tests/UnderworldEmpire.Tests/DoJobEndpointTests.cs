using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for POST /jobs/{id}/do and POST /skills/allocate (T2.2 / T2.3) — the whole CP2
/// loop over real HTTP against real Postgres. Requires the container on port 5433.
/// </summary>
public class DoJobEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>The 1-energy, level-1 job from the crime pack. 10 XP, 15..30 cash.</summary>
    private const string CheapJob = "shake-down-corner-store";

    /// <summary>A level-3 job, used to exercise the level gate.</summary>
    private const string GatedJob = "hijack-a-delivery-truck";

    public DoJobEndpointTests(CustomWebApplicationFactory factory)
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

    private async Task<HttpResponseMessage> DoJobAsync(string token, string jobId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/jobs/{jobId}/do");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> AllocateAsync(string token, string? target)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/skills/allocate")
        {
            Content = JsonContent.Create(new { target })
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<JsonDocument> GetMeAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add("Authorization", $"Bearer {token}");
        var response = await _client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    // -------------------------------------------------------------------------
    // Auth and lookup
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Do_job_without_authorization_returns_401()
    {
        var response = await _client.PostAsync($"/jobs/{CheapJob}/do", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Do_job_with_an_unknown_job_id_returns_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await DoJobAsync(token, "no-such-job");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Job_ids_are_case_sensitive()
    {
        // Ids are content keys. Accepting a different casing would quietly admit a second spelling.
        var token = await NewGuestTokenAsync();

        var response = await DoJobAsync(token, CheapJob.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // The happy path — CP2's first line
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Doing_a_job_spends_energy_and_pays_cash_and_xp()
    {
        var token = await NewGuestTokenAsync();

        var response = await DoJobAsync(token, CheapJob);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var result = doc.RootElement.GetProperty("result");
        var player = doc.RootElement.GetProperty("player");

        Assert.Equal(CheapJob, result.GetProperty("jobId").GetString());
        Assert.Equal(1, result.GetProperty("energySpent").GetInt32());
        Assert.Equal(10, result.GetProperty("xpEarned").GetInt64());
        Assert.InRange(result.GetProperty("cashEarned").GetInt64(), 15, 30);

        // The response carries the post-action projection, so no follow-up GET /me is needed.
        Assert.Equal(PlayerDefaults.EnergyMax - 1, player.GetProperty("energy").GetProperty("current").GetInt32());
        Assert.Equal(10, player.GetProperty("xp").GetInt64());
        Assert.True(player.GetProperty("cash").GetInt64() > PlayerDefaults.StartingCash);
    }

    [Fact]
    public async Task The_returned_projection_matches_a_follow_up_GET_me()
    {
        // The whole point of returning the projection is that the client can trust it. If it drifted
        // from /me the optimisation would be a correctness bug.
        var token = await NewGuestTokenAsync();

        using var doJob = await BodyAsync(await DoJobAsync(token, CheapJob));
        var fromAction = doJob.RootElement.GetProperty("player");

        using var me = await GetMeAsync(token);
        var fromMe = me.RootElement;

        Assert.Equal(fromMe.GetProperty("cash").GetInt64(), fromAction.GetProperty("cash").GetInt64());
        Assert.Equal(fromMe.GetProperty("xp").GetInt64(), fromAction.GetProperty("xp").GetInt64());
        Assert.Equal(fromMe.GetProperty("level").GetInt32(), fromAction.GetProperty("level").GetInt32());
        Assert.Equal(
            fromMe.GetProperty("energy").GetProperty("current").GetInt32(),
            fromAction.GetProperty("energy").GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task Mastery_climbs_across_repeated_attempts()
    {
        var token = await NewGuestTokenAsync();

        for (int i = 1; i <= 3; i++)
        {
            using var doc = await BodyAsync(await DoJobAsync(token, CheapJob));
            var mastery = doc.RootElement.GetProperty("result").GetProperty("mastery");

            Assert.Equal(i, mastery.GetProperty("completions").GetInt32());
            Assert.Equal(0, mastery.GetProperty("rank").GetInt32()); // rank needs 10
        }
    }

    [Fact]
    public async Task Cash_and_xp_accumulate_across_attempts()
    {
        var token = await NewGuestTokenAsync();

        await DoJobAsync(token, CheapJob);
        using var second = await BodyAsync(await DoJobAsync(token, CheapJob));

        Assert.Equal(20, second.RootElement.GetProperty("player").GetProperty("xp").GetInt64());
        Assert.Equal(
            PlayerDefaults.EnergyMax - 2,
            second.RootElement.GetProperty("player").GetProperty("energy").GetProperty("current").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Refusals
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_job_above_the_players_level_is_refused_with_reason_level()
    {
        var token = await NewGuestTokenAsync();

        var response = await DoJobAsync(token, GatedJob);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal("level", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_refused_job_costs_nothing()
    {
        var token = await NewGuestTokenAsync();

        await DoJobAsync(token, GatedJob);

        using var me = await GetMeAsync(token);
        Assert.Equal(PlayerDefaults.EnergyMax, me.RootElement.GetProperty("energy").GetProperty("current").GetInt32());
        Assert.Equal(PlayerDefaults.StartingCash, me.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(0, me.RootElement.GetProperty("xp").GetInt64());
    }

    [Fact]
    public async Task Running_out_of_energy_is_refused_with_reason_energy()
    {
        // Ten 1-energy runs empty a full starting bar — but they also award exactly 100 XP, which
        // levels the player to 2 and refills the meters. So the bar has to be drained twice before
        // energy actually runs out. That interaction is the tuning that makes CP2 work, and this test
        // documents it.
        var token = await NewGuestTokenAsync();

        HttpResponseMessage? refusal = null;
        for (int i = 0; i < 40; i++)
        {
            var response = await DoJobAsync(token, CheapJob);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                refusal = response;
                break;
            }
        }

        Assert.NotNull(refusal);
        using var doc = await BodyAsync(refusal!);
        Assert.Equal("energy", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // Levelling — CP2's second line
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ten_runs_of_the_cheap_job_level_the_player_and_refill_meters()
    {
        // The load-bearing CP2 tuning: one full starting energy bar of the cheapest job is exactly
        // the level-2 threshold, and crosses a mastery rank along the way.
        //
        // The rank has moved twice. M3R dropped `completionsPerRank` 10 -> 8 so the first rank
        // stayed reachable once attempts could fail; lowering the first job base chance to 0.65
        // dropped it again, 8 -> 6. So the rank-up lands on the sixth run. Both instants are
        // checked: the sixth for the rank, the tenth for the level.
        var token = await NewGuestTokenAsync();

        JsonDocument? sixth = null;
        JsonDocument? tenth = null;
        for (int i = 1; i <= 10; i++)
        {
            var body = await BodyAsync(await DoJobAsync(token, CheapJob));
            if (i == 6) sixth = body;
            else if (i == 10) tenth = body;
            else body.Dispose();
        }

        using var rankDoc = sixth!;
        using var doc = tenth!;
        var root = doc.RootElement;

        // The sixth completion is the first mastery rank.
        var rankMastery = rankDoc.RootElement.GetProperty("result").GetProperty("mastery");
        Assert.Equal(6, rankMastery.GetProperty("completions").GetInt32());
        Assert.Equal(1, rankMastery.GetProperty("rank").GetInt32());
        Assert.True(rankDoc.RootElement.GetProperty("result")
            .GetProperty("masteryRankedUp").GetBoolean());

        var levelUp = root.GetProperty("levelUp");
        Assert.Equal(JsonValueKind.Object, levelUp.ValueKind);
        Assert.Equal(1, levelUp.GetProperty("levelsGained").GetInt32());
        Assert.Equal(2, levelUp.GetProperty("newLevel").GetInt32());
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, levelUp.GetProperty("skillPointsGranted").GetInt32());
        Assert.True(levelUp.GetProperty("metersRefilled").GetBoolean());

        // Meters refilled, so energy is back to full despite ten spends.
        var player = root.GetProperty("player");
        Assert.Equal(2, player.GetProperty("level").GetInt32());
        Assert.Equal(PlayerDefaults.EnergyMax, player.GetProperty("energy").GetProperty("current").GetInt32());
        Assert.Equal(LevelCurve.XpToReach(2), player.GetProperty("xp").GetInt64());
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, player.GetProperty("skillPoints").GetInt32());

        // And the tenth run is four completions past that rank, not a second rank-up.
        var mastery = root.GetProperty("result").GetProperty("mastery");
        Assert.Equal(10, mastery.GetProperty("completions").GetInt32());
        Assert.Equal(1, mastery.GetProperty("rank").GetInt32());
        Assert.False(root.GetProperty("result").GetProperty("masteryRankedUp").GetBoolean());
    }

    [Fact]
    public async Task LevelUp_is_null_when_no_threshold_is_crossed()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await DoJobAsync(token, CheapJob));

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("levelUp").ValueKind);
    }

    [Fact]
    public async Task Levelling_up_unlocks_a_previously_gated_job()
    {
        // Level 2 does not reach the level-3 job, so it must still be refused — this checks the gate
        // moves with the player rather than falling open at the first level-up.
        var token = await NewGuestTokenAsync();

        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        var response = await DoJobAsync(token, GatedJob);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal("level", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // Skill allocation — CP2's third line
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Allocate_without_authorization_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/skills/allocate", new { target = "attack" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Allocating_with_no_skill_points_returns_409()
    {
        var token = await NewGuestTokenAsync();

        var response = await AllocateAsync(token, "attack");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal("noSkillPoints", doc.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("stamina")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    // The numeric forms matter: Enum.TryParse would accept "0"/"1" as Attack/Defense, and
    // Enum.IsDefined cannot reject them because both are defined values. The contract is the
    // names, so a numeric target must be a 400.
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    public async Task An_unknown_skill_target_returns_400(string? target)
    {
        var token = await NewGuestTokenAsync();

        var response = await AllocateAsync(token, target);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Allocating_a_point_to_attack_raises_attack_and_consumes_the_point()
    {
        // The full CP2 loop: grind to a level-up, then spend the point and see the stat move.
        var token = await NewGuestTokenAsync();

        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        var response = await AllocateAsync(token, "attack");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var player = doc.RootElement.GetProperty("player");

        Assert.Equal(PlayerDefaults.BaseAttack + PlayerDefaults.StatIncreasePerSkillPoint,
            player.GetProperty("attack").GetInt32());
        Assert.Equal(PlayerDefaults.BaseDefense, player.GetProperty("defense").GetInt32());
        Assert.Equal(0, player.GetProperty("skillPoints").GetInt32());
    }

    [Fact]
    public async Task A_numeric_target_does_not_spend_a_point()
    {
        // Belt and braces on the parsing fix: a rejected target must not consume the point either.
        var token = await NewGuestTokenAsync();

        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        var response = await AllocateAsync(token, "0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var me = await GetMeAsync(token);
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, me.RootElement.GetProperty("skillPoints").GetInt32());
        Assert.Equal(PlayerDefaults.BaseAttack, me.RootElement.GetProperty("attack").GetInt32());
    }

    [Fact]
    public async Task Skill_target_is_case_insensitive()
    {
        // Unlike job ids, the target arrives as display-ish text from a client, so casing is forgiven.
        var token = await NewGuestTokenAsync();

        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        var response = await AllocateAsync(token, "DEFENSE");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal(PlayerDefaults.BaseDefense + PlayerDefaults.StatIncreasePerSkillPoint,
            doc.RootElement.GetProperty("player").GetProperty("defense").GetInt32());
    }

    [Fact]
    public async Task An_allocated_stat_is_visible_on_GET_me()
    {
        // CP2 says "see the stat increase on Home", and Home renders from /me.
        var token = await NewGuestTokenAsync();

        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        await AllocateAsync(token, "attack");

        using var me = await GetMeAsync(token);
        Assert.Equal(PlayerDefaults.BaseAttack + PlayerDefaults.StatIncreasePerSkillPoint,
            me.RootElement.GetProperty("attack").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Risk: the stat gate and a failed attempt (T3R.5)
    // -------------------------------------------------------------------------

    /// <summary>A losing success roll, then the cash roll, then the health-loss roll.</summary>
    private IDisposable ScriptedFailure() => _factory.Random.Scripted(10_000, 30, 1);

    [Fact]
    public async Task A_job_above_the_players_stats_is_refused_with_the_stats_reason()
    {
        // Run Numbers needs 6 attack against a fresh player's 5. Level 2 first, so the level gate is
        // not the reason reported.
        var token = await NewGuestTokenAsync();
        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        var response = await DoJobAsync(token, "run-numbers-for-the-bookie");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await BodyAsync(response);
        Assert.Equal("stats", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_stat_gate_spends_nothing()
    {
        var token = await NewGuestTokenAsync();
        for (int i = 0; i < 10; i++)
            await DoJobAsync(token, CheapJob);

        using var before = await GetMeAsync(token);
        long cashBefore = before.RootElement.GetProperty("cash").GetInt64();
        int energyBefore = before.RootElement.GetProperty("energy").GetProperty("current").GetInt32();

        await DoJobAsync(token, "run-numbers-for-the-bookie");

        using var after = await GetMeAsync(token);
        Assert.Equal(cashBefore, after.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(energyBefore, after.RootElement.GetProperty("energy").GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task A_failed_attempt_is_a_200_not_a_409()
    {
        // The contract this task exists for. A failure is an outcome, not a refusal: energy was
        // spent and state changed, so it owes a result body rather than a problem response.
        var token = await NewGuestTokenAsync();

        using (ScriptedFailure())
        {
            var response = await DoJobAsync(token, CheapJob);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await BodyAsync(response);
            Assert.False(body.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        }
    }

    [Fact]
    public async Task A_failed_attempt_reports_its_consolation_and_health_loss()
    {
        var token = await NewGuestTokenAsync();

        using (ScriptedFailure())
        {
            using var body = await BodyAsync(await DoJobAsync(token, CheapJob));
            var result = body.RootElement.GetProperty("result");

            // 1% of the rolled 30 and 10% of the job's 10 XP, both rounded up.
            Assert.Equal(1, result.GetProperty("cashEarned").GetInt64());
            Assert.Equal(1, result.GetProperty("xpEarned").GetInt64());
            Assert.Equal(1, result.GetProperty("healthLost").GetInt32());
            Assert.Equal(1, result.GetProperty("energySpent").GetInt32());
        }
    }

    [Fact]
    public async Task A_failed_attempt_does_not_advance_the_mastery_bar()
    {
        var token = await NewGuestTokenAsync();

        using (ScriptedFailure())
        {
            using var body = await BodyAsync(await DoJobAsync(token, CheapJob));
            var result = body.RootElement.GetProperty("result");

            Assert.Equal(0, result.GetProperty("mastery").GetProperty("completions").GetInt32());
            Assert.False(result.GetProperty("masteryRankedUp").GetBoolean());
        }
    }

    [Fact]
    public async Task A_failed_attempt_still_spends_the_energy()
    {
        var token = await NewGuestTokenAsync();

        using (ScriptedFailure())
        {
            using var body = await BodyAsync(await DoJobAsync(token, CheapJob));

            Assert.Equal(PlayerDefaults.EnergyMax - 1,
                body.RootElement.GetProperty("player")
                    .GetProperty("energy").GetProperty("current").GetInt32());
        }
    }

    [Fact]
    public async Task A_successful_attempt_reports_succeeded_and_no_health_loss()
    {
        var token = await NewGuestTokenAsync();

        using var body = await BodyAsync(await DoJobAsync(token, CheapJob));
        var result = body.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, result.GetProperty("healthLost").GetInt32());
    }
}
