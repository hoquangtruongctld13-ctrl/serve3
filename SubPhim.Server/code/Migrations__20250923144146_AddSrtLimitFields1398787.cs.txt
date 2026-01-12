using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields1398787 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AioTtsServiceAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EncryptedJsonKey = table.Column<string>(type: "TEXT", nullable: false),
                    Iv = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CharactersUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    UsageMonth = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioTtsServiceAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail",
                table: "AioTtsServiceAccounts",
                column: "ClientEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AioTtsServiceAccounts");
        }
    }
}
