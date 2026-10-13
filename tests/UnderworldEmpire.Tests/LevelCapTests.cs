using UnderworldEmpire.Domain;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Pins the level cap as a property of the shipped content pack, not of one number.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists.</b> The 2026-10-02 core-loop audit found that <b>98.4%</b> of the XP
/// curve unlocked nothing. The cap was 100 while the pack's content ran out at level 12, so a player
/// who had seen everything the game offers faced a long wall of levels that paid a skill point each.
/// Nothing asserted the relationship between the cap and the content, so nothing noticed when the
/// two drifted apart; the cap was simply never revisited.
/// </para>
/// <para>
/// Two directions, because the cap can be wrong either way. Too <i>low</i> and content the pack
/// ships is unreachable, which is a defect. Too <i>high</i> and the audit's finding comes back.
/// Both read the <b>real</b> <c>content/crime</c> pack, so adding a content tier without moving the
/// cap (or moving the cap without content) breaks the build rather than a playtest.
/// </para>
/// <para>
/// This is deliberately <b>not</b> a claim that every level beyond the last unlock is meaningful.
/// A level with no new job, item or property still pays a skill point, and whether it should pay
/// something more is the per-level-grant question, which these tests do not answer.
/// </para>
/// </remarks>
public class LevelCapTests
{
    /// <summary>
    /// The most levels the cap may sit beyond the highest level any content requires.
    /// </summary>
    /// <remarks>
    /// "A little beyond the content, so there is headroom, not a cliff" — a few levels of room let the
    /// last unlock feel like an arrival rather than the end of the game. Ten is generous: the shipped
    /// pack sits well inside it, and the audit's cap of 100 sat 88 levels outside. When a content tier
    /// is added, move the cap with it rather than raising this.
    /// </remarks>
    private const int MaxHeadroomLevels = 10;

    /// <summary>The highest level that any job, item or property in the shipped pack requires.</summary>
    private static int HighestLevelAnyContentRequires()
    {
        var loader = new ContentPackLoader(RepoContent.FindRoot());
        var items = loader.LoadItems("crime");
        var jobs = loader.LoadJobs("crime", items);
        var properties = loader.LoadProperties("crime");

        return new[]
        {
            jobs.Jobs.Max(j => j.LevelRequirement),
            items.Items.Max(i => i.LevelRequirement),
            properties.Properties.Max(p => p.LevelRequirement),
        }.Max();
    }

    [Fact]
    public void Every_level_the_shipped_content_requires_can_be_reached()
    {
        int highest = HighestLevelAnyContentRequires();

        Assert.True(PlayerDefaults.MaxLevel >= highest,
            $"MaxLevel is {PlayerDefaults.MaxLevel} but the pack requires level {highest}: " +
            "content past the cap could never be unlocked.");
    }

    [Fact]
    public void The_cap_sits_a_few_levels_past_the_last_unlock_not_a_wall_beyond_it()
    {
        int highest = HighestLevelAnyContentRequires();
        int headroom = PlayerDefaults.MaxLevel - highest;

        Assert.True(headroom <= MaxHeadroomLevels,
            $"MaxLevel is {PlayerDefaults.MaxLevel} and the last content unlocks at level {highest}: " +
            $"{headroom} levels of curve unlock nothing, more than the {MaxHeadroomLevels} allowed. " +
            "Either add content or lower the cap.");
    }

    [Fact]
    public void The_cap_leaves_at_least_one_level_beyond_the_last_unlock()
    {
        int highest = HighestLevelAnyContentRequires();

        Assert.True(PlayerDefaults.MaxLevel > highest,
            "A cap equal to the last unlock makes the final unlock the end of the game: headroom, not a cliff.");
    }
}
