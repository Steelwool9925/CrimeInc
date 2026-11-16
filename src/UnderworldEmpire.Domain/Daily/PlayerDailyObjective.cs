namespace UnderworldEmpire.Domain.Daily;

/// <summary>What an action accomplished, as far as objectives are concerned.</summary>
public enum ObjectiveEventKind
{
    /// <summary>A job attempt that came off. A failed attempt reports nothing: it advances no objective.</summary>
    JobWon,

    /// <summary>A property collection that paid out.</summary>
    PropertyCollected
}

/// <summary>
/// One fact an action reports so objectives can count it. The services that perform actions build
/// these from what they already decided; nothing here reaches back into objective state.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="JobId">The job won, for <see cref="ObjectiveEventKind.JobWon"/>.</param>
/// <param name="Cash">Cash the job paid, for <see cref="ObjectiveEventKind.JobWon"/>.</param>
public readonly record struct ObjectiveEvent(ObjectiveEventKind Kind, string? JobId = null, long Cash = 0)
{
    /// <summary>A job attempt that succeeded.</summary>
    public static ObjectiveEvent JobWon(string jobId, long cash) => new(ObjectiveEventKind.JobWon, jobId, cash);

    /// <summary>A property collection that paid out.</summary>
    public static ObjectiveEvent PropertyCollected() => new(ObjectiveEventKind.PropertyCollected);
}

/// <summary>
/// How far one player has got on one objective on one UTC day. Persisted per
/// (player, date, objective). A player has no rows until their first action of the day; that action stores
/// one per offered objective, which both fixes the day's set and lets <c>GET /objectives</c> be a pure
/// read until then.
/// </summary>
/// <remarks>
/// Only counters are stored; the set is the rows themselves once they exist (see
/// <see cref="DailyObjectiveService"/>).
/// </remarks>
public class PlayerDailyObjective
{
    /// <summary>Owning player. Part of the composite key.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>The UTC day this progress belongs to. Part of the composite key.</summary>
    public DateOnly UtcDate { get; init; }

    /// <summary>Content id of the objective. Part of the composite key.</summary>
    public string ObjectiveId { get; init; } = string.Empty;

    /// <summary>Progress so far, never above the objective target.</summary>
    public long Progress { get; private set; }

    /// <summary>Whether the reward has been taken. Once true, never false again.</summary>
    public bool Claimed { get; private set; }

    /// <summary>
    /// The distinct jobs won so far, comma-separated. Used only by
    /// <see cref="ObjectiveType.CompleteDistinctJobs"/>, where progress is the size of this set.
    /// </summary>
    public string JobsWon { get; private set; } = string.Empty;

    /// <summary>Whether <paramref name="objective"/>'s target has been reached.</summary>
    public bool IsComplete(Objective objective) => Progress >= objective.Target;

    /// <summary>Counts <paramref name="e"/> toward <paramref name="objective"/>, if it is relevant.</summary>
    /// <returns><see langword="true"/> when progress changed.</returns>
    public bool Apply(Objective objective, ObjectiveEvent e)
    {
        // A claimed or finished objective stops counting: progress is capped at the target so the
        // bar never reads past full.
        if (Claimed || IsComplete(objective))
            return false;

        long before = Progress;
        switch (objective.Type)
        {
            case ObjectiveType.CompleteDistinctJobs when e.Kind == ObjectiveEventKind.JobWon && e.JobId is not null:
                var won = JobsWon.Length == 0 ? [] : JobsWon.Split(',').ToList();
                if (!won.Contains(e.JobId, StringComparer.Ordinal))
                {
                    won.Add(e.JobId);
                    JobsWon = string.Join(',', won);
                    Progress = won.Count;
                }
                break;

            case ObjectiveType.CompleteJobTimes when e.Kind == ObjectiveEventKind.JobWon:
                if (objective.JobId is null || string.Equals(objective.JobId, e.JobId, StringComparison.Ordinal))
                    Progress++;
                break;

            case ObjectiveType.EarnCash when e.Kind == ObjectiveEventKind.JobWon:
                Progress += Math.Max(0, e.Cash);
                break;

            case ObjectiveType.CollectProperties when e.Kind == ObjectiveEventKind.PropertyCollected:
                Progress++;
                break;
        }

        Progress = Math.Min(Progress, objective.Target);
        return Progress != before;
    }

    /// <summary>Takes the reward: refuses unless the target is reached and it has not been taken.</summary>
    public ObjectiveFailure Claim(Objective objective)
    {
        if (Claimed) return ObjectiveFailure.AlreadyClaimed;
        if (!IsComplete(objective)) return ObjectiveFailure.NotComplete;

        Claimed = true;
        return ObjectiveFailure.None;
    }
}
