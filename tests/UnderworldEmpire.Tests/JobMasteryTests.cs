using UnderworldEmpire.Domain.Jobs;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PlayerJobMastery"/> rank arithmetic (T2.1). Pure domain — no host, no
/// database.
/// </summary>
public class JobMasteryTests
{
    private const int PerRank = 10;
    private const int MaxRank = 5;

    private static PlayerJobMastery NewMastery() =>
        new() { PlayerId = Guid.NewGuid(), JobId = "mug-a-tourist" };

    private static PlayerJobMastery WithCompletions(int completions)
    {
        var mastery = NewMastery();
        for (int i = 0; i < completions; i++)
            mastery.RecordCompletion(PerRank, MaxRank);
        return mastery;
    }

    // -------------------------------------------------------------------------
    // Fresh state
    // -------------------------------------------------------------------------

    [Fact]
    public void New_mastery_starts_at_zero_completions_and_rank_zero()
    {
        var mastery = NewMastery();

        Assert.Equal(0, mastery.Completions);
        Assert.Equal(0, mastery.Rank);
    }

    // -------------------------------------------------------------------------
    // RecordCompletion
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCompletion_increments_completions()
    {
        var mastery = NewMastery();

        mastery.RecordCompletion(PerRank, MaxRank);

        Assert.Equal(1, mastery.Completions);
    }

    [Fact]
    public void RecordCompletion_below_the_threshold_does_not_rank_up()
    {
        var mastery = NewMastery();

        bool rankedUp = mastery.RecordCompletion(PerRank, MaxRank);

        Assert.False(rankedUp);
        Assert.Equal(0, mastery.Rank);
    }

    [Fact]
    public void RecordCompletion_at_the_threshold_ranks_up_exactly_once()
    {
        var mastery = WithCompletions(PerRank - 1);

        bool rankedUp = mastery.RecordCompletion(PerRank, MaxRank);

        Assert.True(rankedUp);
        Assert.Equal(1, mastery.Rank);
        Assert.Equal(PerRank, mastery.Completions);
    }

    [Fact]
    public void RecordCompletion_one_past_the_threshold_does_not_rank_up_again()
    {
        var mastery = WithCompletions(PerRank);

        bool rankedUp = mastery.RecordCompletion(PerRank, MaxRank);

        Assert.False(rankedUp);
        Assert.Equal(1, mastery.Rank);
    }

    [Fact]
    public void Rank_is_capped_at_maxRank_however_many_completions_accrue()
    {
        var mastery = WithCompletions(PerRank * (MaxRank + 3));

        Assert.Equal(MaxRank, mastery.Rank);
    }

    [Fact]
    public void RecordCompletion_past_maxRank_reports_no_rank_up_but_still_counts()
    {
        var mastery = WithCompletions(PerRank * MaxRank);
        int completionsBefore = mastery.Completions;

        bool rankedUp = mastery.RecordCompletion(PerRank, MaxRank);

        Assert.False(rankedUp);
        Assert.Equal(MaxRank, mastery.Rank);
        Assert.Equal(completionsBefore + 1, mastery.Completions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RecordCompletion_rejects_a_non_positive_completionsPerRank(int perRank)
    {
        // A zero divisor would throw DivideByZeroException deep inside the rank calculation; the
        // guard turns it into an argument error at the boundary.
        var mastery = NewMastery();

        Assert.Throws<ArgumentOutOfRangeException>(() => mastery.RecordCompletion(perRank, MaxRank));
    }

    [Fact]
    public void RecordCompletion_rejects_a_negative_maxRank()
    {
        var mastery = NewMastery();

        Assert.Throws<ArgumentOutOfRangeException>(() => mastery.RecordCompletion(PerRank, -1));
    }

    // -------------------------------------------------------------------------
    // RankFor
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(50, 5)]
    [InlineData(999, 5)]
    public void RankFor_floors_to_whole_ranks_and_caps_at_maxRank(int completions, int expectedRank)
    {
        Assert.Equal(expectedRank, PlayerJobMastery.RankFor(completions, PerRank, MaxRank));
    }

    [Fact]
    public void RankFor_returns_zero_when_maxRank_is_zero()
    {
        // maxRank 0 is legal content and disables mastery entirely.
        Assert.Equal(0, PlayerJobMastery.RankFor(1000, PerRank, maxRank: 0));
    }

    // -------------------------------------------------------------------------
    // CompletionsUntilNextRank
    // -------------------------------------------------------------------------

    [Fact]
    public void CompletionsUntilNextRank_counts_down_within_a_rank()
    {
        var mastery = WithCompletions(3);

        Assert.Equal(PerRank - 3, mastery.CompletionsUntilNextRank(PerRank, MaxRank));
    }

    [Fact]
    public void CompletionsUntilNextRank_resets_to_the_full_threshold_after_a_rank_up()
    {
        var mastery = WithCompletions(PerRank);

        Assert.Equal(PerRank, mastery.CompletionsUntilNextRank(PerRank, MaxRank));
    }

    [Fact]
    public void CompletionsUntilNextRank_is_null_at_maxRank()
    {
        var mastery = WithCompletions(PerRank * MaxRank);

        Assert.Null(mastery.CompletionsUntilNextRank(PerRank, MaxRank));
    }

    [Fact]
    public void CompletionsUntilNextRank_is_null_when_mastery_is_disabled()
    {
        Assert.Null(NewMastery().CompletionsUntilNextRank(PerRank, maxRank: 0));
    }
}
