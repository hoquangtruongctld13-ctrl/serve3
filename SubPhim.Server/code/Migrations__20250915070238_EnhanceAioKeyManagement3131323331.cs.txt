using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceAioKeyManagement3131323331 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UpdateInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LatestVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseNotes = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateInfos", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "UpdateInfos",
                columns: new[] { "Id", "DownloadUrl", "LastUpdated", "LatestVersion", "ReleaseNotes" },
                values: new object[] { 1, "https://example.com/download/latest", new DateTime(2025, 9, 15, 7, 2, 36, 831, DateTimeKind.Utc).AddTicks(7639), "1.0.0", "Phiên bản đầu tiên." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpdateInfos");
        }
    }
}
