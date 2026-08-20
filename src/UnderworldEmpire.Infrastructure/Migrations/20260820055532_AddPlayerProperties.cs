using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerProperties",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UncollectedCash = table.Column<long>(type: "bigint", nullable: false),
                    AccruedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerProperties", x => new { x.PlayerId, x.PropertyId });
                    table.ForeignKey(
                        name: "FK_PlayerProperties_Players_PlayerId",
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
                name: "PlayerProperties");
        }
    }
}
