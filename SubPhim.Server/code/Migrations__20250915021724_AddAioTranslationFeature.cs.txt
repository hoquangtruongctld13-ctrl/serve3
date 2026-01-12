using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAioTranslationFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AioCharactersUsedToday",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAioResetUtc",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "AioCharacterLimit",
                table: "TierDefaultSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AioRequestsPerMinute",
                table: "TierDefaultSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AioApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Iv = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestsToday = table.Column<int>(type: "INTEGER", nullable: false),
                    LastResetUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisabledReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AioTranslationJobs",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalContent = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedContent = table.Column<string>(type: "TEXT", nullable: true),
                    Genre = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioTranslationJobs", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_AioTranslationJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AioTranslationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultModelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Temperature = table.Column<decimal>(type: "decimal(3, 2)", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableThinkingBudget = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThinkingBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    RpmPerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxApiRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryApiDelayMs = table.Column<int>(type: "INTEGER", nullable: false),
                    DelayBetweenFilesMs = table.Column<int>(type: "INTEGER", nullable: false),
                    DelayBetweenChunksMs = table.Column<int>(type: "INTEGER", nullable: false),
                    DirectSendThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkSize = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioTranslationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TranslationGenres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GenreName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SystemInstruction = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationGenres", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AioTranslationSettings",
                columns: new[] { "Id", "ChunkSize", "DefaultModelName", "DelayBetweenChunksMs", "DelayBetweenFilesMs", "DirectSendThreshold", "EnableThinkingBudget", "MaxApiRetries", "MaxOutputTokens", "RetryApiDelayMs", "RpmPerKey", "Temperature", "ThinkingBudget" },
                values: new object[] { 1, 3500, "gemini-1.5-flash-latest", 5000, 5000, 8000, true, 3, 8192, 10000, 10, 0.7m, 8192 });

            migrationBuilder.CreateIndex(
                name: "IX_AioTranslationJobs_UserId",
                table: "AioTranslationJobs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AioApiKeys");

            migrationBuilder.DropTable(
                name: "AioTranslationJobs");

            migrationBuilder.DropTable(
                name: "AioTranslationSettings");

            migrationBuilder.DropTable(
                name: "TranslationGenres");

            migrationBuilder.DropColumn(
                name: "AioCharactersUsedToday",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastAioResetUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AioCharacterLimit",
                table: "TierDefaultSettings");

            migrationBuilder.DropColumn(
                name: "AioRequestsPerMinute",
                table: "TierDefaultSettings");
        }
    }
}
