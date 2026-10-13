using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Pins "every level grants something beyond a skill point" as a property of the shipped pack.
/// </summary>
/// <remarks>
/// The core-loop audit found that levels past the last unlock paid only a skill point, which stops
/// mattering once stats overshoot the success ceiling. The per-level pay bonus is the second grant;
/// this asserts it reaches every level up to the cap, not that it equals any particular number.
/// </remarks>
public class LevelRewardInvariantTests
{
    private static double ShippedBonusPerLevel() =>
        new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime").LevelPayoutBonusPerLevel;

    [Fact]
    public void Every_level_from_two_to_the_cap_pays_strictly_more_than_the_one_before()
    {
        double perLevel = ShippedBonusPerLevel();

        for (int level = 2; level <= PlayerDefaults.MaxLevel; level++)
        {
            Assert.True(
                LevelPayout.BonusFor(level, perLevel) > LevelPayout.BonusFor(level - 1, perLevel),
                $"Level {level} pays no more than level {level - 1}: the shipped pack's " +
                $"levels.payoutBonusPerLevel is {perLevel}, so that level grants only a skill point.");
        }
    }

    /// <summary>
    /// The level by which the level bonus must already change the cheapest job's payout. Levels 2 and 3
    /// are announced before they are felt (payouts are whole numbers), and a new player reaches level 4
    /// in their first few sessions; later than that and the first level-ups would read as empty.
    /// </summary>
    private const int LevelByWhichBonusIsFelt = 4;

    [Fact]
    public void The_bonus_changes_the_cheapest_jobs_payout_by_the_early_levels()
    {
        // A bonus that rounds away on the cheapest job is announced and never felt. Payouts round to
        // the nearest whole number, so the bonus must be worth at least half a point of the cheapest
        // job's XP. Checking an early level, not the cap, is what catches a rate too small to matter
        // until the climb is nearly over.
        var jobs = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");
        long cheapestXp = jobs.Jobs.Min(j => j.Xp);
        double bonus = LevelPayout.BonusFor(LevelByWhichBonusIsFelt, ShippedBonusPerLevel());

        Assert.True(cheapestXp * bonus >= 0.5,
            $"At level {LevelByWhichBonusIsFelt} the level bonus is {bonus:P0}, which rounds to no change " +
            $"on a {cheapestXp} XP job.");
    }
}
