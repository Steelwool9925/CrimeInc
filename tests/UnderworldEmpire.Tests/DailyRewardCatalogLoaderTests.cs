using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>Loading and validating <c>daily-rewards.json</c>.</summary>
public class DailyRewardCatalogLoaderTests : IDisposable
{
    private const string PackId = "crime";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daily-reward-loader-" + Guid.NewGuid().ToString("N"));

    public DailyRewardCatalogLoaderTests() => Directory.CreateDirectory(Path.Combine(_root, PackId));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DailyRewardCatalog Load(string json)
    {
        File.WriteAllText(Path.Combine(_root, PackId, "daily-rewards.json"), json);
        return new ContentPackLoader(_root).LoadDailyRewards(PackId);
    }

    private static string Rows(string rows) => $$"""{ "packId": "crime", "version": 1, "dailyRewards": [ {{rows}} ] }""";

    private string Errors(string json) =>
        Assert.Throws<ContentPackException>(() => Load(json)).Message;

    [Fact]
    public void The_shipped_table_loads()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadDailyRewards(PackId);

        Assert.NotEmpty(catalog.Rewards);
        Assert.Equal(1, catalog.Rewards[0].Day);
        // Contiguous from 1 — ValidateDailyRewards already proves this, but the invariant matters
        // enough to the catalog's own index-by-(day - 1) lookup to pin it here too.
        Assert.Equal(Enumerable.Range(1, catalog.Rewards.Count), catalog.Rewards.Select(r => r.Day));
    }

    [Fact]
    public void A_valid_table_round_trips_in_day_order()
    {
        var catalog = Load(Rows("""{ "day": 2, "cash": 75, "xp": 15 }, { "day": 1, "cash": 50, "xp": 10 }"""));

        Assert.Equal([1, 2], catalog.Rewards.Select(r => r.Day));
        Assert.Equal(50, catalog.Rewards[0].Cash);
    }

    [Fact]
    public void An_empty_table_is_rejected()
    {
        Assert.Contains("is missing or empty", Errors(Rows("")));
    }

    [Fact]
    public void A_duplicate_day_is_rejected()
    {
        Assert.Contains("duplicate 'day'",
            Errors(Rows("""{ "day": 1, "cash": 10, "xp": 0 }, { "day": 1, "cash": 20, "xp": 0 }""")));
    }

    [Fact]
    public void A_gap_in_the_day_sequence_is_rejected()
    {
        Assert.Contains("missing day 2",
            Errors(Rows("""{ "day": 1, "cash": 10, "xp": 0 }, { "day": 3, "cash": 20, "xp": 0 }""")));
    }

    [Fact]
    public void A_zero_day_is_rejected()
    {
        Assert.Contains("'day' must be >= 1", Errors(Rows("""{ "day": 0, "cash": 10, "xp": 0 }""")));
    }

    [Fact]
    public void A_reward_that_pays_nothing_is_rejected()
    {
        Assert.Contains("reward must pay cash or xp", Errors(Rows("""{ "day": 1, "cash": 0, "xp": 0 }""")));
    }

    [Fact]
    public void A_negative_cash_or_xp_is_rejected()
    {
        Assert.Contains("'cash' must be >= 0", Errors(Rows("""{ "day": 1, "cash": -5, "xp": 0 }""")));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var message = Errors(Rows(
            """{ "day": 1, "cash": -1, "xp": 0 }, { "day": 1, "cash": 10, "xp": 0 }"""));

        Assert.Contains("'cash'", message);
        Assert.Contains("duplicate 'day'", message);
    }

    [Fact]
    public void A_missing_file_is_a_content_error()
    {
        Assert.Throws<ContentPackException>(() => new ContentPackLoader(_root).LoadDailyRewards(PackId));
    }
}
