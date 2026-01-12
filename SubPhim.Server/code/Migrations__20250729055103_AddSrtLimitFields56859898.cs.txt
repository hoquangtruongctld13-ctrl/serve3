using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields56859898 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranslationJobs",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Genre = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationJobs", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "OriginalSrtLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OriginalSrtLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OriginalSrtLines_TranslationJobs_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TranslationJobs",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranslatedSrtLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    TranslatedText = table.Column<string>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslatedSrtLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranslatedSrtLines_TranslationJobs_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TranslationJobs",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OriginalSrtLines_SessionId",
                table: "OriginalSrtLines",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TranslatedSrtLines_SessionId",
                table: "TranslatedSrtLines",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OriginalSrtLines");

            migrationBuilder.DropTable(
                name: "TranslatedSrtLines");

            migrationBuilder.DropTable(
                name: "TranslationJobs");
        }
    }
}
