using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTierDefaultSettingsTable121 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TierDefaultSettings",
                columns: table => new
                {
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyVideoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyTranslationRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedApis = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantedFeatures = table.Column<int>(type: "INTEGER", nullable: false),
                    DailySrtLineLimit = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TierDefaultSettings", x => x.Tier);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TierDefaultSettings");
        }
    }
}
