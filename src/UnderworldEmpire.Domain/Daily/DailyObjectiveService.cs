using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UnderworldEmpire.Domain.Daily;

/// <summary>
/// Pure rules for daily objectives. Holds no state and reads no content: the pool is passed in.
/// </summary>
/// <remarks>
/// <b>A day's set is fixed by the player's first action.</b> Before that, the offered set is computed
/// from <c>(player, UTC date, level)</c> and nothing is stored, so reading it writes nothing and cannot
/// be rerolled. Nothing but an action can change a player's level, so that computed set cannot move
/// before the first action. The first applied action therefore stores one zero-progress row per offered
/// objective (<see cref="Lock"/>); from then on the day's set is those rows, and a level-up cannot
/// change it.
/// </remarks>
public sealed class DailyObjectiveService
{
    /// <summary>How many objectives a player is offered per UTC day.</summary>
    public const int ObjectivesPerDay = 3;

    /// <summary>
    /// Picks the objectives offered to a player on a day. A pure function of its arguments, so the same
    /// call twice returns the same set, a player cannot reroll, and reading it writes nothing.
    /// </summary>
    /// <remarks>
    /// Each eligible objective is ranked by a SHA-256 of <c>(playerId, utcDate, objectiveId)</c> and the
    /// lowest ranks are taken: selection without replacement, so no objective repeats within a day. A
    /// cryptographic hash rather than <see cref="string.GetHashCode()"/> (randomised per process) or a
    /// modulo of a date (which hands the same player the same three on a short cycle).
    /// <para>
    /// Eligibility is the player's level, so a level-1 player is never handed a job they cannot attempt.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Objective> SelectFor(
        IReadOnlyList<Objective> pool, Guid playerId, DateOnly utcDate, int playerLevel, int count = ObjectivesPerDay)
    {
        var day = utcDate.ToString("yyyy-MM-dd");
        return pool
            .Where(o => o.MinLevel <= playerLevel)
            .Select(o => (Objective: o, Rank: Rank(playerId, day, o.Id)))
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Objective.Id, StringComparer.Ordinal)
            .Take(count)
            .Select(x => x.Objective)
            .ToList();
    }

    /// <summary>
    /// Fixes the day's set by creating a zero-progress row for each offered objective, if the player has
    /// none yet today. A no-op once any row exists.
    /// </summary>
    /// <param name="offered">The set computed at the level the player held <i>before</i> the action.</param>
    /// <param name="rows">Today's existing rows by objective id; new rows are added to it.</param>
    /// <returns>The rows created by this call, for the caller to add to its unit of work.</returns>
    public IReadOnlyList<PlayerDailyObjective> Lock(
        IReadOnlyList<Objective> offered,
        IDictionary<string, PlayerDailyObjective> rows,
        Guid playerId,
        DateOnly utcDate)
    {
        var created = new List<PlayerDailyObjective>();
        if (rows.Count > 0)
            return created;

        foreach (var objective in offered)
        {
            var row = new PlayerDailyObjective { PlayerId = playerId, UtcDate = utcDate, ObjectiveId = objective.Id };
            rows[objective.Id] = row;
            created.Add(row);
        }

        return created;
    }

    /// <summary>
    /// Counts <paramref name="e"/> toward every objective in today's stored set that it advances.
    /// </summary>
    /// <param name="pool">The full objective pool, used to resolve each stored row.</param>
    /// <param name="rows">Today's rows by objective id.</param>
    public void Credit(
        IReadOnlyList<Objective> pool, IReadOnlyDictionary<string, PlayerDailyObjective> rows, ObjectiveEvent e)
    {
        foreach (var objective in pool)
        {
            if (rows.TryGetValue(objective.Id, out var row))
                row.Apply(objective, e);
        }
    }

    /// <summary>
    /// What the player sees today: their stored set once an action has fixed it, otherwise the computed
    /// offer with no progress. Always in pool order, which is stable.
    /// </summary>
    /// <param name="pool">The full objective pool.</param>
    /// <param name="offered">The computed offer, shown only while no row exists.</param>
    /// <param name="rows">Today's stored rows by objective id.</param>
    public IReadOnlyList<ObjectiveState> View(
        IReadOnlyList<Objective> pool,
        IReadOnlyList<Objective> offered,
        IReadOnlyDictionary<string, PlayerDailyObjective> rows)
    {
        // Pool order before and after the first action, so the list never reshuffles when it is fixed.
        if (rows.Count == 0)
        {
            var offeredIds = offered.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            return pool
                .Where(o => offeredIds.Contains(o.Id))
                .Select(o => new ObjectiveState(o, 0, false))
                .ToList();
        }

        // An objective removed from content since the row was written simply drops out.
        return pool
            .Where(o => rows.ContainsKey(o.Id))
            .Select(o => new ObjectiveState(o, rows[o.Id].Progress, rows[o.Id].Claimed))
            .ToList();
    }

    private static ulong Rank(Guid playerId, string day, string objectiveId)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{playerId:N}|{day}|{objectiveId}"), hash);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}

/// <summary>An objective together with the player's progress on it today.</summary>
/// <param name="Objective">The objective.</param>
/// <param name="Progress">Progress so far, never above the target.</param>
/// <param name="Claimed">Whether the reward has been taken.</param>
public readonly record struct ObjectiveState(Objective Objective, long Progress, bool Claimed);
