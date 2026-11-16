using UnderworldEmpire.Domain.Daily;

namespace UnderworldEmpire.Tests;

/// <summary>Pure rules for counting events toward objectives, claiming, and building the player view.</summary>
public class PlayerDailyObjectiveTests
{
    private static readonly Guid Player = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateOnly Day = new(2026, 10, 13);
    private readonly DailyObjectiveService _service = new();

    private static Objective Make(string id, ObjectiveType type, long target, string? jobId = null, int minLevel = 1) => new()
    {
        Id = id, Name = id, Type = type, Target = target, JobId = jobId, MinLevel = minLevel, RewardCash = 10
    };

    private static PlayerDailyObjective Row(string id) =>
        new() { PlayerId = Player, UtcDate = Day, ObjectiveId = id };

    // ---- distinct jobs ----

    [Fact]
    public void Distinct_jobs_counts_each_job_once()
    {
        var o = Make("d", ObjectiveType.CompleteDistinctJobs, 3);
        var row = Row("d");

        row.Apply(o, ObjectiveEvent.JobWon("a", 10));
        row.Apply(o, ObjectiveEvent.JobWon("a", 10));
        row.Apply(o, ObjectiveEvent.JobWon("b", 10));

        Assert.Equal(2, row.Progress);
    }

    [Fact]
    public void Distinct_jobs_completes_at_the_target()
    {
        var o = Make("d", ObjectiveType.CompleteDistinctJobs, 2);
        var row = Row("d");

        row.Apply(o, ObjectiveEvent.JobWon("a", 10));
        Assert.False(row.IsComplete(o));
        row.Apply(o, ObjectiveEvent.JobWon("b", 10));

        Assert.True(row.IsComplete(o));
    }

    // ---- job times ----

    [Fact]
    public void Job_times_with_no_job_counts_any_win()
    {
        var o = Make("t", ObjectiveType.CompleteJobTimes, 3);
        var row = Row("t");

        row.Apply(o, ObjectiveEvent.JobWon("a", 1));
        row.Apply(o, ObjectiveEvent.JobWon("b", 1));

        Assert.Equal(2, row.Progress);
    }

    [Fact]
    public void Job_times_for_a_named_job_ignores_other_jobs()
    {
        var o = Make("t", ObjectiveType.CompleteJobTimes, 2, jobId: "a");
        var row = Row("t");

        Assert.False(row.Apply(o, ObjectiveEvent.JobWon("b", 1)));
        Assert.True(row.Apply(o, ObjectiveEvent.JobWon("a", 1)));

        Assert.Equal(1, row.Progress);
    }

    // ---- cash / properties ----

    [Fact]
    public void Earn_cash_adds_what_the_job_paid()
    {
        var o = Make("c", ObjectiveType.EarnCash, 100);
        var row = Row("c");

        row.Apply(o, ObjectiveEvent.JobWon("a", 40));
        row.Apply(o, ObjectiveEvent.JobWon("a", 35));

        Assert.Equal(75, row.Progress);
    }

    [Fact]
    public void Collect_properties_counts_collections_and_ignores_jobs()
    {
        var o = Make("p", ObjectiveType.CollectProperties, 2);
        var row = Row("p");

        Assert.False(row.Apply(o, ObjectiveEvent.JobWon("a", 50)));
        row.Apply(o, ObjectiveEvent.PropertyCollected());

        Assert.Equal(1, row.Progress);
    }

    [Fact]
    public void A_job_does_not_advance_a_collection_objective_and_a_collection_does_not_advance_a_job_one()
    {
        var jobs = Make("j", ObjectiveType.CompleteJobTimes, 2);
        var row = Row("j");

        Assert.False(row.Apply(jobs, ObjectiveEvent.PropertyCollected()));
        Assert.Equal(0, row.Progress);
    }

    // ---- cap and claim ----

    [Fact]
    public void Progress_never_exceeds_the_target()
    {
        var o = Make("c", ObjectiveType.EarnCash, 100);
        var row = Row("c");

        row.Apply(o, ObjectiveEvent.JobWon("a", 5_000));
        row.Apply(o, ObjectiveEvent.JobWon("a", 5_000));

        Assert.Equal(100, row.Progress);
    }

    [Fact]
    public void A_claim_before_the_target_is_refused()
    {
        var o = Make("t", ObjectiveType.CompleteJobTimes, 2);
        var row = Row("t");
        row.Apply(o, ObjectiveEvent.JobWon("a", 1));

        Assert.Equal(ObjectiveFailure.NotComplete, row.Claim(o));
        Assert.False(row.Claimed);
    }

    [Fact]
    public void A_finished_objective_claims_once()
    {
        var o = Make("t", ObjectiveType.CompleteJobTimes, 1);
        var row = Row("t");
        row.Apply(o, ObjectiveEvent.JobWon("a", 1));

        Assert.Equal(ObjectiveFailure.None, row.Claim(o));
        Assert.Equal(ObjectiveFailure.AlreadyClaimed, row.Claim(o));
        Assert.True(row.Claimed);
    }

