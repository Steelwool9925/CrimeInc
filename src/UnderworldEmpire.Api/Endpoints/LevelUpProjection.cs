using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>
/// Turns a <see cref="LevelUpOutcome"/> into the wire shape. Shared by every endpoint whose XP grant can
/// level a player, so the level pay bonus the client is told about is computed in exactly one place.
/// </summary>
internal static class LevelUpProjection
{
    // Float arithmetic leaves noise (0.1 x 3 is 0.30000000000000004) that would otherwise reach the wire.
    internal static double Tidy(double bonus) => Math.Round(bonus, 6);

    /// <summary>The level-up detail, or <see langword="null"/> when no threshold was crossed.</summary>
    internal static LevelUpDto? ToDto(LevelUpOutcome? outcome, JobCatalog catalog) =>
        outcome is { LeveledUp: true } lvl
            ? new LevelUpDto(
                lvl.LevelsGained, lvl.NewLevel, lvl.SkillPointsGranted, lvl.MetersRefilled,
                PayoutBonusGained: Tidy(LevelPayout.BonusFor(lvl.NewLevel, catalog.LevelPayoutBonusPerLevel)
                    - LevelPayout.BonusFor(lvl.NewLevel - lvl.LevelsGained, catalog.LevelPayoutBonusPerLevel)),
                PayoutBonus: Tidy(LevelPayout.BonusFor(lvl.NewLevel, catalog.LevelPayoutBonusPerLevel)))
            : null;
}
