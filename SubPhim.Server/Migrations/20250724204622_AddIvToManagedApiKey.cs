using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIvToManagedApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ManagedApiKeys");

            migrationBuilder.AddColumn<int>(
                name: "BatchSize",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Iv",
                table: "ManagedApiKeys",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Rpm",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatchSize",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "Iv",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "Rpm",
                table: "ManagedApiKeys");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ManagedApiKeys",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
