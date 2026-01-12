using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields5646846541 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisabledReason",
                table: "ManagedApiKeys",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeminiModel",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerDay",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerMinute",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TtsProvider",
                table: "ManagedApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabledReason",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "GeminiModel",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerDay",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerMinute",
                table: "ManagedApiKeys");

            migrationBuilder.DropColumn(
                name: "TtsProvider",
                table: "ManagedApiKeys");
        }
    }
}
