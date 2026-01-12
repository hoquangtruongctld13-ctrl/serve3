using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddVipTranslationFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tables with IF NOT EXISTS - safe to run multiple times
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""VipApiKeys"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VipApiKeys"" PRIMARY KEY AUTOINCREMENT,
                    ""EncryptedApiKey"" TEXT NOT NULL,
                    ""Iv"" TEXT NOT NULL,
                    ""IsEnabled"" INTEGER NOT NULL,
                    ""TotalTokensUsed"" INTEGER NOT NULL,
                    ""RequestsToday"" INTEGER NOT NULL,
                    ""LastRequestCountResetUtc"" TEXT NOT NULL,
                    ""TemporaryCooldownUntil"" TEXT NULL,
                    ""DisabledReason"" TEXT NULL,
                    ""Consecutive429Count"" INTEGER NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""VipAvailableApiModels"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VipAvailableApiModels"" PRIMARY KEY AUTOINCREMENT,
                    ""ModelName"" TEXT NOT NULL,
                    ""IsActive"" INTEGER NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""VipTranslationSettings"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VipTranslationSettings"" PRIMARY KEY,
                    ""Rpm"" INTEGER NOT NULL,
                    ""BatchSize"" INTEGER NOT NULL,
                    ""MaxRetries"" INTEGER NOT NULL,
                    ""RetryDelayMs"" INTEGER NOT NULL,
                    ""DelayBetweenBatchesMs"" INTEGER NOT NULL,
                    ""Temperature"" TEXT NOT NULL,
                    ""MaxOutputTokens"" INTEGER NOT NULL,
                    ""EnableThinkingBudget"" INTEGER NOT NULL,
                    ""ThinkingBudget"" INTEGER NOT NULL,
                    ""RpmPerProxy"" INTEGER NOT NULL
                );
            ");

            // Seed default VipTranslationSettings if not exists
            migrationBuilder.Sql(@"
                INSERT INTO ""VipTranslationSettings"" (""Id"", ""Rpm"", ""BatchSize"", ""MaxRetries"", ""RetryDelayMs"", ""DelayBetweenBatchesMs"", ""Temperature"", ""MaxOutputTokens"", ""EnableThinkingBudget"", ""ThinkingBudget"", ""RpmPerProxy"")
                SELECT 1, 60, 10, 3, 1000, 100, '0.7', 8192, 0, 0, 10
                WHERE NOT EXISTS (SELECT 1 FROM ""VipTranslationSettings"" WHERE ""Id"" = 1);
            ");

            // NOTE: Columns for Users and TierDefaultSettings tables are added via EnsureMissingColumnsExist() in Program.cs
            // This is because SQLite doesn't support IF NOT EXISTS for ALTER TABLE ADD COLUMN
            // and the migration may have already been applied partially
        }
        
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""VipApiKeys"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""VipAvailableApiModels"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""VipTranslationSettings"";");
        }
    }
}
