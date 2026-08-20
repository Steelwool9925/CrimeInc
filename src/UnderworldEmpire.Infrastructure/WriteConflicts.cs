using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace UnderworldEmpire.Infrastructure;

/// <summary>
/// Recognises the write failures that mean "somebody else got there first" rather than "something is
/// broken". Shared by every action service, because they all answer such a failure the same way: report
/// a conflict and let the client re-read.
/// </summary>
/// <remarks>
/// Extracted when a third action service (<see cref="PropertyActionService"/>) needed the same test that
/// <see cref="JobActionService"/> and <see cref="InventoryActionService"/> already carried a copy of
/// each. Three copies of a rule about which faults are safe to swallow is three chances to widen one of
/// them by accident.
/// </remarks>
internal static class WriteConflicts
{
    /// <summary>
    /// Whether <paramref name="ex"/> wraps a PostgreSQL unique-constraint violation (SQLSTATE 23505).
    /// </summary>
    /// <remarks>
    /// This is the shape a lost race takes when two concurrent requests each try to INSERT the same
    /// composite-keyed holding row: the loser hits the primary key before any concurrency token can
    /// matter.
    /// <para>
    /// Narrowed to the specific SQLSTATE rather than catching <see cref="DbUpdateException"/> wholesale,
    /// which would swallow genuine faults — a broken foreign key or a not-null violation would be
    /// silently reported to the player as "try again" and never surface in logs as a bug.
    /// </para>
    /// </remarks>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
