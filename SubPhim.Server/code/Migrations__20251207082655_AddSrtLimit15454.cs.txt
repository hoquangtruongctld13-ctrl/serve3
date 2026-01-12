using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimit15454 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalMaxRequests",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "GlobalWindowMinutes",
                table: "LocalApiSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GlobalMaxRequests",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GlobalWindowMinutes",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
