using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomPromptToTranslationJob1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomPrompt",
                table: "TranslationJobs");

            migrationBuilder.AddColumn<string>(
                name: "SystemInstruction",
                table: "TranslationJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemInstruction",
                table: "TranslationJobs");

            migrationBuilder.AddColumn<string>(
                name: "CustomPrompt",
                table: "TranslationJobs",
                type: "TEXT",
                nullable: true);
        }
    }
}
