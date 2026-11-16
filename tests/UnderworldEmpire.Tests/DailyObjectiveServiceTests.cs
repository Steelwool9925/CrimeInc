using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Tests;

/// <summary>Pure rules: the deterministic daily selection and the UTC day helper.</summary>
public class DailyObjectiveServiceTests
{
    private readonly DailyObjectiveService _service = new();

    private static readonly Guid Player = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateOnly Day = new(2026, 10, 13);

    private static Objective Make(string id, int minLevel = 1) => new()
    {
        Id = id,
        Name = id,
        Type = ObjectiveType.CompleteJobTimes,
        Target = 1,
        MinLevel = minLevel,
        RewardCash = 10
    };

    private static List<Objective> Pool(int size, int minLevel = 1) =>
        Enumerable.Range(0, size).Select(i => Make($"obj-{i:D2}", minLevel)).ToList();

    [Fact]
    public void Offers_three_distinct_objectives()
    {
        var picked = _service.SelectFor(Pool(10), Player, Day, playerLevel: 1);

        Assert.Equal(DailyObjectiveService.ObjectivesPerDay, picked.Count);
        Assert.Equal(3, picked.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void The_same_inputs_give_the_same_set_in_the_same_order()
    {
        var pool = Pool(10);
        var first = _service.SelectFor(pool, Player, Day, 1).Select(o => o.Id);
        var second = _service.SelectFor(pool, Player, Day, 1).Select(o => o.Id);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Pool_declaration_order_does_not_change_the_set()
    {
        var pool = Pool(10);
        var reversed = pool.AsEnumerable().Reverse().ToList();

        Assert.Equal(
            _service.SelectFor(pool, Player, Day, 1).Select(o => o.Id).OrderBy(x => x),
            _service.SelectFor(reversed, Player, Day, 1).Select(o => o.Id).OrderBy(x => x));
    }

    [Fact]
    public void A_different_date_gives_a_different_set_for_some_day_in_a_week()
    {
        var pool = Pool(10);
        var today = _service.SelectFor(pool, Player, Day, 1).Select(o => o.Id).ToHashSet();

        var differs = Enumerable.Range(1, 7)
            .Any(d => !_service.SelectFor(pool, Player, Day.AddDays(d), 1).Select(o => o.Id).ToHashSet().SetEquals(today));

        Assert.True(differs);
    }

    [Fact]
    public void Different_players_are_not_all_given_the_same_set()
    {
        var pool = Pool(10);
        var sets = Enumerable.Range(0, 20)
            .Select(_ => string.Join(",", _service.SelectFor(pool, Guid.NewGuid(), Day, 1).Select(o => o.Id).OrderBy(x => x)))
            .Distinct()
            .Count();

        Assert.True(sets > 1);
    }

    [Fact]
    public void An_objective_above_the_player_level_is_never_offered()
    {
        var pool = new List<Objective> { Make("easy-a"), Make("easy-b"), Make("hard", minLevel: 8) };

        for (int d = 0; d < 30; d++)
        {
            var picked = _service.SelectFor(pool, Player, Day.AddDays(d), playerLevel: 2);
            Assert.DoesNotContain(picked, o => o.Id == "hard");
        }
    }

    [Fact]
    public void A_pool_smaller_than_three_offers_what_it_has()
    {
        var picked = _service.SelectFor(Pool(2), Player, Day, 1);

        Assert.Equal(2, picked.Count);
    }

    [Fact]
    public void Nothing_eligible_offers_nothing()
    {
        Assert.Empty(_service.SelectFor(Pool(5, minLevel: 9), Player, Day, playerLevel: 1));
    }

    [Fact]
    public void The_utc_day_ignores_the_offset_the_instant_was_written_in()
    {
        // 23:30 on the 12th at UTC-5 is 04:30 on the 13th in UTC.
        var instant = new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.FromHours(-5));

        Assert.Equal(new DateOnly(2026, 10, 13), UtcDay.Of(instant));
    }

    [Fact]
    public void The_next_day_starts_at_utc_midnight()
    {
        var instant = new DateTimeOffset(2026, 10, 13, 17, 45, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), UtcDay.NextStart(instant));
    }
}
