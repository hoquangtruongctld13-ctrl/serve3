using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields419898 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModelType",
                table: "AioTtsServiceAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ModelType",
                table: "AioTtsBatchJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GoogleTtsModelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelIdentifier = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MonthlyFreeLimit = table.Column<long>(type: "INTEGER", nullable: false),
                    PricePerMillionChars = table.Column<decimal>(type: "decimal(10, 2)", nullable: false),
                    SupportsSsml = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsSpeakingRate = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsPitch = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleTtsModelConfigs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "GoogleTtsModelConfigs",
                columns: new[] { "Id", "CreatedAt", "Description", "IsEnabled", "ModelIdentifier", "ModelType", "MonthlyFreeLimit", "PricePerMillionChars", "SupportsPitch", "SupportsSpeakingRate", "SupportsSsml" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Standard voices - Cost-efficient general purpose TTS", true, "Standard", 1, 4000000L, 4.00m, true, true, true },
                    { 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "WaveNet voices - Premium synthetic speech with human-like quality", true, "Wavenet", 2, 1000000L, 16.00m, true, true, true },
                    { 3, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Neural2 voices - Premium with custom voice technology", true, "Neural2", 3, 1000000L, 16.00m, true, true, true },
                    { 4, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Chirp 3: HD voices - Conversational agents with 30 distinct styles", true, "Chirp3-HD", 4, 1000000L, 30.00m, false, false, false },
                    { 5, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Chirp HD voices (Legacy) - Earlier generation Chirp voices", false, "Chirp-HD", 5, 1000000L, 30.00m, false, false, false },
                    { 6, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Studio voices - News reading and broadcast content", true, "Studio", 6, 1000000L, 16.00m, true, true, true },
                    { 7, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Polyglot voices - Multilingual capability", true, "Polyglot", 7, 1000000L, 16.00m, true, true, true },
                    { 8, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "News voices - Specialized for news reading", true, "News", 8, 1000000L, 16.00m, true, true, true },
                    { 9, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Casual voices - Relaxed conversational style", true, "Casual", 9, 1000000L, 16.00m, true, true, true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleTtsModelConfigs_ModelType",
                table: "GoogleTtsModelConfigs",
                column: "ModelType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleTtsModelConfigs");

            migrationBuilder.DropColumn(
                name: "ModelType",
                table: "AioTtsServiceAccounts");

            migrationBuilder.DropColumn(
                name: "ModelType",
                table: "AioTtsBatchJobs");
        }
    }
}
