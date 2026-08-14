using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnderworldEmpire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerMeters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EnergyMax",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnergyStoredAtUtc",
                table: "Players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "EnergyStoredValue",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HealthMax",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HealthStoredAtUtc",
                table: "Players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "HealthStoredValue",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StaminaMax",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StaminaStoredAtUtc",
                table: "Players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "StaminaStoredValue",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnergyMax",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EnergyStoredAtUtc",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EnergyStoredValue",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HealthMax",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HealthStoredAtUtc",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HealthStoredValue",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "StaminaMax",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "StaminaStoredAtUtc",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "StaminaStoredValue",
                table: "Players");
        }
    }
}
