namespace UnderworldEmpire.Api.Contracts;

/// <summary>One of today's objectives as the calling player sees it.</summary>
/// <param name="Id">Content id; the value for the claim route.</param>
/// <param name="Name">Player-facing title.</param>
/// <param name="Description">Player-facing detail.</param>
/// <param name="Type">
///     What is counted, as the camelCase token <c>completeDistinctJobs</c>, <c>completeJobTimes</c>,
///     <c>earnCash</c> or <c>collectProperties</c>.
/// </param>
/// <param name="Target">The count (or cash amount) that completes it.</param>
/// <param name="JobId">The job it is about, or <see langword="null"/> when any job counts.</param>
/// <param name="Progress">Progress so far today, never above <paramref name="Target"/>.</param>
/// <param name="Complete">Whether the target is reached.</param>
/// <param name="Claimed">Whether the reward has been taken today.</param>
/// <param name="RewardCash">Cash paid on claim.</param>
/// <param name="RewardXp">Experience paid on claim.</param>
public record ObjectiveDto(
    string Id, string Name, string Description, string Type, long Target, string? JobId,
    long Progress, bool Complete, bool Claimed, long RewardCash, long RewardXp);

/// <summary>Response body for POST /objectives/{id}/claim.</summary>
/// <param name="Claimed">The objective that was paid.</param>
/// <param name="Player">The player's full state after the payout.</param>
/// <param name="LevelUp">Level-up detail when the XP reward crossed a threshold; otherwise null.</param>
/// <param name="Objectives">Today's objectives after the claim.</param>
public record ClaimObjectiveResponse(
    ObjectiveDto Claimed, MeDto Player, LevelUpDto? LevelUp, IReadOnlyList<ObjectiveDto> Objectives);

/// <summary>Response body for GET /objectives.</summary>
/// <param name="Date">The UTC date these objectives belong to, as <c>yyyy-MM-dd</c>.</param>
/// <param name="ResetsAtUtc">When the next set is offered.</param>
/// <param name="Objectives">Today's objectives in a stable order.</param>
public record ObjectivesResponse(string Date, DateTimeOffset ResetsAtUtc, IReadOnlyList<ObjectiveDto> Objectives);
