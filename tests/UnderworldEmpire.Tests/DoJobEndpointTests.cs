using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure;

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
    // Helpers — item-gated jobs (Tier 1 consumable item gate, M2)
    // -------------------------------------------------------------------------

    /// <summary>The item-gated job's own stat gate: 9 attack / 7 defense, level 3.</summary>
    private const string ItemGatedJob = "hijack-a-delivery-truck";

    private const string GateItemId = "switchblade";

    private async Task<Guid> PlayerIdForAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var player = await db.Players.AsNoTracking().FirstAsync(p => p.DeviceToken == token);
        return player.Id;
    }

    /// <summary>
    /// Sets a fresh guest player's level and stats directly, bypassing the grind — these tests are
    /// about the item gate, not about re-proving levelling (already covered elsewhere).
    /// </summary>
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

    private async Task<int> HeldQuantityAsync(Guid playerId, string itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var holding = await db.PlayerItems.AsNoTracking()
            .FirstOrDefaultAsync(h => h.PlayerId == playerId && h.ItemId == itemId);
        return holding?.Quantity ?? 0;
    }

    /// <summary>
    /// A fresh guest, already at the item-gated job's level and stat requirements, holding
    /// <paramref name="switchbladeQuantity"/> of the item it gates on.
    /// </summary>
    private async Task<(string Token, Guid PlayerId)> ReadyForItemGatedJobAsync(int switchbladeQuantity)
    {
        var token = await NewGuestTokenAsync();
        var playerId = await PlayerIdForAsync(token);
        await SetLevelAndStatsAsync(playerId, level: 3, attack: 9, defense: 7);
        if (switchbladeQuantity > 0)
            await GiveItemAsync(playerId, GateItemId, switchbladeQuantity);
        return (token, playerId);
    }

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
        // Draining the bar does not refuse on the first empty: a 1-energy job awards 10 XP, and
        // crossing a level threshold refills every meter. So the bar refills underneath the drain
        // several times before energy genuinely runs out. At a 20-energy bar the refills land on
        // attempts 10 (level 2, 100 XP) and 30 (level 3, 300 XP), which pushes the first real
        // refusal out to attempt 51.
        //
        // The bound scales with EnergyMax rather than being a bare literal. It was previously 40,
        // sufficient only while the bar was 10, and it silently became too small when the bar moved
        // to 20 — failing with an unhelpful "value is null" instead of naming the problem.
        //
        // Honest caveat: x10 is a safety factor, not a real derivation. The true bound depends on
        // the level-threshold cascade (how far apart consecutive level-ups get before a gap exceeds
        // one bar), which is a property of LevelCurve and the job's XP, not of EnergyMax alone. A
        // future curve change could in principle outgrow it — so the assertion below says so
        // explicitly rather than leaving the next reader to rediscover it.
        var token = await NewGuestTokenAsync();

        int maxAttempts = PlayerDefaults.EnergyMax * 10;
        HttpResponseMessage? refusal = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            var response = await DoJobAsync(token, CheapJob);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                refusal = response;
                break;
            }
        }

        Assert.True(refusal is not null,
            $"No energy refusal arrived within {maxAttempts} attempts at a " +
            $"{PlayerDefaults.EnergyMax}-energy bar. Either the drain is being outrun by level-up " +
            $"meter refills further than the x10 safety factor allows (see the note above), or " +
            $"running out of energy has stopped producing a 409.");

        using var doc = await BodyAsync(refusal!);
        Assert.Equal("energy", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // Levelling — CP2's second line
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ten_runs_of_the_cheap_job_level_the_player_and_refill_meters()
    {
        // Ten successful runs of the cheapest job are exactly the level-2 threshold (10 XP each),
        // and cross a mastery rank along the way. Both instants are checked: the sixth run for the
        // rank, the tenth for the level.
        //
        // Outcomes are forced, not rolled — CustomWebApplicationFactory swaps IRandomSource for a
        // deterministic SwitchableRandomSource so "ten jobs" really means "ten completions". The
        // starter job's base success chance is therefore irrelevant to this test, which is why
        // raising it 0.65 -> 0.80 did not move these numbers.
        //
        // `completionsPerRank` has moved twice (10 -> 8 -> 6) to keep the first rank reachable on
        // one bar as the odds changed; at 6 the rank-up lands on the sixth run.
        //
        // Note ten runs is no longer a *full* bar: PlayerDefaults.EnergyMax is 20, so this test
        // spends half of one. The level-2 reachability property that depends on the whole bar is
        // pinned separately by FirstSessionTests.
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

    // -------------------------------------------------------------------------
    // Item-quantity gate (Tier 1 consumable item gate, M2) — the real transaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Attempting_the_item_gated_job_below_the_minimum_is_refused()
    {
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        var response = await DoJobAsync(token, ItemGatedJob);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_item_gate_refusal_spends_nothing_and_consumes_no_stock()
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        using var before = await GetMeAsync(token);
        long cashBefore = before.RootElement.GetProperty("cash").GetInt64();
        int energyBefore = before.RootElement.GetProperty("energy").GetProperty("current").GetInt32();

        await DoJobAsync(token, ItemGatedJob);

        using var after = await GetMeAsync(token);
        Assert.Equal(cashBefore, after.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(energyBefore, after.RootElement.GetProperty("energy").GetProperty("current").GetInt32());
        Assert.Equal(0, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Holding_exactly_the_minimum_is_admitted_and_attempting_it_succeeds()
    {
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 1);

        var response = await DoJobAsync(token, ItemGatedJob);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_successful_attempt_consumes_exactly_one_unit_from_stock()
    {
        // Holding 5, well above the minimum of 1 — proves the spend is the fixed rate, not
        // proportional to the surplus that improved the odds.
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        var response = await DoJobAsync(token, ItemGatedJob);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(4, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task A_failed_item_gated_attempt_still_consumes_stock()
    {
        // True one-time-use: the item is spent on the attempt, not the outcome.
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        using (_factory.Random.Scripted(10_000, 200, 3))
        {
            using var body = await BodyAsync(await DoJobAsync(token, ItemGatedJob));
            Assert.False(body.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        }

        Assert.Equal(4, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Stock_dropping_below_the_minimum_relocks_the_job()
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 1);

        var first = await DoJobAsync(token, ItemGatedJob);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(0, await HeldQuantityAsync(playerId, GateItemId));

        // Refused: energy for a second attempt is plentiful, but the last blade is spent.
        var second = await DoJobAsync(token, ItemGatedJob);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(0, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Attempting_with_none_of_the_required_item_at_all_is_refused()
    {
        // No PlayerItems row for the item at all, as opposed to a row with too few — the gate must
        // treat "never bought one" the same as "bought some, ran out".
        var (token, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 0);

        var response = await DoJobAsync(token, ItemGatedJob);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Spending a worn item (the item must not outlive its last copy)
    // -------------------------------------------------------------------------

    private const string JacketId = "leather-jacket";

    private async Task WearAsync(Guid playerId, string itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var holding = await db.PlayerItems.FindAsync(playerId, itemId);
        holding!.Equip();
        await db.SaveChangesAsync();
    }

    private async Task<bool> IsWornAsync(Guid playerId, string itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var holding = await db.PlayerItems.AsNoTracking()
            .FirstOrDefaultAsync(h => h.PlayerId == playerId && h.ItemId == itemId);
        return holding?.Equipped ?? false;
    }

    private async Task<JsonElement> GetLoadoutAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/inventory");
        request.Headers.Add("Authorization", $"Bearer {token}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).RootElement.GetProperty("loadout").Clone();
    }

    private async Task<JsonElement> GetJobAsync(string token, string jobId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        request.Headers.Add("Authorization", $"Bearer {token}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await BodyAsync(response);
        return doc.RootElement.GetProperty("jobs").EnumerateArray()
            .First(j => j.GetProperty("id").GetString() == jobId).Clone();
    }

    /// <summary>
    /// The path a real player takes to meet the item-gated job's stat gate: wear the switchblade
    /// (+7 attack, +1 defense) and the leather jacket (+5 defense) on top of base 2 / 1, which is
    /// exactly 9 / 7, and hold <paramref name="spareBlades"/> more switchblades beyond the worn one.
    /// Earlier tests in this file seeded <i>base</i> 9 / 7 with the blade unworn, which is why they
    /// could not see how a worn blade and a spent blade interact.
    /// </summary>
    private async Task<(string Token, Guid PlayerId)> WearingTheGearForTheItemGatedJobAsync(int spareBlades)
    {
        var token = await NewGuestTokenAsync();
        var playerId = await PlayerIdForAsync(token);
        await SetLevelAndStatsAsync(playerId, level: 3, attack: 2, defense: 1);
        await GiveItemAsync(playerId, GateItemId, 1 + spareBlades);
        await GiveItemAsync(playerId, JacketId, 1);
        await WearAsync(playerId, GateItemId);
        await WearAsync(playerId, JacketId);
        return (token, playerId);
    }

    [Fact]
    public async Task The_worn_copy_alone_meets_the_stat_gate_but_is_not_spendable()
    {
        // Gear alone clears the stats (9 / 7), yet the job is refused for items: the one switchblade the
        // player holds is the one they are wearing, and a job never spends the worn copy.
        var (token, _) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 0);

        var loadout = await GetLoadoutAsync(token);
        var job = await GetJobAsync(token, ItemGatedJob);

        Assert.Equal(9, loadout.GetProperty("totalAttack").GetInt32());
        Assert.Equal(7, loadout.GetProperty("totalDefense").GetInt32());
        Assert.False(job.GetProperty("canAttempt").GetBoolean());
        Assert.Equal("items", job.GetProperty("unavailableReason").GetString());
        Assert.Equal(0, job.GetProperty("itemRequirements")[0].GetProperty("held").GetInt32());
    }

    [Fact]
    public async Task Attempting_with_only_the_worn_copy_is_refused_and_spends_nothing()
    {
        var (token, playerId) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 0);

        var response = await DoJobAsync(token, ItemGatedJob);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await HeldQuantityAsync(playerId, GateItemId));
        Assert.True(await IsWornAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task A_spare_copy_opens_the_job_and_is_reported_as_the_amount_held()
    {
        var (token, _) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 1);

        var job = await GetJobAsync(token, ItemGatedJob);
        var requirement = job.GetProperty("itemRequirements")[0];

        Assert.True(job.GetProperty("canAttempt").GetBoolean());
        Assert.Equal(1, requirement.GetProperty("held").GetInt32());
        Assert.Equal(3, requirement.GetProperty("maxExtras").GetInt32());
        Assert.True(requirement.GetProperty("met").GetBoolean());
    }

    [Fact]
    public async Task An_attempt_spends_the_spare_and_leaves_the_worn_copy_and_its_stats()
    {
        var (token, playerId) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 1);

        var response = await DoJobAsync(token, ItemGatedJob);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Two held, one spent: the worn one remains, still worn, still scoring.
        Assert.Equal(1, await HeldQuantityAsync(playerId, GateItemId));
        Assert.True(await IsWornAsync(playerId, GateItemId));
        var loadout = await GetLoadoutAsync(token);
        Assert.Equal(9, loadout.GetProperty("totalAttack").GetInt32());
        Assert.Equal(7, loadout.GetProperty("totalDefense").GetInt32());

        // And with no spare left, the job closes on items rather than on stats.
        var job = await GetJobAsync(token, ItemGatedJob);
        Assert.False(job.GetProperty("canAttempt").GetBoolean());
        Assert.Equal("items", job.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task The_reported_odds_do_not_count_the_stack_the_player_holds()
    {
        // Three held, one worn: two spare. Odds follow what the player chooses to spend, so with nothing
        // committed they are the base 0.65 however much sits in the inventory.
        var (token, _) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 2);

        var job = await GetJobAsync(token, ItemGatedJob);

        Assert.Equal(0.65, job.GetProperty("successChance").GetDouble(), precision: 10);
    }

    // -------------------------------------------------------------------------
    // Extra items the player chooses to spend for better odds
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> DoJobWithExtrasAsync(
        string token, string jobId, Dictionary<string, int> extras)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/jobs/{jobId}/do")
        {
            Content = JsonContent.Create(new { extras })
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static string? ReasonOf(JsonDocument problem) =>
        problem.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    [Fact]
    public async Task Committed_extras_are_spent_on_top_of_the_requirement_and_reported()
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        var response = await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [GateItemId] = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await BodyAsync(response);
        var consumed = body.RootElement.GetProperty("result").GetProperty("itemsConsumed")[0];
        Assert.Equal(3, consumed.GetProperty("quantityConsumed").GetInt32());
        Assert.Equal(2, consumed.GetProperty("quantityRemaining").GetInt32());
        Assert.Equal(2, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Extras_change_the_outcome_of_the_same_roll()
    {
        // 0.65 base, so a roll of 9000 (90%) misses. Three extras at 10% make it 0.95 and the very same
        // roll hits. The only thing that differs between the two attempts is the extras.
        var (withExtras, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);
        var (without, _) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        using (_factory.Random.Scripted(9_000, 500))
        {
            using var hit = await BodyAsync(
                await DoJobWithExtrasAsync(withExtras, ItemGatedJob, new() { [GateItemId] = 3 }));
            Assert.True(hit.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        }

        using (_factory.Random.Scripted(9_000, 500, 3))
        {
            using var miss = await BodyAsync(await DoJobAsync(without, ItemGatedJob));
            Assert.False(miss.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        }
    }

    [Fact]
    public async Task Extras_are_spent_when_the_attempt_fails_too()
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 5);

        using (_factory.Random.Scripted(10_000, 500, 3))
        {
            using var body = await BodyAsync(
                await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [GateItemId] = 2 }));
            Assert.False(body.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        }

        Assert.Equal(2, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Theory]
    [InlineData("switchblade", 4)]      // above the three the content allows
    [InlineData("switchblade", -1)]     // negative
    [InlineData("brass-knuckles", 1)]   // an item the job does not use
    public async Task Extras_the_job_does_not_accept_are_a_bad_request_and_spend_nothing(string itemId, int count)
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 10);

        var response = await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [itemId] = count });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await BodyAsync(response);
        Assert.Equal("extras", ReasonOf(problem));
        Assert.Equal(10, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Extras_the_player_cannot_cover_are_a_conflict_on_items_and_spend_nothing()
    {
        // Two held: enough for the requirement alone, not for the requirement plus two extras.
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 2);

        var response = await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [GateItemId] = 2 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await BodyAsync(response);
        Assert.Equal("items", ReasonOf(problem));
        Assert.Equal(2, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task An_explicit_empty_extras_object_is_the_same_as_no_body()
    {
        var (token, playerId) = await ReadyForItemGatedJobAsync(switchbladeQuantity: 3);

        var response = await DoJobWithExtrasAsync(token, ItemGatedJob, new());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, await HeldQuantityAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task Extras_never_spend_the_worn_copy()
    {
        // Wearing one of three: two spare, enough for the requirement plus one extra but not two. The
        // worn copy is not on offer however many extras are asked for.
        var (token, playerId) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 2);

        var tooMany = await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [GateItemId] = 2 });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var fits = await DoJobWithExtrasAsync(token, ItemGatedJob, new() { [GateItemId] = 1 });
        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        Assert.Equal(1, await HeldQuantityAsync(playerId, GateItemId));
        Assert.True(await IsWornAsync(playerId, GateItemId));
    }

    [Fact]
    public async Task A_worn_row_left_at_zero_copies_by_the_old_behaviour_scores_nothing()
    {
        // Players who attempted the job before the fix hold a row at zero copies still flagged as
        // worn. Written by SQL because the setters are private: this is the state in their database.
        var (token, playerId) = await WearingTheGearForTheItemGatedJobAsync(spareBlades: 0);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"PlayerItems\" SET \"Quantity\" = 0, \"Equipped\" = TRUE " +
                "WHERE \"PlayerId\" = {0} AND \"ItemId\" = {1}", playerId, GateItemId);
        }

        var loadout = await GetLoadoutAsync(token);
        var job = await GetJobAsync(token, ItemGatedJob);

        Assert.Equal(2, loadout.GetProperty("totalAttack").GetInt32());
        Assert.Equal(6, loadout.GetProperty("totalDefense").GetInt32());
        Assert.False(job.GetProperty("canAttempt").GetBoolean());
    }
}
