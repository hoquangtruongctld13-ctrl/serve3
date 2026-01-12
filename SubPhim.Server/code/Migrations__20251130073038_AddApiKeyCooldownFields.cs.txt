using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyCooldownFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Consecutive429Count",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DisabledReason",
                table: "ManagedApiKeys",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemporaryCooldownUntil",
                table: "ManagedApiKeys",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Consecutive429Count",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "DisabledReason",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "TemporaryCooldownUntil",
                table: "ManagedApiKeys");
        }
    }
}