    [Fact]
    public void A_claimed_objective_stops_counting()
    {
        var o = Make("c", ObjectiveType.EarnCash, 10);
        var row = Row("c");
        row.Apply(o, ObjectiveEvent.JobWon("a", 10));
        row.Claim(o);

        Assert.False(row.Apply(o, ObjectiveEvent.JobWon("a", 10)));
    }

    // ---- fixing the day ----

    [Fact]
    public void The_first_action_stores_a_zero_progress_row_for_every_offered_objective()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 3);
        var b = Make("b", ObjectiveType.CollectProperties, 1);
        var rows = new Dictionary<string, PlayerDailyObjective>();

        var created = _service.Lock([a, b], rows, Player, Day);

        Assert.Equal(["a", "b"], created.Select(r => r.ObjectiveId).OrderBy(x => x));
        Assert.All(created, r => { Assert.Equal(0, r.Progress); Assert.Equal(Day, r.UtcDate); });
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Locking_again_after_a_row_exists_changes_nothing()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 3);
        var b = Make("b", ObjectiveType.CollectProperties, 1);
        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([a], rows, Player, Day);

        // A later action offers a different set (the player levelled): it must not be added.
        var created = _service.Lock([b], rows, Player, Day);

        Assert.Empty(created);
        Assert.Equal(["a"], rows.Keys);
    }

    // ---- crediting ----

    [Fact]
    public void Crediting_advances_only_the_stored_objectives_the_event_matches()
    {
        var jobs = Make("jobs", ObjectiveType.CompleteJobTimes, 3);
        var props = Make("props", ObjectiveType.CollectProperties, 1);
        var elsewhere = Make("elsewhere", ObjectiveType.CompleteJobTimes, 3);
        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([jobs, props], rows, Player, Day);

        _service.Credit([jobs, props, elsewhere], rows, ObjectiveEvent.JobWon("a", 10));

        Assert.Equal(1, rows["jobs"].Progress);
        Assert.Equal(0, rows["props"].Progress);
        Assert.False(rows.ContainsKey("elsewhere"));
    }

    [Fact]
    public void Crediting_never_creates_rows()
    {
        var jobs = Make("jobs", ObjectiveType.CompleteJobTimes, 3);
        var rows = new Dictionary<string, PlayerDailyObjective>();

        _service.Credit([jobs], rows, ObjectiveEvent.JobWon("a", 10));

        Assert.Empty(rows);
    }

    // ---- the view ----

    [Fact]
    public void Before_any_action_the_view_is_the_computed_offer_with_no_progress()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 3);

        var view = _service.View([a], [a], new Dictionary<string, PlayerDailyObjective>());

        var state = Assert.Single(view);
        Assert.Equal(0, state.Progress);
        Assert.False(state.Claimed);
    }

    [Fact]
    public void The_computed_offer_is_shown_in_pool_order_so_fixing_the_day_never_reshuffles_it()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 2);
        var b = Make("b", ObjectiveType.CompleteJobTimes, 2);
        var c = Make("c", ObjectiveType.CompleteJobTimes, 2);
        var none = new Dictionary<string, PlayerDailyObjective>();

        var unlocked = _service.View([a, b, c], [c, a], none).Select(s => s.Objective.Id);

        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([c, a], rows, Player, Day);
        var locked = _service.View([a, b, c], [c, a], rows).Select(s => s.Objective.Id);

        Assert.Equal(["a", "c"], unlocked);
        Assert.Equal(unlocked, locked);
    }

    [Fact]
    public void After_an_action_the_view_is_the_stored_set_even_if_a_new_offer_would_differ()
    {
        var kept = Make("kept", ObjectiveType.CompleteJobTimes, 2);
        var other = Make("other", ObjectiveType.CompleteJobTimes, 5);
        var newer = Make("newer", ObjectiveType.CompleteJobTimes, 5);
        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([kept, other], rows, Player, Day);
        _service.Credit([kept, other, newer], rows, ObjectiveEvent.JobWon("a", 1));

        // The player has since levelled and would now be offered "newer" instead of "other".
        var view = _service.View([kept, other, newer], [kept, newer], rows);

        Assert.Equal(["kept", "other"], view.Select(s => s.Objective.Id));
        Assert.Equal(1, view[0].Progress);
    }

    [Fact]
    public void The_stored_view_follows_pool_order_not_row_order()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 2);
        var b = Make("b", ObjectiveType.CompleteJobTimes, 2);
        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([b, a], rows, Player, Day);

        Assert.Equal(["a", "b"], _service.View([a, b], [], rows).Select(s => s.Objective.Id));
    }

    [Fact]
    public void A_stored_objective_removed_from_content_drops_out_of_the_view()
    {
        var a = Make("a", ObjectiveType.CompleteJobTimes, 2);
        var gone = Make("gone", ObjectiveType.CompleteJobTimes, 2);
        var rows = new Dictionary<string, PlayerDailyObjective>();
        _service.Lock([a, gone], rows, Player, Day);

        Assert.Equal(["a"], _service.View([a], [], rows).Select(s => s.Objective.Id));
    }
}
