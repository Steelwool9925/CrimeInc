using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attack",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "Defense",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SkillPoints",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Xp",
                table: "Players",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attack",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Defense",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SkillPoints",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "Players");
        }
    }
}
