namespace UnderworldEmpire.Domain.Timed;

/// <summary>
/// Why a start or collect request was refused, or <see cref="None"/> when it succeeded.
/// </summary>
/// <remarks>
/// The names map onto the token vocabulary <c>POST /timed-jobs/{id}/start</c> and
/// <c>POST /timed-jobs/collect</c> report, the same style <see cref="Jobs.JobFailure"/> uses for
/// instant jobs.
/// </remarks>
public enum TimedJobFailure
{
    /// <summary>The request succeeded.</summary>
    None = 0,

    /// <summary>
    /// A start was requested while a slot is already occupied. Reported as
    /// <c>"alreadyRunning"</c>. Checked first — the most informative "why can't I start anything"
    /// reason, since Tier 1 has exactly one slot.
    /// </summary>
    AlreadyRunning,

    /// <summary>The player's level is below the appointment's requirement. Reported as <c>"level"</c>.</summary>
    LevelTooLow,

    /// <summary>The player has less energy than the appointment costs. Reported as <c>"energy"</c>.</summary>
    InsufficientEnergy,

    /// <summary>
    /// A collect was requested before the running appointment's duration has elapsed, or with
    /// nothing running at all. Reported as <c>"notReady"</c>.
    /// </summary>
    NotReady
}
