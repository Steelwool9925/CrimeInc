using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for POST /session and GET /whoami (T1.2 — guest identity + bearer auth).
/// Requires the Postgres container on port 5433 with the <c>underworld_test</c> database.
/// </summary>
public class SessionEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SessionEndpointTests(CustomWebApplicationFactory factory)
    {
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // POST /session — new guest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_session_with_empty_body_returns_200_with_device_token_and_player_id()
    {
        var response = await _client.PostAsJsonAsync("/session", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var deviceToken = doc.RootElement.GetProperty("deviceToken").GetString();
        var playerId = doc.RootElement.GetProperty("playerId").GetString();

        Assert.NotNull(deviceToken);
        Assert.NotEmpty(deviceToken);
        Assert.NotNull(playerId);
        Assert.True(Guid.TryParse(playerId, out _), "playerId must be a valid GUID");
    }

    // -------------------------------------------------------------------------
    // POST /session — returning guest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_session_with_existing_device_token_returns_same_player_id()
    {
        // Create a new session first.
        var createResponse = await _client.PostAsJsonAsync("/session", new { });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var deviceToken = createDoc.RootElement.GetProperty("deviceToken").GetString()!;
        var originalPlayerId = createDoc.RootElement.GetProperty("playerId").GetString()!;

        // Return with the same token.
        var returnResponse = await _client.PostAsJsonAsync("/session", new { deviceToken });
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var returnBody = await returnResponse.Content.ReadAsStringAsync();
        using var returnDoc = JsonDocument.Parse(returnBody);
        var returnedPlayerId = returnDoc.RootElement.GetProperty("playerId").GetString();

        Assert.Equal(originalPlayerId, returnedPlayerId);
    }

    // -------------------------------------------------------------------------
    // GET /whoami — unauthenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_whoami_without_authorization_header_returns_401()
    {
        var response = await _client.GetAsync("/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // GET /whoami — authenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_whoami_with_valid_bearer_token_returns_200_and_correct_player_id()
    {
        // Obtain credentials.
        var sessionResponse = await _client.PostAsJsonAsync("/session", new { });
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var sessionBody = await sessionResponse.Content.ReadAsStringAsync();
        using var sessionDoc = JsonDocument.Parse(sessionBody);
        var deviceToken = sessionDoc.RootElement.GetProperty("deviceToken").GetString()!;
        var expectedPlayerId = sessionDoc.RootElement.GetProperty("playerId").GetString()!;

        // Call /whoami with the token.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("Authorization", $"Bearer {deviceToken}");
        var whoamiResponse = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, whoamiResponse.StatusCode);

        var whoamiBody = await whoamiResponse.Content.ReadAsStringAsync();
        using var whoamiDoc = JsonDocument.Parse(whoamiBody);
        var returnedPlayerId = whoamiDoc.RootElement.GetProperty("playerId").GetString();

        Assert.Equal(expectedPlayerId, returnedPlayerId);
    }
}
