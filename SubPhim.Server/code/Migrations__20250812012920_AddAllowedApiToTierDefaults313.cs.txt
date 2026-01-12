using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedApiToTierDefaults313 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastTtsResetUtc",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "TtsCharacterLimit",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TtsCharactersUsed",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AllowedApiAccess",
                table: "TierDefaultSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TtsCharacterLimit",
                table: "TierDefaultSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTtsResetUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TtsCharacterLimit",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TtsCharactersUsed",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AllowedApiAccess",
                table: "TierDefaultSettings");

            migrationBuilder.DropColumn(
                name: "TtsCharacterLimit",
                table: "TierDefaultSettings");
        }
    }
}
