using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// The loaded, validated daily-login reward table. Immutable and shared as a singleton. Unlike
/// <see cref="ObjectiveCatalog"/>'s by-id dictionary, a claim resolves by streak day number, so
/// the table is kept as a plain ordered list — <see cref="DailyRewardService.RewardFor"/> indexes
/// it directly, relying on <c>ContentPackLoader.ValidateDailyRewards</c> to guarantee the days run
/// contiguously from 1 with no gaps.
/// </summary>
public sealed class DailyRewardCatalog
{
    /// <param name="rewards">Validated rewards, ordered by day ascending, contiguous from 1.</param>
    public DailyRewardCatalog(IReadOnlyList<DailyReward> rewards)
    {
        Rewards = rewards;
    }

    /// <summary>Every reward entry, in ascending day order.</summary>
    public IReadOnlyList<DailyReward> Rewards { get; }
}
