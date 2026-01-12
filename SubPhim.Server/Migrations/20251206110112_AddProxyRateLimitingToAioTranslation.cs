using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyRateLimitingToAioTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Genre",
                table: "AioTranslationJobs",
                newName: "SystemInstruction");

            migrationBuilder.AddColumn<int>(
                name: "AioRequestsToday",
                table: "Proxies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAioResetUtc",
                table: "Proxies",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RpdPerProxy",
                table: "AioTranslationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RpmPerProxy",
                table: "AioTranslationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AioTranslationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "RpdPerProxy", "RpmPerProxy" },
                values: new object[] { 1500, 60 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AioRequestsToday",
                table: "Proxies");

            migrationBuilder.DropColumn(
                name: "LastAioResetUtc",
                table: "Proxies");

            migrationBuilder.DropColumn(
                name: "RpdPerProxy",
                table: "AioTranslationSettings");

            migrationBuilder.DropColumn(
                name: "RpmPerProxy",
                table: "AioTranslationSettings");

            migrationBuilder.RenameColumn(
                name: "SystemInstruction",
                table: "AioTranslationJobs",
                newName: "Genre");
        }
    }
}
