using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalApiEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    KeySuffix = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreditBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCreditsUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCreditsAdded = table.Column<long>(type: "INTEGER", nullable: false),
                    RpmLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalApiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreditsPerCharacter = table.Column<int>(type: "INTEGER", nullable: false),
                    VndPerCredit = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DefaultRpm = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultInitialCredits = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalApiCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKeyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RelatedUsageLogId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiCreditTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalApiCreditTransactions_ExternalApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ExternalApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalApiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKeyId = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    InputLines = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputCharacters = table.Column<int>(type: "INTEGER", nullable: false),
                    CreditsCharged = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    GeminiErrors = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalApiUsageLogs_ExternalApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ExternalApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ExternalApiSettings",
                columns: new[] { "Id", "CreditsPerCharacter", "DefaultInitialCredits", "DefaultRpm", "UpdatedAt", "VndPerCredit" },
                values: new object[] { 1, 5, 0L, 100, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 10m });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiCreditTransactions_ApiKeyId",
                table: "ExternalApiCreditTransactions",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiCreditTransactions_CreatedAt",
                table: "ExternalApiCreditTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiCreditTransactions_Type",
                table: "ExternalApiCreditTransactions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_CreatedAt",
                table: "ExternalApiKeys",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_IsEnabled",
                table: "ExternalApiKeys",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_KeyHash",
                table: "ExternalApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiUsageLogs_ApiKeyId",
                table: "ExternalApiUsageLogs",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiUsageLogs_SessionId",
                table: "ExternalApiUsageLogs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiUsageLogs_StartedAt",
                table: "ExternalApiUsageLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiUsageLogs_Status",
                table: "ExternalApiUsageLogs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalApiCreditTransactions");

            migrationBuilder.DropTable(
                name: "ExternalApiSettings");

            migrationBuilder.DropTable(
                name: "ExternalApiUsageLogs");

            migrationBuilder.DropTable(
                name: "ExternalApiKeys");
        }
    }
}
