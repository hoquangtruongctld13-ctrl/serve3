using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AllowSameClientEmailForDifferentModelTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create GoogleTtsModelConfigs table
            migrationBuilder.CreateTable(
                name: "GoogleTtsModelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelIdentifier = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MonthlyFreeLimit = table.Column<long>(type: "INTEGER", nullable: false),
                    PricePerMillionChars = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    SupportsSsml = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsSpeakingRate = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsPitch = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleTtsModelConfigs", x => x.Id);
                });

            // Add ModelType column to AioTtsServiceAccounts
            migrationBuilder.AddColumn<int>(
                name: "ModelType",
                table: "AioTtsServiceAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4); // Default to Chirp3HD

            // Add ModelType column to AioTtsBatchJobs
            migrationBuilder.AddColumn<int>(
                name: "ModelType",
                table: "AioTtsBatchJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4); // Default to Chirp3HD

            // Drop old unique index on ClientEmail only
            migrationBuilder.DropIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail",
                table: "AioTtsServiceAccounts");

            // Create new composite unique index on (ClientEmail, ModelType)
            migrationBuilder.CreateIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail_ModelType",
                table: "AioTtsServiceAccounts",
                columns: new[] { "ClientEmail", "ModelType" },
                unique: true);

            // Create unique index on ModelType for GoogleTtsModelConfigs
            migrationBuilder.CreateIndex(
                name: "IX_GoogleTtsModelConfigs_ModelType",
                table: "GoogleTtsModelConfigs",
                column: "ModelType",
                unique: true);

            // Seed GoogleTtsModelConfigs data
            migrationBuilder.InsertData(
                table: "GoogleTtsModelConfigs",
                columns: new[] { "Id", "ModelType", "ModelIdentifier", "MonthlyFreeLimit", "PricePerMillionChars", "SupportsSsml", "SupportsSpeakingRate", "SupportsPitch", "IsEnabled", "Description", "CreatedAt" },
                values: new object[,]
                {
                    { 1, 1, "Standard", 4000000L, 4.00m, true, true, true, true, "Standard voices - Cost-efficient general purpose TTS", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, 2, "Wavenet", 1000000L, 16.00m, true, true, true, true, "WaveNet voices - Premium synthetic speech with human-like quality", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, 3, "Neural2", 1000000L, 16.00m, true, true, true, true, "Neural2 voices - Premium with custom voice technology", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, 4, "Chirp3-HD", 1000000L, 30.00m, false, false, false, true, "Chirp 3: HD voices - Conversational agents with 30 distinct styles", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, 5, "Chirp-HD", 1000000L, 30.00m, false, false, false, false, "Chirp HD voices (Legacy) - Earlier generation Chirp voices", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, 6, "Studio", 1000000L, 16.00m, true, true, true, true, "Studio voices - News reading and broadcast content", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, 7, "Polyglot", 1000000L, 16.00m, true, true, true, true, "Polyglot voices - Multilingual capability", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 8, 8, "News", 1000000L, 16.00m, true, true, true, true, "News voices - Specialized for news reading", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 9, 9, "Casual", 1000000L, 16.00m, true, true, true, true, "Casual voices - Relaxed conversational style", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop GoogleTtsModelConfigs table
            migrationBuilder.DropTable(
                name: "GoogleTtsModelConfigs");

            // Drop composite index
            migrationBuilder.DropIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail_ModelType",
                table: "AioTtsServiceAccounts");

            // Remove ModelType column from AioTtsServiceAccounts
            migrationBuilder.DropColumn(
                name: "ModelType",
                table: "AioTtsServiceAccounts");

            // Remove ModelType column from AioTtsBatchJobs
            migrationBuilder.DropColumn(
                name: "ModelType",
                table: "AioTtsBatchJobs");

            // Restore old unique index on ClientEmail only
            migrationBuilder.CreateIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail",
                table: "AioTtsServiceAccounts",
                column: "ClientEmail",
                unique: true);
        }
    }
}
