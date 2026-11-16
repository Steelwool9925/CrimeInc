using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// The statistical properties of daily selection, against the real pool: it is deterministic, and it
/// spreads. A naive modulo of the date would pass the first and fail the second, which is why these
/// are measured rather than assumed.
/// </summary>
public class ObjectiveSelectionTests
{
    private static readonly ContentPackLoader Loader = new(RepoContent.FindRoot());
    private static readonly IReadOnlyList<Objective> Pool =
        Loader.LoadObjectives("crime", Loader.LoadJobs("crime", Loader.LoadItems("crime"))).Objectives;

    private readonly DailyObjectiveService _service = new();

    /// <summary>A level at which every objective in the pool is eligible.</summary>
    private static readonly int AllEligibleLevel = Pool.Max(o => o.MinLevel);

    private static readonly DateOnly Start = new(2026, 10, 1);

    private static Guid Player(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private IReadOnlyList<string> Ids(Guid player, DateOnly day, int level) =>
        _service.SelectFor(Pool, player, day, level).Select(o => o.Id).ToList();

    [Fact]
    public void The_same_player_and_day_always_get_the_same_objectives()
    {
        for (int p = 0; p < 20; p++)
            Assert.Equal(Ids(Player(p), Start, AllEligibleLevel), Ids(Player(p), Start, AllEligibleLevel));
    }

    [Fact]
    public void A_player_sees_a_spread_of_the_pool_over_thirty_days_not_a_short_cycle()
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int d = 0; d < 30; d++)
            foreach (var id in Ids(Player(1), Start.AddDays(d), AllEligibleLevel))
                seen[id] = seen.GetValueOrDefault(id) + 1;

        // 30 days x 3 picks from 15: about 6 of each. Most of the pool must appear, and none may dominate.
        Assert.True(seen.Count >= Pool.Count * 4 / 5,
            $"Only {seen.Count} of {Pool.Count} objectives appeared in 30 days.");
        Assert.True(seen.Values.Max() <= 15, $"One objective appeared on {seen.Values.Max()} of 30 days.");
    }

    [Fact]
    public void Consecutive_days_rarely_repeat_the_same_three()
    {
        int repeats = 0, pairs = 0;
        for (int p = 0; p < 5; p++)
            for (int d = 0; d < 29; d++, pairs++)
            {
                var today = Ids(Player(p), Start.AddDays(d), AllEligibleLevel).Order().ToList();
                var tomorrow = Ids(Player(p), Start.AddDays(d + 1), AllEligibleLevel).Order().ToList();
                if (today.SequenceEqual(tomorrow)) repeats++;
            }

        Assert.True(repeats <= 2, $"{repeats} of {pairs} consecutive-day pairs were identical.");
    }

    [Fact]
    public void Across_many_players_on_one_day_every_objective_is_handed_out_about_as_often()
    {
        const int players = 300;
        var counts = Pool.ToDictionary(o => o.Id, _ => 0, StringComparer.Ordinal);
        for (int p = 0; p < players; p++)
            foreach (var id in Ids(Player(1000 + p), Start, AllEligibleLevel))
                counts[id]++;

        // Expected 300 x 3 / 15 = 60 each; four standard deviations is about 28 either side.
        Assert.All(counts, kv => Assert.InRange(kv.Value, 30, 90));
    }

    [Fact]
    public void Two_players_on_the_same_day_do_not_all_get_the_same_set()
    {
        var sets = Enumerable.Range(0, 50).Select(p => string.Join(",", Ids(Player(p), Start, AllEligibleLevel).Order())).Distinct().Count();

        Assert.True(sets >= 30, $"Only {sets} distinct sets across 50 players.");
    }

    [Fact]
    public void A_level_one_player_is_only_ever_offered_level_one_objectives()
    {
        for (int d = 0; d < 60; d++)
            Assert.All(_service.SelectFor(Pool, Player(7), Start.AddDays(d), playerLevel: 1), o => Assert.Equal(1, o.MinLevel));
    }

    [Fact]
    public void Every_day_offers_the_full_number_at_every_level()
    {
        for (int level = 1; level <= AllEligibleLevel; level++)
            for (int d = 0; d < 10; d++)
                Assert.Equal(DailyObjectiveService.ObjectivesPerDay, Ids(Player(3), Start.AddDays(d), level).Count);
    }

    [Fact]
    public void The_pool_is_never_asked_to_repeat_an_objective_within_a_day()
    {
        for (int p = 0; p < 50; p++)
        {
            var ids = Ids(Player(p), Start, AllEligibleLevel);
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }
}
