namespace UnderworldEmpire.Api.Contracts;

/// <summary>The reward for one streak day, as the client sees it.</summary>
/// <param name="Day">The streak day this reward belongs to.</param>
/// <param name="Cash">Cash paid on claim.</param>
/// <param name="Xp">Experience paid on claim.</param>
public record DailyRewardDto(int Day, long Cash, long Xp);

/// <summary>Response body for GET /daily.</summary>
/// <param name="Claimable">Whether a claim right now would be accepted.</param>
/// <param name="CurrentStreak">The player's stored streak, before any claim.</param>
/// <param name="LongestStreak">The player's longest streak ever reached.</param>
/// <param name="NextReward">What claiming right now would pay.</param>
/// <param name="ResetsAtUtc">When today's claim window ends.</param>
public record DailyStatusResponse(
    bool Claimable, int CurrentStreak, int LongestStreak, DailyRewardDto NextReward, DateTimeOffset ResetsAtUtc);

/// <summary>Response body for POST /daily/claim.</summary>
/// <param name="Reward">What was paid.</param>
/// <param name="CurrentStreak">The player's streak after this claim.</param>
/// <param name="LongestStreak">The player's longest streak after this claim.</param>
/// <param name="Player">The player's full state after the payout.</param>
/// <param name="LevelUp">Level-up detail when the XP reward crossed a threshold; otherwise null.</param>
public record ClaimDailyResponse(
    DailyRewardDto Reward, int CurrentStreak, int LongestStreak, MeDto Player, LevelUpDto? LevelUp);
