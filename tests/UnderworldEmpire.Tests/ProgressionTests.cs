using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="LevelCurve"/> and the progression methods on <see cref="Player"/>
/// (T2.3). Pure domain — no host, no database.
/// </summary>
public class ProgressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static Player TestPlayer(int level = 1, long xp = 0, int skillPoints = 0) => new()
    {
        CreatedAtUtc = Now,
        Level = level,
        Xp = xp,
        SkillPoints = skillPoints
    };

    // -------------------------------------------------------------------------
    // LevelCurve.XpToReach
    // -------------------------------------------------------------------------

    [Fact]
    public void Level_1_requires_no_xp()
    {
        Assert.Equal(0, LevelCurve.XpToReach(1));
    }

    [Fact]
    public void Level_2_requires_exactly_100_xp()
    {
        // Load-bearing: level 2 must be reachable in a new player's first sitting, because a
        // first session with no level-up fires none of the game's reward vocabulary.
        //
        // The threshold is NOT "one bar of the cheapest job awards exactly 100 XP" — that was true
        // only before jobs could fail, and this comment asserted it long after it stopped being
        // so. The pack now clears it with headroom instead: a 20-energy bar of the starter job at
        // 0.80 success expects 164 XP. See LevelCurve's remarks and FirstSessionTests, which pins
        // the reachability property itself rather than this one number.
        Assert.Equal(100, LevelCurve.XpToReach(2));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(3, 300)]
    [InlineData(4, 600)]
    [InlineData(5, 1000)]
    [InlineData(6, 1500)]
    public void XpToReach_follows_the_documented_curve(int level, long expected)
    {
        Assert.Equal(expected, LevelCurve.XpToReach(level));
    }

    [Fact]
    public void XpToReach_is_strictly_increasing()
    {
        for (int level = 2; level <= PlayerDefaults.MaxLevel; level++)
            Assert.True(LevelCurve.XpToReach(level) > LevelCurve.XpToReach(level - 1),
                $"Curve must increase at level {level}.");
    }

    [Fact]
    public void XpToReach_rejects_a_level_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelCurve.XpToReach(0));
    }

    // -------------------------------------------------------------------------
    // LevelCurve.LevelForXp
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(101, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    [InlineData(1000, 5)]
    public void LevelForXp_returns_the_level_the_total_earns(long xp, int expected)
    {
        Assert.Equal(expected, LevelCurve.LevelForXp(xp));
    }

    [Fact]
    public void LevelForXp_treats_negative_xp_as_level_one()
    {
        Assert.Equal(1, LevelCurve.LevelForXp(-500));
    }

    [Fact]
    public void LevelForXp_is_capped_at_MaxLevel()
    {
        Assert.Equal(PlayerDefaults.MaxLevel, LevelCurve.LevelForXp(long.MaxValue / 4));
    }

    [Fact]
    public void LevelForXp_agrees_with_XpToReach_at_every_boundary()
    {
        // The sqrt estimate plus correction must land exactly on each threshold — an off-by-one here
        // would grant or withhold a real level.
        for (int level = 1; level < PlayerDefaults.MaxLevel; level++)
        {
            long threshold = LevelCurve.XpToReach(level);
            Assert.Equal(level, LevelCurve.LevelForXp(threshold));

            if (level > 1)
                Assert.Equal(level - 1, LevelCurve.LevelForXp(threshold - 1));
        }
    }

    // -------------------------------------------------------------------------
    // LevelCurve.XpUntilNextLevel
    // -------------------------------------------------------------------------

    [Fact]
    public void XpUntilNextLevel_counts_down_toward_the_threshold()
    {
        Assert.Equal(100, LevelCurve.XpUntilNextLevel(0));
        Assert.Equal(40, LevelCurve.XpUntilNextLevel(60));
        Assert.Equal(200, LevelCurve.XpUntilNextLevel(100));
    }

    [Fact]
    public void XpUntilNextLevel_is_null_at_MaxLevel()
    {
        Assert.Null(LevelCurve.XpUntilNextLevel(LevelCurve.XpToReach(PlayerDefaults.MaxLevel)));
    }

    // -------------------------------------------------------------------------
    // Player.AddXp
    // -------------------------------------------------------------------------

    [Fact]
    public void AddXp_accumulates_without_levelling()
    {
        var player = TestPlayer(xp: 0);

        player.AddXp(500);

        Assert.Equal(500, player.Xp);
        Assert.Equal(1, player.Level); // raw accumulator does not apply the curve
    }

    [Fact]
    public void AddXp_rejects_a_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPlayer().AddXp(-1));
    }

    // -------------------------------------------------------------------------
    // Player.ApplyXpAndLevelUp
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyXp_below_the_threshold_does_not_level_up()
    {
        var player = TestPlayer(xp: 0);

        var outcome = player.ApplyXpAndLevelUp(99, Now);

        Assert.False(outcome.LeveledUp);
        Assert.Equal(0, outcome.LevelsGained);
        Assert.Equal(1, player.Level);
        Assert.Equal(0, player.SkillPoints);
    }

    [Fact]
    public void ApplyXp_at_the_threshold_levels_up_and_grants_a_skill_point()
    {
        var player = TestPlayer(xp: 0);

        var outcome = player.ApplyXpAndLevelUp(100, Now);

        Assert.True(outcome.LeveledUp);
        Assert.Equal(1, outcome.LevelsGained);
        Assert.Equal(2, outcome.NewLevel);
        Assert.Equal(2, player.Level);
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, outcome.SkillPointsGranted);
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, player.SkillPoints);
    }

    [Fact]
    public void ApplyXp_resolves_multiple_levels_from_one_grant()
    {
        // A single large payout crossing several thresholds must award every level and every point
        // in one pass — the Tier 2 closed-form requirement.
        var player = TestPlayer(xp: 0);

        var outcome = player.ApplyXpAndLevelUp(1000, Now);

        Assert.Equal(4, outcome.LevelsGained);       // level 1 -> 5
        Assert.Equal(5, outcome.NewLevel);
        Assert.Equal(5, player.Level);
        Assert.Equal(4 * PlayerDefaults.SkillPointsPerLevel, player.SkillPoints);
    }

    [Fact]
    public void Levelling_up_refills_all_three_meters()
    {
        var player = TestPlayer(xp: 0);
        player.Energy = new Meter(PlayerDefaults.EnergyMax, 0, Now);
        player.Stamina = new Meter(PlayerDefaults.StaminaMax, 0, Now);
        player.Health = new Meter(PlayerDefaults.HealthMax, 1, Now);

        var outcome = player.ApplyXpAndLevelUp(100, Now);

        Assert.True(outcome.MetersRefilled);
        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(PlayerDefaults.StaminaMax, player.Stamina.CurrentAt(Now, MeterRates.Stamina));
        Assert.Equal(PlayerDefaults.HealthMax, player.Health.CurrentAt(Now, MeterRates.Health));
    }

    [Fact]
    public void Not_levelling_up_leaves_meters_alone()
    {
        var player = TestPlayer(xp: 0);
        player.Energy = new Meter(PlayerDefaults.EnergyMax, 3, Now);

        var outcome = player.ApplyXpAndLevelUp(50, Now);

        Assert.False(outcome.MetersRefilled);
        Assert.Equal(3, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    [Fact]
    public void ApplyXp_never_demotes_a_player_whose_level_is_ahead_of_the_curve()
    {
        // Hand-edited data or a curve retune that lowered thresholds must not cost anyone a level.
        var player = TestPlayer(level: 10, xp: 0);

        var outcome = player.ApplyXpAndLevelUp(100, Now);

        Assert.False(outcome.LeveledUp);
        Assert.Equal(10, player.Level);
    }

    [Fact]
    public void ApplyXp_rejects_a_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPlayer().ApplyXpAndLevelUp(-1, Now));
    }

    // -------------------------------------------------------------------------
    // Skill points
    // -------------------------------------------------------------------------

    [Fact]
    public void GrantSkillPoints_adds_to_the_unspent_pool()
    {
        var player = TestPlayer(skillPoints: 2);

        player.GrantSkillPoints(3);

        Assert.Equal(5, player.SkillPoints);
    }

    [Fact]
    public void GrantSkillPoints_rejects_a_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPlayer().GrantSkillPoints(-1));
    }

    [Fact]
    public void SpendSkillPoint_on_attack_raises_attack_and_consumes_the_point()
    {
        var player = TestPlayer(skillPoints: 1);
        int before = player.Attack;

        player.SpendSkillPoint(SkillTarget.Attack);

        Assert.Equal(before + PlayerDefaults.StatIncreasePerSkillPoint, player.Attack);
        Assert.Equal(0, player.SkillPoints);
    }

    [Fact]
    public void SpendSkillPoint_on_defense_raises_defense_only()
    {
        var player = TestPlayer(skillPoints: 1);
        int attackBefore = player.Attack;
        int defenseBefore = player.Defense;

        player.SpendSkillPoint(SkillTarget.Defense);

        Assert.Equal(defenseBefore + PlayerDefaults.StatIncreasePerSkillPoint, player.Defense);
        Assert.Equal(attackBefore, player.Attack);
    }

    [Fact]
    public void SpendSkillPoint_with_no_points_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestPlayer(skillPoints: 0).SpendSkillPoint(SkillTarget.Attack));

        Assert.Contains("no skill points", ex.Message);
    }

    [Fact]
    public void SpendSkillPoint_with_an_undefined_target_does_not_burn_a_point()
    {
        // The point is decremented only after the target is known good.
        var player = TestPlayer(skillPoints: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => player.SpendSkillPoint((SkillTarget)999));
        Assert.Equal(1, player.SkillPoints);
    }

    [Fact]
    public void Spending_every_point_leaves_the_pool_empty_and_the_next_spend_throws()
    {
        var player = TestPlayer(skillPoints: 2);

        player.SpendSkillPoint(SkillTarget.Attack);
        player.SpendSkillPoint(SkillTarget.Defense);

        Assert.Equal(0, player.SkillPoints);
        Assert.Throws<InvalidOperationException>(() => player.SpendSkillPoint(SkillTarget.Attack));
    }
}
