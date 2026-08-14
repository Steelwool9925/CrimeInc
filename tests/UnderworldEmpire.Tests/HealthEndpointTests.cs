using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Boots the real API host in-memory (no Postgres needed — the liveness endpoint does
/// not touch the database) and asserts CP0's contract: GET /health returns 200.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_200_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);
    }
}
