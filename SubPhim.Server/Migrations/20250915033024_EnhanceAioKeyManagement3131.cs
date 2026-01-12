using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceAioKeyManagement3131 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AioTranslationSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultModelName",
                value: "gemini-2.5-pro");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AioTranslationSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultModelName",
                value: "gemini-1.5-flash-latest");
        }
    }
}
