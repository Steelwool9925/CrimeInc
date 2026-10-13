using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Tests;

/// <summary>
/// The pay bonus a level earns: the second, varying grant that makes every level past the last
/// unlock mean something beyond a skill point.
/// </summary>
public class LevelPayoutTests
{
    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(2, 0.02)]
    [InlineData(11, 0.20)]
    [InlineData(20, 0.38)]
    public void Each_level_past_the_first_adds_the_per_level_bonus(int level, double expected)
    {
        Assert.Equal(expected, LevelPayout.BonusFor(level, 0.02), 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_level_below_the_first_earns_nothing(int level)
    {
        Assert.Equal(0, LevelPayout.BonusFor(level, 0.02));
    }

    [Fact]
    public void A_level_beyond_the_cap_earns_no_more_than_the_cap_does()
    {
        // Accounts made under the old cap of 100 are not demoted, so they can sit above today's cap.
        // They must not out-earn a player who has simply reached it.
        Assert.Equal(
            LevelPayout.BonusFor(PlayerDefaults.MaxLevel, 0.02),
            LevelPayout.BonusFor(35, 0.02),
            9);
    }

    [Fact]
    public void Zero_per_level_earns_nothing_at_any_level()
    {
        for (int level = 1; level <= PlayerDefaults.MaxLevel; level++)
            Assert.Equal(0, LevelPayout.BonusFor(level, 0));
    }

    [Fact]
    public void A_negative_per_level_bonus_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelPayout.BonusFor(5, -0.01));
    }
}
