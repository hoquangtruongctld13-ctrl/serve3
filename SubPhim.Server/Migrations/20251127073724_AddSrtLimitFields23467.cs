using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSrtLimitFields23467 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail",
                table: "AioTtsServiceAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail_ModelType",
                table: "AioTtsServiceAccounts",
                columns: new[] { "ClientEmail", "ModelType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail_ModelType",
                table: "AioTtsServiceAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_AioTtsServiceAccounts_ClientEmail",
                table: "AioTtsServiceAccounts",
                column: "ClientEmail",
                unique: true);
        }
    }
}
