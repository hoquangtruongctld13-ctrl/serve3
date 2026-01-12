using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields5434645 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabledReason",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "GeminiModel",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerDay",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerMinute",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "TtsModelName",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "TtsProvider",
                table: "ManagedApiKeys");

            migrationBuilder.CreateTable(
                name: "TtsApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Iv = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestsToday = table.Column<int>(type: "INTEGER", nullable: false),
                    LastResetUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisabledReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsApiKeys", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TtsApiKeys");

            migrationBuilder.AddColumn<string>(
                name: "DisabledReason",
                table: "ManagedApiKeys",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeminiModel",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerDay",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerMinute",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TtsModelName",
                table: "ManagedApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TtsProvider",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
