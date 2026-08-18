namespace UnderworldEmpire.Domain.Progression;

/// <summary>
/// What crossing one or more XP thresholds produced.
/// </summary>
/// <param name="LevelsGained">How many levels were gained; 0 when no threshold was crossed.</param>
/// <param name="NewLevel">The player's level afterwards.</param>
/// <param name="SkillPointsGranted">Skill points awarded for the levels gained.</param>
/// <param name="MetersRefilled">
///     Whether all three meters were refilled. True whenever at least one level was gained — the
///     refill is the level-up reward that lets a player keep playing instead of waiting on regen.
/// </param>
public record LevelUpOutcome(int LevelsGained, int NewLevel, int SkillPointsGranted, bool MetersRefilled)
{
    /// <summary>Whether anything actually happened.</summary>
    public bool LeveledUp => LevelsGained > 0;

    /// <summary>A no-op outcome for the common case where the XP grant crossed no threshold.</summary>
    public static LevelUpOutcome None(int currentLevel) => new(0, currentLevel, 0, false);
}
