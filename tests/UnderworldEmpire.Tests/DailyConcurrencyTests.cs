using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Simultaneous claims against one player's daily streak, over real HTTP against real Postgres.
/// Requires the container on port 5433.
/// </summary>
/// <remarks>
/// The streak row is created lazily on the first-ever claim, so two racing first-ever claims both
/// try to INSERT the same (PlayerId)-keyed row — the unique-violation path. Once the row exists, a
/// racing re-claim the same day hits the row's own <c>xmin</c> instead. Both are exercised here,
/// the same split <see cref="PropertyConcurrencyTests"/> and <see cref="ObjectiveConcurrencyTests"/>
/// use for their own lazily-inserted rows.
/// </remarks>
public class DailyConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DailyConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    private async Task<string> NewPlayerTokenAsync()
    {
        using var session = await (await _client.PostAsJsonAsync("/session", new { })).Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(session);
        return doc.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<HttpResponseMessage> ClaimAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/daily/claim");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private async Task<(int CurrentStreak, long RowCount)> StreakStateAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var playerId = await db.Players.Where(p => p.DeviceToken == token).Select(p => p.Id).SingleAsync();
        var rows = await db.DailyStreaks.AsNoTracking().Where(d => d.PlayerId == playerId).ToListAsync();
        return (rows.SingleOrDefault()?.CurrentStreak ?? 0, rows.Count);
    }

    [Fact]
    public async Task Simultaneous_first_ever_claims_pay_exactly_once()
    {
        var token = await NewPlayerTokenAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAsync(token)));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var (currentStreak, rowCount) = await StreakStateAsync(token);
        Assert.Equal(1, currentStreak);
        Assert.Equal(1, rowCount); // exactly one streak row, never a duplicate
    }

    [Fact]
    public async Task A_burst_of_simultaneous_claims_on_an_already_claimed_day_all_conflict()
    {
        var token = await NewPlayerTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(token)).StatusCode);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAsync(token)));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var (currentStreak, _) = await StreakStateAsync(token);
        Assert.Equal(1, currentStreak); // unchanged by the burst
    }
}
