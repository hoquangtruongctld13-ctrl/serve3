using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRpmPerProxySetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RpmPerProxy",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10); // Default RPM per proxy
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RpmPerProxy",
                table: "LocalApiSettings");
        }
    }
}
