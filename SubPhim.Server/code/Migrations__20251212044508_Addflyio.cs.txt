using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class Addflyio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxSrtLineLength",
                table: "VipTranslationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SubtitleApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Iv = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestsToday = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSuccessRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalFailedRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    LastResetUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CooldownUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Consecutive429Count = table.Column<int>(type: "INTEGER", nullable: false),
                    DisabledReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleApiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LinesPerServer = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchSizePerServer = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiKeysPerServer = table.Column<int>(type: "INTEGER", nullable: false),
                    MergeBatchThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxServerRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    DelayBetweenServerBatchesMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiKeyCooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableCallback = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Temperature = table.Column<decimal>(type: "decimal(3, 2)", nullable: false),
                    ThinkingBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleApiSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleTranslationJobs",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExternalApiKeyPrefix = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalLines = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedLines = table.Column<int>(type: "INTEGER", nullable: false),
                    Progress = table.Column<float>(type: "REAL", nullable: false),
                    SystemInstruction = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ThinkingBudget = table.Column<int>(type: "INTEGER", nullable: true),
                    CallbackUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalLinesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKeyUsageJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleTranslationJobs", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleTranslationServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RpmLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBusy = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentSessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UsageCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<long>(type: "INTEGER", nullable: false),
                    AvgResponseTimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleTranslationServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleServerTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StartLineIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProcessingTimeMs = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleServerTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleServerTasks_SubtitleTranslationJobs_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SubtitleTranslationJobs",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubtitleServerTasks_SubtitleTranslationServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "SubtitleTranslationServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SubtitleApiSettings",
                columns: new[] { "Id", "ApiKeyCooldownMinutes", "ApiKeysPerServer", "BatchSizePerServer", "DefaultModel", "DelayBetweenServerBatchesMs", "EnableCallback", "LinesPerServer", "MaxServerRetries", "MergeBatchThreshold", "ServerTimeoutSeconds", "Temperature", "ThinkingBudget", "UpdatedAt" },
                values: new object[] { 1, 5, 5, 40, "gemini-2.5-flash", 500, true, 120, 3, 10, 300, 0.3m, 0, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleApiKeys_CooldownUntil",
                table: "SubtitleApiKeys",
                column: "CooldownUntil");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleApiKeys_IsEnabled",
                table: "SubtitleApiKeys",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleServerTasks_ServerId",
                table: "SubtitleServerTasks",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleServerTasks_SessionId",
                table: "SubtitleServerTasks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleServerTasks_Status",
                table: "SubtitleServerTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationJobs_CreatedAt",
                table: "SubtitleTranslationJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationJobs_Status",
                table: "SubtitleTranslationJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationJobs_UserId",
                table: "SubtitleTranslationJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationServers_IsBusy",
                table: "SubtitleTranslationServers",
                column: "IsBusy");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationServers_IsEnabled",
                table: "SubtitleTranslationServers",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTranslationServers_ServerUrl",
                table: "SubtitleTranslationServers",
                column: "ServerUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubtitleApiKeys");

            migrationBuilder.DropTable(
                name: "SubtitleApiSettings");

            migrationBuilder.DropTable(
                name: "SubtitleServerTasks");

            migrationBuilder.DropTable(
                name: "SubtitleTranslationJobs");

            migrationBuilder.DropTable(
                name: "SubtitleTranslationServers");

            migrationBuilder.DropColumn(
                name: "MaxSrtLineLength",
                table: "VipTranslationSettings");
        }
    }
}
