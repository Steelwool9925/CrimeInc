using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only migration: takes off any item the player is flagged as wearing but no longer holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it is needed.</b> <c>PlayerItem.Consume</c> used to leave a row flagged as worn when it
    /// spent the last copy. Once <c>hijack-a-delivery-truck</c> needed only one switchblade, the
    /// ordinary path (wear it for the stat gate, then run the job) produced exactly that row: zero
    /// copies, still worn, still adding its attack and defense to the loadout. The code now clears the
    /// flag on consumption and the loadout ignores unowned rows, so such a row can no longer score; this
    /// migration makes the stored data agree, so nothing reading the column directly sees a worn item
    /// the player does not have.
    /// </para>
    /// <para>
    /// <b>Narrow by construction.</b> Only rows with no copies are touched, and the flag is the only
    /// thing changed; quantities and every other row are left alone. It is idempotent: after it runs
    /// no row matches the predicate.
    /// </para>
    /// </remarks>
    public partial class ClearWornFlagOnEmptyHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "PlayerItems"
                SET "Equipped" = FALSE
                WHERE "Quantity" = 0 AND "Equipped" = TRUE;
                """);
        }

        /// <summary>
        /// Deliberately a no-op.
        /// </summary>
        /// <remarks>
        /// A worn item with no copies is the defect this migration repairs, not a state worth restoring,
        /// and nothing records which rows it changed. Rolling back leaves them unworn, which is correct.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
