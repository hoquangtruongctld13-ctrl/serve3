using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChangeTierDefaultSettingsPrimaryKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite requires table rebuild to change primary key
            // Step 1: Create new table with correct structure
            migrationBuilder.Sql(@"
                CREATE TABLE ""TierDefaultSettings_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_TierDefaultSettings"" PRIMARY KEY AUTOINCREMENT,
                    ""Tier"" INTEGER NOT NULL,
                    ""VideoDurationMinutes"" INTEGER NOT NULL,
                    ""DailyVideoCount"" INTEGER NOT NULL,
                    ""DailyTranslationRequests"" INTEGER NOT NULL,
                    ""DailySrtLineLimit"" INTEGER NOT NULL,
                    ""DailyLocalSrtLimit"" INTEGER NOT NULL,
                    ""AllowedApiAccess"" INTEGER NOT NULL,
                    ""AllowedApis"" INTEGER NOT NULL,
                    ""GrantedFeatures"" INTEGER NOT NULL,
                    ""IsYearlyProSettings"" INTEGER NOT NULL,
                    ""AioCharacterLimit"" INTEGER NOT NULL,
                    ""AioRequestsPerMinute"" INTEGER NOT NULL,
                    ""TtsCharacterLimit"" INTEGER NOT NULL,
                    ""DailyVipSrtLimit"" INTEGER NOT NULL
                );
            ");

            // Step 2: Copy data from old table to new table
            migrationBuilder.Sql(@"
                INSERT INTO ""TierDefaultSettings_new"" (
                    ""Tier"", ""VideoDurationMinutes"", ""DailyVideoCount"", ""DailyTranslationRequests"",
                    ""DailySrtLineLimit"", ""DailyLocalSrtLimit"", ""AllowedApiAccess"", ""AllowedApis"",
                    ""GrantedFeatures"", ""IsYearlyProSettings"", ""AioCharacterLimit"", ""AioRequestsPerMinute"",
                    ""TtsCharacterLimit"", ""DailyVipSrtLimit""
                )
                SELECT 
                    ""Tier"", ""VideoDurationMinutes"", ""DailyVideoCount"", ""DailyTranslationRequests"",
                    ""DailySrtLineLimit"", COALESCE(""DailyLocalSrtLimit"", 0), ""AllowedApiAccess"", ""AllowedApis"",
                    ""GrantedFeatures"", COALESCE(""IsYearlyProSettings"", 0), COALESCE(""AioCharacterLimit"", 0), 
                    COALESCE(""AioRequestsPerMinute"", 0), COALESCE(""TtsCharacterLimit"", 0), COALESCE(""DailyVipSrtLimit"", 0)
                FROM ""TierDefaultSettings"";
            ");

            // Step 3: Drop old table
            migrationBuilder.Sql(@"DROP TABLE ""TierDefaultSettings"";");

            // Step 4: Rename new table to original name
            migrationBuilder.Sql(@"ALTER TABLE ""TierDefaultSettings_new"" RENAME TO ""TierDefaultSettings"";");

            // Step 5: Create unique index
            migrationBuilder.CreateIndex(
                name: "IX_TierDefaultSettings_Tier_IsYearlyProSettings",
                table: "TierDefaultSettings",
                columns: new[] { "Tier", "IsYearlyProSettings" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Create old table structure (with Tier as primary key)
            migrationBuilder.Sql(@"
                CREATE TABLE ""TierDefaultSettings_old"" (
                    ""Tier"" INTEGER NOT NULL CONSTRAINT ""PK_TierDefaultSettings"" PRIMARY KEY,
                    ""VideoDurationMinutes"" INTEGER NOT NULL,
                    ""DailyVideoCount"" INTEGER NOT NULL,
                    ""DailyTranslationRequests"" INTEGER NOT NULL,
                    ""DailySrtLineLimit"" INTEGER NOT NULL,
                    ""DailyLocalSrtLimit"" INTEGER NOT NULL,
                    ""AllowedApiAccess"" INTEGER NOT NULL,
                    ""AllowedApis"" INTEGER NOT NULL,
                    ""GrantedFeatures"" INTEGER NOT NULL,
                    ""IsYearlyProSettings"" INTEGER NOT NULL,
                    ""AioCharacterLimit"" INTEGER NOT NULL,
                    ""AioRequestsPerMinute"" INTEGER NOT NULL,
                    ""TtsCharacterLimit"" INTEGER NOT NULL,
                    ""DailyVipSrtLimit"" INTEGER NOT NULL
                );
            ");

            // Step 2: Copy data (only unique Tier values, take first for each)
            migrationBuilder.Sql(@"
                INSERT INTO ""TierDefaultSettings_old"" (
                    ""Tier"", ""VideoDurationMinutes"", ""DailyVideoCount"", ""DailyTranslationRequests"",
                    ""DailySrtLineLimit"", ""DailyLocalSrtLimit"", ""AllowedApiAccess"", ""AllowedApis"",
                    ""GrantedFeatures"", ""IsYearlyProSettings"", ""AioCharacterLimit"", ""AioRequestsPerMinute"",
                    ""TtsCharacterLimit"", ""DailyVipSrtLimit""
                )
                SELECT 
                    ""Tier"", ""VideoDurationMinutes"", ""DailyVideoCount"", ""DailyTranslationRequests"",
                    ""DailySrtLineLimit"", ""DailyLocalSrtLimit"", ""AllowedApiAccess"", ""AllowedApis"",
                    ""GrantedFeatures"", ""IsYearlyProSettings"", ""AioCharacterLimit"", ""AioRequestsPerMinute"",
                    ""TtsCharacterLimit"", ""DailyVipSrtLimit""
                FROM ""TierDefaultSettings""
                WHERE ""IsYearlyProSettings"" = 0
                GROUP BY ""Tier"";
            ");

            // Step 3: Drop index and current table
            migrationBuilder.DropIndex(
                name: "IX_TierDefaultSettings_Tier_IsYearlyProSettings",
                table: "TierDefaultSettings");
            
            migrationBuilder.Sql(@"DROP TABLE ""TierDefaultSettings"";");

            // Step 4: Rename old table back
            migrationBuilder.Sql(@"ALTER TABLE ""TierDefaultSettings_old"" RENAME TO ""TierDefaultSettings"";");
        }
    }
}
