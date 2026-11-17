using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Tests;

/// <summary>Pure rules for the daily login streak: claimability, extension, and reset.</summary>
public class DailyStreakTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Day(int offsetDays) => Day1.AddDays(offsetDays);

    [Fact]
    public void A_brand_new_streak_is_claimable()
    {
        var streak = new DailyStreak();

        Assert.True(streak.IsClaimableAt(Day1));
    }

    [Fact]
    public void The_first_ever_claim_starts_the_streak_at_one()
    {
        var streak = new DailyStreak();

        Assert.Equal(DailyFailure.None, streak.Claim(Day1));

        Assert.Equal(1, streak.CurrentStreak);
        Assert.Equal(1, streak.LongestStreak);
        Assert.Equal(UtcDay.Of(Day1), streak.LastClaimedUtcDate);
    }

    [Fact]
    public void A_second_claim_the_same_day_is_refused_and_changes_nothing()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);

        Assert.Equal(DailyFailure.AlreadyClaimedToday, streak.Claim(Day1.AddHours(6)));

        Assert.Equal(1, streak.CurrentStreak);
        Assert.False(streak.IsClaimableAt(Day1.AddHours(6)));
    }

    [Fact]
    public void Claiming_the_very_next_day_extends_the_streak()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);

        Assert.Equal(DailyFailure.None, streak.Claim(Day(1)));

        Assert.Equal(2, streak.CurrentStreak);
        Assert.Equal(2, streak.LongestStreak);
    }

    [Fact]
    public void Skipping_a_day_resets_the_streak_to_one()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);
        streak.Claim(Day(1));

        // Day 2 is skipped entirely; the next claim lands on day 3.
        Assert.Equal(DailyFailure.None, streak.Claim(Day(3)));

        Assert.Equal(1, streak.CurrentStreak);
    }

    [Fact]
    public void Longest_streak_survives_a_later_reset()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);
        streak.Claim(Day(1));
        streak.Claim(Day(2));
        Assert.Equal(3, streak.LongestStreak);

        streak.Claim(Day(10)); // a long gap resets the current streak

        Assert.Equal(1, streak.CurrentStreak);
        Assert.Equal(3, streak.LongestStreak);
    }

    [Fact]
    public void StreakIfClaimedAt_previews_an_extension_without_mutating()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);

        Assert.Equal(2, streak.StreakIfClaimedAt(Day(1)));
        Assert.Equal(1, streak.CurrentStreak); // unchanged
    }

    [Fact]
    public void StreakIfClaimedAt_previews_a_reset_without_mutating()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);

        Assert.Equal(1, streak.StreakIfClaimedAt(Day(5)));
        Assert.Equal(1, streak.CurrentStreak); // unchanged
    }

    [Fact]
    public void StreakIfClaimedAt_on_an_already_claimed_day_returns_what_was_paid()
    {
        var streak = new DailyStreak();
        streak.Claim(Day1);
        streak.Claim(Day(1));

        Assert.Equal(2, streak.StreakIfClaimedAt(Day(1).AddHours(3)));
    }
}
