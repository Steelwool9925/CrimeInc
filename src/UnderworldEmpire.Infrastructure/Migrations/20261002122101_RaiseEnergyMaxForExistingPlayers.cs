using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only migration: widens the energy bar of accounts created before
    /// <c>PlayerDefaults.EnergyMax</c> was raised from 10 to 20.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a migration is needed at all.</b> <c>Meter.Max</c> is persisted per player (mapped to
    /// the <c>EnergyMax</c> column by <c>GameDbContext</c>'s complex-property configuration), not
    /// read from the constant at runtime. Raising the constant therefore only affects <i>newly
    /// created</i> players; every existing row would keep a 10-point bar forever.
    /// </para>
    /// <para>
    /// <b>The literals are deliberate.</b> This does <i>not</i> reference
    /// <c>PlayerDefaults.EnergyMax</c>. A migration is an immutable historical record of one
    /// specific transition, and the next balance pass that moves that constant must not retroactively
    /// change what this migration did. The old default (10) and the new one (20) are baked in on
    /// purpose. This is the project's first data migration; later ones should follow the same rule.
    /// </para>
    /// <para>
    /// <b>Hand-tuned bars are left alone.</b> The update is conditional on the column still holding
    /// the old default, so any row deliberately set to something else is untouched. That also makes
    /// <see cref="Up"/> naturally idempotent: after it runs, no row matches the predicate.
    /// </para>
    /// <para>
    /// <b>Stored value is not topped up.</b> Only the ceiling moves. A player sitting at 10/10
    /// becomes 10/20 and regenerates into the new headroom at the normal rate, rather than being
    /// handed 10 free energy. <c>Meter.CurrentAt</c> is <c>min(Max, stored + elapsed / rate)</c>, so
    /// a stored value below the new max needs no adjustment.
    /// </para>
    /// </remarks>
    public partial class RaiseEnergyMaxForExistingPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Players"
                SET "EnergyMax" = 20
                WHERE "EnergyMax" = 10;
                """);
        }

        /// <summary>
        /// Narrows the bar back to the old default.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This inverse is necessarily approximate, and that is inherent to data migrations.</b>
        /// It cannot distinguish a row this migration raised to 20 from a row created at 20
        /// afterwards, because nothing records which was which. Reverting therefore narrows both.
        /// That is acceptable here only because the column is a capacity ceiling rather than player
        /// data: nothing is destroyed, and the bar refills at the normal rate.
        /// </para>
        /// <para>
        /// The stored value <i>is</i> clamped on the way down, so no row is left holding more energy
        /// than its own ceiling. <c>Meter.CurrentAt</c> would mask such a row by taking the minimum,
        /// but persisting a value above its max would still be an inconsistent state for anything
        /// reading the column directly.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Players"
                SET "EnergyMax" = 10,
                    "EnergyStoredValue" = LEAST("EnergyStoredValue", 10)
                WHERE "EnergyMax" = 20;
                """);
        }
    }
}
