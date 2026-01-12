using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLi42572 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorDetails",
                table: "TranslationJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FailedLinesCount",
                table: "TranslationJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasRefunded",
                table: "TranslationJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ErrorDetail",
                table: "TranslatedSrtLines",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorType",
                table: "TranslatedSrtLines",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorDetails",
                table: "TranslationJobs");

            migrationBuilder.DropColumn(
                name: "FailedLinesCount",
                table: "TranslationJobs");

            migrationBuilder.DropColumn(
                name: "HasRefunded",
                table: "TranslationJobs");

            migrationBuilder.DropColumn(
                name: "ErrorDetail",
                table: "TranslatedSrtLines");

            migrationBuilder.DropColumn(
                name: "ErrorType",
                table: "TranslatedSrtLines");
        }
    }
}
