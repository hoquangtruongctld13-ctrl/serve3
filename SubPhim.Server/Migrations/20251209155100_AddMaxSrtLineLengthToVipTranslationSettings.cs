using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxSrtLineLengthToVipTranslationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxSrtLineLength",
                table: "VipTranslationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3000); // Default max characters per line
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxSrtLineLength",
                table: "VipTranslationSettings");
        }
    }
}
