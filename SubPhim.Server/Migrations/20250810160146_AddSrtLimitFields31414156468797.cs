using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields31414156468797 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TtsModelName",
                table: "ManagedApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TtsModelSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Identifier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MaxRequestsPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRequestsPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsModelSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TtsModelSettings");

            migrationBuilder.DropColumn(
                name: "TtsModelName",
                table: "ManagedApiKeys");
        }
    }
}
