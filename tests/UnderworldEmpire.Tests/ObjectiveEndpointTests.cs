using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>GET /objectives over real HTTP against real Postgres. Requires the container on port 5433.</summary>
public class ObjectiveEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ObjectiveEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    private async Task<string> NewGuestTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/session", new { });
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<HttpResponseMessage> GetObjectivesAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/objectives");
        if (token is not null) request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Requires_a_session()
    {
        var response = await GetObjectivesAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_new_player_is_offered_three_objectives_with_no_progress()
    {
        var token = await NewGuestTokenAsync();

        var response = await GetObjectivesAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var objectives = body.RootElement.GetProperty("objectives").EnumerateArray().ToList();
        Assert.Equal(3, objectives.Count);
        foreach (var o in objectives)
        {
            Assert.Equal(0, o.GetProperty("progress").GetInt64());
            Assert.False(o.GetProperty("complete").GetBoolean());
            Assert.False(o.GetProperty("claimed").GetBoolean());
            Assert.True(o.GetProperty("target").GetInt64() >= 1);
        }
    }

    [Fact]
    public async Task A_level_one_player_is_only_offered_level_one_objectives()
    {
        // Derived from the pool, so adding a level-1 objective to content cannot break this.
        var levelOne = _factory.Services.GetRequiredService<IContentPackProvider>().ObjectiveCatalog.Objectives
            .Where(o => o.MinLevel == 1)
            .Select(o => o.Id)
            .ToList();
        var token = await NewGuestTokenAsync();
        using var body = JsonDocument.Parse(await (await GetObjectivesAsync(token)).Content.ReadAsStringAsync());

        var ids = body.RootElement.GetProperty("objectives").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()).ToList();

        Assert.All(ids, id => Assert.Contains(id, levelOne));
    }

    [Fact]
    public async Task Reading_twice_returns_the_same_set()
    {
        var token = await NewGuestTokenAsync();

        var first = await (await GetObjectivesAsync(token)).Content.ReadAsStringAsync();
        var second = await (await GetObjectivesAsync(token)).Content.ReadAsStringAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reports_todays_utc_date_and_the_next_reset()
    {
        var token = await NewGuestTokenAsync();
        var before = DateTimeOffset.UtcNow;
        using var body = JsonDocument.Parse(await (await GetObjectivesAsync(token)).Content.ReadAsStringAsync());

        var date = body.RootElement.GetProperty("date").GetString();
        var resets = body.RootElement.GetProperty("resetsAtUtc").GetDateTimeOffset();

        Assert.Contains(date, new[] { UtcDay.Of(before).ToString("yyyy-MM-dd"), UtcDay.Of(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd") });
        Assert.Equal(TimeSpan.Zero, resets.TimeOfDay);
        Assert.True(resets > before);
    }
}
