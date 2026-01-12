using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSr3131231 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DelayBetweenBatchesMs",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EnableThinkingBudget",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxOutputTokens",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryDelayMs",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Temperature",
                table: "LocalApiSettings",
                type: "decimal(3, 2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ThinkingBudget",
                table: "LocalApiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DelayBetweenBatchesMs",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "EnableThinkingBudget",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "MaxOutputTokens",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "RetryDelayMs",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "Temperature",
                table: "LocalApiSettings");

            migrationBuilder.DropColumn(
                name: "ThinkingBudget",
                table: "LocalApiSettings");
        }
    }
}
