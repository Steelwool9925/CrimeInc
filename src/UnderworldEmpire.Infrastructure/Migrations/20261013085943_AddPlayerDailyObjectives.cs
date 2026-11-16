using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDailyObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerDailyObjectives",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtcDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ObjectiveId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Progress = table.Column<long>(type: "bigint", nullable: false),
                    Claimed = table.Column<bool>(type: "boolean", nullable: false),
                    JobsWon = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDailyObjectives", x => new { x.PlayerId, x.UtcDate, x.ObjectiveId });
                    table.ForeignKey(
                        name: "FK_PlayerDailyObjectives_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerDailyObjectives");
        }
    }
}
