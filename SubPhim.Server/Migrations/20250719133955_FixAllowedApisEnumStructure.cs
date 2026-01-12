using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class FixAllowedApisEnumStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllowedApiAccess",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DailyRequestLimitOverride",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BannedDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hwid = table.Column<string>(type: "TEXT", nullable: false),
                    LastKnownIp = table.Column<string>(type: "TEXT", nullable: true),
                    AssociatedUsername = table.Column<string>(type: "TEXT", nullable: true),
                    BanReason = table.Column<string>(type: "TEXT", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedDevices", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannedDevices");

            migrationBuilder.DropColumn(
                name: "AllowedApiAccess",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DailyRequestLimitOverride",
                table: "Users");
        }
    }
}
