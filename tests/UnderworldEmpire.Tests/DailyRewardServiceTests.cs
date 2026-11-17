using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Tests;

/// <summary>Pure rules for looking up what a streak day pays.</summary>
public class DailyRewardServiceTests
{
    private static readonly List<DailyReward> Table =
    [
        new() { Day = 1, Cash = 50, Xp = 10 },
        new() { Day = 2, Cash = 75, Xp = 15 },
        new() { Day = 3, Cash = 100, Xp = 20 }
    ];

    private readonly DailyRewardService _service = new();

    [Fact]
    public void Day_one_returns_the_first_entry()
    {
        Assert.Equal(Table[0], _service.RewardFor(Table, 1));
    }

    [Fact]
    public void A_day_within_the_table_returns_its_own_entry()
    {
        Assert.Equal(Table[1], _service.RewardFor(Table, 2));
    }

    [Fact]
    public void A_day_beyond_the_table_clamps_to_the_last_entry()
    {
        Assert.Equal(Table[2], _service.RewardFor(Table, 4));
        Assert.Equal(Table[2], _service.RewardFor(Table, 365));
    }

    [Fact]
    public void A_day_below_one_clamps_to_the_first_entry()
    {
        Assert.Equal(Table[0], _service.RewardFor(Table, 0));
        Assert.Equal(Table[0], _service.RewardFor(Table, -5));
    }

    [Fact]
    public void An_empty_table_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _service.RewardFor([], 1));
    }
}
