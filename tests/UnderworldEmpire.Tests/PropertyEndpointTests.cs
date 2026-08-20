using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /properties and the buy/collect endpoints (T3.4) over real HTTP against
/// real Postgres. Requires the container on port 5433.
/// </summary>
/// <remarks>
/// Mirrors <see cref="InventoryEndpointTests"/>. The one thing these tests need that the item tests do
/// not is <i>elapsed time</i>: income accrues from the gap between a holding's stored instant and now,
/// so a test that wants earned cash moves the stored instant backwards rather than waiting. See
/// <see cref="BackdateAccrualAsync"/>.
/// </remarks>
public class PropertyEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>Level 1, 500 cash, 240/hour (4 a minute), a 15-minute window — a 60-cash cap.</summary>
    private const string Starter = "street-corner";

    /// <summary>Level 2 — above a new player's level, so it exercises the level gate.</summary>
    private const string GatedProperty = "pawn-shop";

    /// <summary>What one unit of <see cref="Starter"/> earns per minute, from content.</summary>
    private const int StarterPerMinute = 4;

    /// <summary>What one unit of <see cref="Starter"/> holds when full, from content.</summary>
    private const int StarterCapPerUnit = 60;

    public PropertyEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
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

    private async Task<HttpResponseMessage> GetPropertiesAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/properties");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> BuyAsync(string token, string propertyId) =>
        PostAsync(token, $"/properties/{propertyId}/buy");

    private Task<HttpResponseMessage> CollectAsync(string token, string propertyId) =>
        PostAsync(token, $"/properties/{propertyId}/collect");

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static JsonElement PropertyNamed(JsonElement body, string propertyId) =>
        body.GetProperty("properties").EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == propertyId);

    private static string? ReasonOf(JsonDocument problem) =>
        problem.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    /// <summary>
    /// Tops the player up directly in the database. The cheapest property costs five times the starting
    /// cash by design, so a test about owning one should not have to grind a random-payout job to get
    /// there.
    /// </summary>
    private async Task GrantAsync(string deviceToken, long cash = 0, int? level = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var player = await db.Players.SingleAsync(p => p.DeviceToken == deviceToken);
        player.AddCash(cash);
        if (level is not null)
            player.Level = level.Value;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves a holding's stored accrual instant backwards, so the server sees time as having passed.
    /// </summary>
    /// <remarks>
    /// Written straight to the column rather than through the entity: <c>AccruedAtUtc</c> has a private
    /// setter because only the accrual maths may move it, and a test-only public setter would be a hole
    /// in exactly the invariant T3.3 was built around. The value written is computed in C# and passed as
    /// a constant so the update needs no date arithmetic translated into SQL.
    /// </remarks>
    private async Task BackdateAccrualAsync(string deviceToken, string propertyId, TimeSpan by)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var playerId = await db.Players
            .Where(p => p.DeviceToken == deviceToken)
            .Select(p => p.Id)
            .SingleAsync();

        var current = await db.PlayerProperties
            .AsNoTracking()
            .Where(h => h.PlayerId == playerId && h.PropertyId == propertyId)
            .Select(h => h.AccruedAtUtc)
            .SingleAsync();

        var backdated = current - by;

        await db.PlayerProperties
            .Where(h => h.PlayerId == playerId && h.PropertyId == propertyId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.AccruedAtUtc, backdated));
    }

    /// <summary>A player who owns one <see cref="Starter"/>, with cash left over.</summary>
    private async Task<string> OwnerOfStarterAsync(long spareCash = 0)
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 500 + spareCash - 100);
        var response = await BuyAsync(token, Starter);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return token;
    }

    // -------------------------------------------------------------------------
    // GET /properties
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Properties_without_authorization_returns_401()
    {
        var response = await _client.GetAsync("/properties");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Properties_lists_the_whole_catalog_owning_none_of_them()
    {
        var token = await NewGuestTokenAsync();

        var response = await GetPropertiesAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var properties = doc.RootElement.GetProperty("properties");

        // The whole catalog, not only the rows the player owns — the same rule GET /inventory follows,
        // so the "buy" and "collect" halves of the Properties screen are one list.
        Assert.Equal(6, properties.GetArrayLength());
        Assert.All(properties.EnumerateArray(), p =>
        {
            Assert.Equal(0, p.GetProperty("quantity").GetInt32());
            Assert.Equal(0, p.GetProperty("pending").GetInt64());
            Assert.Equal(0, p.GetProperty("cap").GetInt64());
        });
    }

    [Fact]
    public async Task Properties_carries_the_content_definition_of_each_property()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.Equal("Street Corner", corner.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(corner.GetProperty("description").GetString()));
        Assert.Equal(1, corner.GetProperty("levelRequirement").GetInt32());
        Assert.Equal(500, corner.GetProperty("price").GetInt64());
        Assert.Equal(240, corner.GetProperty("incomePerHour").GetInt64());
        Assert.Equal(15, corner.GetProperty("accrualCapMinutes").GetInt32());
    }

    [Fact]
    public async Task Properties_reports_the_cash_and_level_the_gating_was_computed_against()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));

        Assert.Equal(100, doc.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task A_new_player_cannot_afford_the_cheapest_property()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.False(corner.GetProperty("canBuy").GetBoolean());
        Assert.Equal("cash", corner.GetProperty("buyUnavailableReason").GetString());
    }

    [Fact]
    public async Task A_property_above_the_players_level_reports_level_ahead_of_cash()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var gated = PropertyNamed(doc.RootElement, GatedProperty);

        // The player fails both gates. Level is reported because it is the permanent obstacle — the
        // one that will not fix itself by earning more.
        Assert.False(gated.GetProperty("canBuy").GetBoolean());
        Assert.Equal("level", gated.GetProperty("buyUnavailableReason").GetString());
    }

    [Fact]
    public async Task An_affordable_property_reports_no_obstacle()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 400); // 100 + 400 = the 500 price exactly

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.True(corner.GetProperty("canBuy").GetBoolean());
        Assert.Null(corner.GetProperty("buyUnavailableReason").GetString());
    }

    [Fact]
    public async Task An_unowned_property_cannot_be_collected()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.False(corner.GetProperty("canCollect").GetBoolean());
        Assert.Equal("notOwned", corner.GetProperty("collectUnavailableReason").GetString());
    }

    [Fact]
    public async Task A_freshly_bought_property_has_nothing_to_collect_yet()
    {
        var token = await OwnerOfStarterAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.Equal(1, corner.GetProperty("quantity").GetInt32());
        Assert.False(corner.GetProperty("canCollect").GetBoolean());
        Assert.Equal("nothingToCollect", corner.GetProperty("collectUnavailableReason").GetString());
    }

    [Fact]
    public async Task The_listing_reports_pending_income_and_the_cap_it_is_filling()
    {
        var token = await OwnerOfStarterAsync();
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromMinutes(5));

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.Equal(5 * StarterPerMinute, corner.GetProperty("pending").GetInt64());
        Assert.Equal(StarterCapPerUnit, corner.GetProperty("cap").GetInt64());
        Assert.True(corner.GetProperty("canCollect").GetBoolean());
        Assert.Null(corner.GetProperty("collectUnavailableReason").GetString());
    }

    [Fact]
    public async Task Reading_the_listing_does_not_settle_the_holding()
    {
        var token = await OwnerOfStarterAsync();
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromMinutes(5));

        // A polling client reads this endpoint constantly. If a read settled the holding it would
        // reset the income clock and quietly discard the part-earned coin every time.
        for (int i = 0; i < 5; i++)
            (await GetPropertiesAsync(token)).Dispose();

        using var collected = await BodyAsync(await CollectAsync(token, Starter));

        Assert.Equal(5 * StarterPerMinute, collected.RootElement.GetProperty("cashCollected").GetInt64());
    }

    // -------------------------------------------------------------------------
    // POST /properties/{id}/buy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Buying_without_authorization_returns_401()
    {
        var response = await _client.PostAsync($"/properties/{Starter}/buy", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Buying_a_property_charges_its_price_and_returns_the_new_state()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 400); // 500 exactly

        var response = await BuyAsync(token, Starter);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var root = doc.RootElement;

        Assert.Equal(Starter, root.GetProperty("propertyId").GetString());
        Assert.Equal(500, root.GetProperty("cashSpent").GetInt64());
        Assert.Equal(1, root.GetProperty("quantityOwned").GetInt32());
        Assert.Equal(0, root.GetProperty("pending").GetInt64());
        Assert.Equal(StarterCapPerUnit, root.GetProperty("cap").GetInt64());

        // The full post-action player rides along, so the client needs no follow-up GET /me.
        Assert.Equal(0, root.GetProperty("player").GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task A_bought_property_shows_in_the_listing_with_its_quantity_and_cap()
    {
        var token = await OwnerOfStarterAsync();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.Equal(1, corner.GetProperty("quantity").GetInt32());
        Assert.Equal(StarterCapPerUnit, corner.GetProperty("cap").GetInt64());
    }

    [Fact]
    public async Task Buying_a_second_unit_raises_the_quantity_rather_than_the_row_count()
    {
        var token = await OwnerOfStarterAsync(spareCash: 500);

        using var doc = await BodyAsync(await BuyAsync(token, Starter));

        Assert.Equal(2, doc.RootElement.GetProperty("quantityOwned").GetInt32());

        // The cap scales with the units owned, so a second unit doubles the takings rather than
        // filling the same ceiling twice as fast.
        Assert.Equal(2 * StarterCapPerUnit, doc.RootElement.GetProperty("cap").GetInt64());

        using var listing = await BodyAsync(await GetPropertiesAsync(token));
        Assert.Equal(6, listing.RootElement.GetProperty("properties").GetArrayLength());
    }

    [Fact]
    public async Task Buying_a_property_the_player_cannot_afford_returns_409_cash()
    {
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, Starter);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("cash", ReasonOf(problem));
    }

    [Fact]
    public async Task A_refused_purchase_leaves_the_player_untouched()
    {
        var token = await NewGuestTokenAsync();

        await BuyAsync(token, Starter);

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        Assert.Equal(100, doc.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(0, PropertyNamed(doc.RootElement, Starter).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Buying_a_property_above_the_players_level_returns_409_level()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 100_000);

        var response = await BuyAsync(token, GatedProperty);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("level", ReasonOf(problem));
    }

    [Fact]
    public async Task Buying_an_unknown_property_returns_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, "no-such-property");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Property_ids_are_matched_case_sensitively()
    {
        var token = await NewGuestTokenAsync();

        // Content ids are keys, not display text. A case-insensitive match would let a second
        // spelling of every id resolve — and a holding keyed on that spelling would accrue separately.
        var response = await BuyAsync(token, "Street-Corner");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST /properties/{id}/collect
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Collecting_without_authorization_returns_401()
    {
        var response = await _client.PostAsync($"/properties/{Starter}/collect", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collecting_pays_the_accrued_income_into_the_players_cash()
    {
        var token = await OwnerOfStarterAsync(spareCash: 7);
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromMinutes(5));

        var response = await CollectAsync(token, Starter);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var root = doc.RootElement;

        Assert.Equal(Starter, root.GetProperty("propertyId").GetString());
        Assert.Equal(5 * StarterPerMinute, root.GetProperty("cashCollected").GetInt64());
        Assert.Equal(0, root.GetProperty("pending").GetInt64());
        Assert.Equal(StarterCapPerUnit, root.GetProperty("cap").GetInt64());
        Assert.Equal(7 + 5 * StarterPerMinute, root.GetProperty("player").GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Collecting_empties_the_holding()
    {
        var token = await OwnerOfStarterAsync();
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromMinutes(5));

        (await CollectAsync(token, Starter)).Dispose();

        using var doc = await BodyAsync(await GetPropertiesAsync(token));
        var corner = PropertyNamed(doc.RootElement, Starter);

        Assert.Equal(0, corner.GetProperty("pending").GetInt64());
        Assert.Equal(1, corner.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Collecting_stops_at_the_cap_however_long_the_property_was_left()
    {
        var token = await OwnerOfStarterAsync();
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromDays(3));

        using var doc = await BodyAsync(await CollectAsync(token, Starter));

        // Three days at 4 a minute would be 17,280 without the window. The cap is the reason to
        // come back.
        Assert.Equal(StarterCapPerUnit, doc.RootElement.GetProperty("cashCollected").GetInt64());
    }

    [Fact]
    public async Task Collecting_a_property_the_player_does_not_own_returns_409_notOwned()
    {
        var token = await NewGuestTokenAsync();

        var response = await CollectAsync(token, Starter);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("notOwned", ReasonOf(problem));
    }

    [Fact]
    public async Task Collecting_before_a_whole_coin_has_accrued_returns_409_nothingToCollect()
    {
        var token = await OwnerOfStarterAsync();

        var response = await CollectAsync(token, Starter);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("nothingToCollect", ReasonOf(problem));
    }

    [Fact]
    public async Task A_refused_collection_does_not_reset_the_income_clock()
    {
        var token = await OwnerOfStarterAsync();
        await BackdateAccrualAsync(token, Starter, TimeSpan.FromSeconds(10));

        // Ten seconds is under half a coin, so each of these is refused. If a refusal settled the
        // holding, a player tapping the button would earn nothing at all.
        for (int i = 0; i < 5; i++)
            (await CollectAsync(token, Starter)).Dispose();

        await BackdateAccrualAsync(token, Starter, TimeSpan.FromMinutes(5));

        using var doc = await BodyAsync(await CollectAsync(token, Starter));
        Assert.True(doc.RootElement.GetProperty("cashCollected").GetInt64() >= 5 * StarterPerMinute);
    }

    [Fact]
    public async Task Collecting_an_unknown_property_returns_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await CollectAsync(token, "no-such-property");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
