using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /me (T1.5 — player profile projection).
/// Requires the Postgres container on port 5433 with the <c>underworld_test</c> database.
/// </summary>
public class MeEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MeEndpointTests(CustomWebApplicationFactory factory)
    {
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // GET /me — unauthenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_me_without_authorization_header_returns_401()
    {
        var response = await _client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // GET /me — authenticated brand-new player
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_me_for_new_player_returns_200_with_correct_defaults()
    {
        // Create a fresh session.
        var sessionResponse = await _client.PostAsJsonAsync("/session", new { });
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var sessionBody = await sessionResponse.Content.ReadAsStringAsync();
        using var sessionDoc = JsonDocument.Parse(sessionBody);
        var deviceToken = sessionDoc.RootElement.GetProperty("deviceToken").GetString()!;
        var expectedPlayerId = sessionDoc.RootElement.GetProperty("playerId").GetString()!;

        // GET /me with the device token.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add("Authorization", $"Bearer {deviceToken}");
        var meResponse = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var meBody = await meResponse.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meBody);
        var root = meDoc.RootElement;

        // Identity
        Assert.Equal(expectedPlayerId, root.GetProperty("playerId").GetString());

        // Currencies
        Assert.Equal(PlayerDefaults.StartingCash, root.GetProperty("cash").GetInt64());
        Assert.Equal(PlayerDefaults.StartingPremiumCurrency, root.GetProperty("premiumCurrency").GetInt64());

        // Progression
        Assert.Equal(PlayerDefaults.StartingLevel, root.GetProperty("level").GetInt32());
        Assert.Equal(PlayerDefaults.BaseAttack, root.GetProperty("attack").GetInt32());
        Assert.Equal(PlayerDefaults.BaseDefense, root.GetProperty("defense").GetInt32());

        // Energy meter — brand-new player is full
        var energy = root.GetProperty("energy");
        var energyMax = energy.GetProperty("max").GetInt32();
        var energyCurrent = energy.GetProperty("current").GetInt32();
        var energyRegen = energy.GetProperty("regenSecondsPerPoint").GetInt32();

        Assert.Equal(PlayerDefaults.EnergyMax, energyMax);
        Assert.Equal(energyMax, energyCurrent);           // Current == Max for a brand-new player
        Assert.Equal(300, energyRegen);                   // 5 minutes = 300 seconds
        Assert.True(energy.GetProperty("secondsUntilNextPoint").ValueKind == JsonValueKind.Null,
            "secondsUntilNextPoint must be null when the meter is full");
    }
}
