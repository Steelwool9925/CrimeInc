using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerItems",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Equipped = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerItems", x => new { x.PlayerId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_PlayerItems_Players_PlayerId",
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
                name: "PlayerItems");
        }
    }
}
