namespace UnderworldEmpire.Api.Contracts;

/// <summary>
/// Level-up detail, returned when an action crossed one or more XP thresholds.
/// </summary>
/// <param name="LevelsGained">How many levels were gained — more than one is possible from a single large payout.</param>
/// <param name="NewLevel">The level afterwards.</param>
/// <param name="SkillPointsGranted">Skill points awarded.</param>
/// <param name="MetersRefilled">Whether energy, stamina and health were refilled as the level-up reward.</param>
/// <param name="PayoutBonusGained">
///     The extra share of cash and XP that every job now pays because of the levels just gained: 0.02 is
///     +2%. This is the grant beyond the skill point; zero only when the pack sets no level bonus.
/// </param>
/// <param name="PayoutBonus">The player's total level pay bonus at <paramref name="NewLevel"/>.</param>
public record LevelUpDto(
    int LevelsGained, int NewLevel, int SkillPointsGranted, bool MetersRefilled,
    double PayoutBonusGained, double PayoutBonus);

/// <summary>Request body for POST /skills/allocate.</summary>
/// <param name="Target">
///     Which stat to raise. Accepts <c>"attack"</c> or <c>"defense"</c>, case-insensitively; any
///     other value is a 400. Parsed into an enum at the boundary so no unvalidated string reaches
///     the domain.
/// </param>
public record AllocateSkillRequest(string? Target);

/// <summary>Response body for POST /skills/allocate.</summary>
/// <param name="Player">The player's full state after the point was spent, so the client needs no follow-up GET /me.</param>
public record AllocateSkillResponse(MeDto Player);
