namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// How far one player has progressed on one job. Persisted per (player, job) pair — the first
/// entity in the schema besides <see cref="UnderworldEmpire.Domain.Player"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mastery thresholds are content-supplied (<c>jobs.json</c> → <c>mastery</c>), so they are passed
/// in on every call rather than stored here. Storing them on the row would freeze a balance value
/// into player data and make retuning a migration.
/// </para>
/// <para>
/// <see cref="Rank"/> is stored rather than derived on read so the value the player was shown
/// cannot silently change under them when the thresholds are retuned. The trade-off is that a
/// retune does not retroactively re-rank existing rows.
/// </para>
/// </remarks>
public class PlayerJobMastery
{
    /// <summary>Owning player. Half of the composite primary key.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Content id of the job. Half of the composite primary key.</summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>Total successful completions of this job by this player.</summary>
    public int Completions { get; private set; }

    /// <summary>Current mastery rank, starting at 0 and capped by content.</summary>
    public int Rank { get; private set; }

    /// <summary>
    /// Records one successful completion and re-derives <see cref="Rank"/>.
    /// </summary>
    /// <param name="completionsPerRank">Completions required per rank; must be &gt;= 1.</param>
    /// <param name="maxRank">Rank ceiling; must be &gt;= 0.</param>
    /// <returns><see langword="true"/> when this completion pushed the player into a new rank.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a threshold argument is invalid.</exception>
    public bool RecordCompletion(int completionsPerRank, int maxRank)
    {
        if (completionsPerRank < 1)
            throw new ArgumentOutOfRangeException(nameof(completionsPerRank), completionsPerRank,
                "Completions per rank must be >= 1.");
        if (maxRank < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRank), maxRank, "Max rank must be >= 0.");

        int rankBefore = Rank;
        Completions++;
        Rank = RankFor(Completions, completionsPerRank, maxRank);
        return Rank > rankBefore;
    }

    /// <summary>
    /// The rank a given completion count earns, capped at <paramref name="maxRank"/>. Pure — exposed
    /// so read paths can present a consistent figure without mutating anything.
    /// </summary>
    public static int RankFor(int completions, int completionsPerRank, int maxRank)
    {
        if (completionsPerRank < 1)
            throw new ArgumentOutOfRangeException(nameof(completionsPerRank), completionsPerRank,
                "Completions per rank must be >= 1.");

        return Math.Clamp(completions / completionsPerRank, 0, maxRank);
    }

    /// <summary>
    /// Completions still needed for the next rank, or <see langword="null"/> once
    /// <paramref name="maxRank"/> is reached. Drives the client's mastery bar.
    /// </summary>
    public int? CompletionsUntilNextRank(int completionsPerRank, int maxRank)
    {
        if (completionsPerRank < 1)
            throw new ArgumentOutOfRangeException(nameof(completionsPerRank), completionsPerRank,
                "Completions per rank must be >= 1.");

        if (Rank >= maxRank)
            return null;

        int completionsForNextRank = (Rank + 1) * completionsPerRank;
        return Math.Max(0, completionsForNextRank - Completions);
    }
}
