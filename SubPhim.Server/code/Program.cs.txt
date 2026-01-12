using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SubPhim.Server.Data;
using SubPhim.Server.Models;
using SubPhim.Server.Services;
using SubPhim.Server.Settings;
using System.Security.Claims;
using System.Text;
using System.Globalization; 
using Microsoft.AspNetCore.Localization;
using SubPhim.Server.Middleware;


var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection("SmtpSettings")
);
builder.Services.AddTransient<IEmailService, EmailService>();
builder.Services.Configure<LocalApiSettings>(
    builder.Configuration.GetSection(LocalApiSettings.SectionName)
);
builder.Services.Configure<UsageLimitsSettings>(
    builder.Configuration.GetSection("UsageLimits")
);
builder.Services.AddScoped<ITtsOrchestratorService, TtsOrchestratorService>();
builder.Services.AddScoped<ITtsSettingsService, TtsSettingsService>();
builder.Services.AddHostedService<TtsKeyResetService>();
builder.Services.AddHostedService<AioKeyResetService>();
// === BẮT ĐẦU THÊM: Register Cooldown Services ===
builder.Services.AddSingleton<ApiKeyCooldownService>(); // Singleton để share cache
builder.Services.AddSingleton<VipApiKeyCooldownService>(); // Singleton để quản lý cooldown cho VIP API keys
builder.Services.AddSingleton<JobCancellationService>(); // Singleton để quản lý cancellation tokens cho job dịch SRT
builder.Services.AddSingleton<GlobalRequestRateLimiterService>(); // NO-OP service (global rate limiting disabled, kept for compatibility)
builder.Services.AddSingleton<ProxyService>(); // Singleton để quản lý và luân phiên proxy
builder.Services.AddSingleton<ProxyRateLimiterService>(); // Singleton để quản lý RPM per proxy
builder.Services.AddHostedService<ManagedApiKeyResetService>(); // Background service cho LocalAPI keys
builder.Services.AddHostedService<VipApiKeyResetService>(); // Background service cho VIP API keys
// === KẾT THÚC THÊM ===
builder.Services.AddHostedService<AioTtsBatchProcessorService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ITierSettingsService, TierSettingsService>();
builder.Services.AddScoped<IAioLauncherService, AioLauncherService>();
builder.Services.AddSingleton<AioTtsSaStore>();
builder.Services.AddSingleton<AioTtsDispatcherService>();
builder.Services.AddHttpClient("AioLauncherClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); 
});
builder.Services.AddControllers();
builder.Services.AddSingleton<TranslationOrchestratorService>();
builder.Services.AddSingleton<VipTranslationService>();
builder.Services.AddScoped<ISubtitleOrchestratorService, SubtitleOrchestratorService>(); // Subtitle distributed translation
builder.Services.AddMemoryCache();

// External API Key Management Services
builder.Services.AddScoped<IExternalApiKeyService, ExternalApiKeyService>();
builder.Services.AddScoped<IExternalApiCreditService, ExternalApiCreditService>();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en-US") };
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminPolicy");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
});
var dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLY_APP_NAME")))
{
    dbConnectionString = "Data Source=/data/subphim.db";
}

// Configure SQLite for better concurrency handling
// WAL mode allows concurrent reads and writes, significantly reducing lock contention
var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(dbConnectionString)
{
    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
    Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared,
    Pooling = true
};
dbConnectionString = connectionStringBuilder.ToString();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(dbConnectionString, sqliteOptions =>
    {
        // Set command timeout to handle longer operations
        sqliteOptions.CommandTimeout(30);
    });
    
    // Suppress PendingModelChangesWarning since we handle schema changes manually in EnsureMissingColumnsExist
    options.ConfigureWarnings(warnings =>
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    
    // Enable sensitive data logging in development for debugging
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLY_APP_NAME")))
{
    var dataProtectionPath = new DirectoryInfo("/data/keys");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(dataProtectionPath)
        .SetApplicationName("SubPhimApp");
}
else
{
    builder.Services.AddDataProtection();
}
var jwtKey = "SubPhim-Super-Secret-Key-For-JWT-Authentication-2024-@!#$";
var key = Encoding.ASCII.GetBytes(jwtKey);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    })
    .AddScheme<AuthenticationSchemeOptions, SubPhim.Server.Authentication.ExternalApiKeyAuthenticationHandler>(
        "ExternalApiKey", null)
    .AddCookie("AdminCookie", options =>
    {
        options.Cookie.Name = "SubPhim.AdminAuth";
        options.LoginPath = "/Admin/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddAuthenticationSchemes("AdminCookie");
        policy.RequireClaim("Admin", "true");
    });
    
    // Policy for External API authentication
    options.AddPolicy("ExternalApiPolicy", policy =>
    {
        policy.AddAuthenticationSchemes("ExternalApiKey");
        policy.RequireClaim("api_key_id");
    });
});
var app = builder.Build();
app.UseMiddleware<RequestLoggingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseRequestLocalization();
app.UseAuthentication();

// Add External API rate limiting middleware (must be after authentication)
app.UseMiddleware<SubPhim.Server.Middleware.ExternalApiRateLimitMiddleware>();

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();


using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        logger.LogInformation("Attempting to connect to the database...");
        
        // Chạy migrate trước để tạo database nếu chưa tồn tại
        context.Database.Migrate();
        
        if (context.Database.CanConnect())
        {
            logger.LogInformation(">>>>>> DATABASE CONNECTION SUCCESSFUL. <<<<<<");
            
            // === BẮT ĐẦU: Thêm các cột còn thiếu SAU KHI migrate ===
            // Phải chạy sau Migrate() nhưng trước khi EF Core query bất kỳ entity nào
            EnsureMissingColumnsExist(context, logger);
            // === KẾT THÚC ===
        }
        else
        {
            logger.LogCritical("!!!!!!!! DATABASE CONNECTION FAILED. CHECK CONNECTION STRING AND FILE PERMISSIONS. !!!!!!!");
        }

        var adminUsername = "admin";
        var defaultAdminPassword = "AdminMatKhauMoi123!";
        var adminUser = context.Users.FirstOrDefault(u => u.Username == adminUsername);

        if (adminUser == null)
        {
            logger.LogInformation("Admin user not found. Creating a new one.");
            adminUser = new User
            {
                Uid = new Random().Next(100_000_000, 1_000_000_000).ToString(),
                Username = adminUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultAdminPassword),
                Tier = SubscriptionTier.Lifetime,
                SubscriptionExpiry = DateTime.UtcNow.AddYears(100),
                IsBlocked = false,
                CreatedAt = DateTime.UtcNow,
                MaxDevices = 99,
                GrantedFeatures = (GrantedFeatures)Enum.GetValues(typeof(GrantedFeatures)).Cast<int>().Sum(),
                AllowedApiAccess = (AllowedApis)Enum.GetValues(typeof(AllowedApis)).Cast<int>().Sum(),
                IsAdmin = true // Đảm bảo quyền admin
            };
            context.Users.Add(adminUser);
        }
        else
        {
            logger.LogInformation("Admin user found. Ensuring permissions are correctly set.");
            adminUser.IsAdmin = true; 
            adminUser.Tier = SubscriptionTier.Lifetime; 
            adminUser.IsBlocked = false; 
        }

        context.SaveChanges(); 
        logger.LogInformation("Admin user seeding/update completed successfully.");
        try
        {
            if (!context.TierDefaultSettings.Any())
            {
                logger.LogInformation("TierDefaultSettings table is empty. Seeding from appsettings.json...");
                var usageLimitsSettings = services.GetRequiredService<IOptions<UsageLimitsSettings>>().Value;

                foreach (SubscriptionTier tierValue in Enum.GetValues(typeof(SubscriptionTier)))
                {
                    var tierConfigFromFile = tierValue switch
                    {
                        SubscriptionTier.Free => usageLimitsSettings.Free,
                        SubscriptionTier.Daily => usageLimitsSettings.Daily,
                        SubscriptionTier.Monthly => usageLimitsSettings.Monthly,
                        SubscriptionTier.Yearly => usageLimitsSettings.Yearly,
                        SubscriptionTier.Lifetime => usageLimitsSettings.Lifetime,
                        _ => null
                    };

                    if (tierConfigFromFile != null)
                    {
                        var newDefaultSetting = new TierDefaultSetting
                        {
                            Tier = tierValue,
                            VideoDurationMinutes = tierConfigFromFile.VideoDurationMinutes,
                            DailyVideoCount = tierConfigFromFile.DailyVideoCount,
                            DailyTranslationRequests = tierConfigFromFile.DailyTranslationRequests,
                            DailySrtLineLimit = tierConfigFromFile.DailySrtLineLimit,
                            AllowedApis = Enum.TryParse<AllowedApis>(tierConfigFromFile.AllowedApis, true, out var apis) ? apis : AllowedApis.None,
                            GrantedFeatures = Enum.TryParse<GrantedFeatures>(tierConfigFromFile.GrantedFeatures, true, out var features) ? features : GrantedFeatures.None
                        };
                        context.TierDefaultSettings.Add(newDefaultSetting);
                    }
                }
                context.SaveChanges();
                logger.LogInformation("Successfully seeded TierDefaultSettings.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during TierDefaultSettings seeding.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during DB migration or admin user seeding.");
    }
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        if (!context.TtsModelSettings.Any())
        {
            logger.LogInformation("Seeding default TTS Model Settings...");

            var defaultSettings = new List<TtsModelSetting>
            {
                new TtsModelSetting { Provider = TtsProvider.Gemini, Identifier = "Pro", ModelName = "gemini-2.5-pro-preview-tts", MaxRequestsPerDay = 100, MaxRequestsPerMinute = 10 },
                new TtsModelSetting { Provider = TtsProvider.Gemini, Identifier = "Flash", ModelName = "gemini-2.5-flash-preview-tts", MaxRequestsPerDay = 15, MaxRequestsPerMinute = 3 },
                new TtsModelSetting { Provider = TtsProvider.ElevenLabs, Identifier = "ElevenLabs", ModelName = "eleven_multilingual_v2", MaxRequestsPerDay = -1, MaxRequestsPerMinute = -1 }
            };

            context.TtsModelSettings.AddRange(defaultSettings);
            context.SaveChanges();
            logger.LogInformation("Successfully seeded TTS Model Settings.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during TTS Model Settings seeding.");
    }
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLY_APP_NAME")))
{
    app.Run("http://*:8080");
}
else
{
    app.Run("http://*:5000");
}

// Helper method để thêm các cột và bảng còn thiếu vào database trước khi EF Core migrate
static void EnsureMissingColumnsExist(AppDbContext context, ILogger logger)
{
    var connection = context.Database.GetDbConnection();
    try
    {
        connection.Open();
        
        // === TẠO CÁC BẢNG CÒN THIẾU ===
        var tablesToCreate = new List<(string TableName, string CreateStatement)>
        {
            ("VipApiKeys", @"
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
                );"),
            ("VipAvailableApiModels", @"
                CREATE TABLE IF NOT EXISTS ""VipAvailableApiModels"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VipAvailableApiModels"" PRIMARY KEY AUTOINCREMENT,
                    ""ModelName"" TEXT NOT NULL,
                    ""IsActive"" INTEGER NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );"),
            ("VipTranslationSettings", @"
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
                );")
        };

        foreach (var (tableName, createStatement) in tablesToCreate)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = createStatement;
                cmd.ExecuteNonQuery();
                logger.LogInformation("Ensured table {TableName} exists", tableName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create table {TableName}", tableName);
            }
        }

        // Seed default VipTranslationSettings if empty
        try
        {
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = @"SELECT COUNT(*) FROM ""VipTranslationSettings"" WHERE ""Id"" = 1";
            var count = Convert.ToInt32(checkCmd.ExecuteScalar());
            
            if (count == 0)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO ""VipTranslationSettings"" (""Id"", ""Rpm"", ""BatchSize"", ""MaxRetries"", ""RetryDelayMs"", ""DelayBetweenBatchesMs"", ""Temperature"", ""MaxOutputTokens"", ""EnableThinkingBudget"", ""ThinkingBudget"", ""RpmPerProxy"", ""MaxSrtLineLength"")
                    VALUES (1, 60, 10, 3, 1000, 100, '0.7', 8192, 0, 0, 10, 3000);";
                insertCmd.ExecuteNonQuery();
                logger.LogInformation("Seeded default VipTranslationSettings");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed VipTranslationSettings");
        }

        // === THÊM CÁC CỘT CÒN THIẾU ===
        var columnsToAdd = new List<(string TableName, string ColumnName, string ColumnDefinition)>
        {
            ("Users", "DailyVipSrtLimit", "INTEGER NOT NULL DEFAULT 0"),
            ("Users", "VipSrtLinesUsedToday", "INTEGER NOT NULL DEFAULT 0"),
            ("Users", "LastVipSrtResetUtc", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
            ("TierDefaultSettings", "DailyVipSrtLimit", "INTEGER NOT NULL DEFAULT 0"),
            ("VipTranslationSettings", "MaxSrtLineLength", "INTEGER NOT NULL DEFAULT 3000")
        };

        foreach (var (tableName, columnName, columnDefinition) in columnsToAdd)
        {
            if (!ColumnExists(connection, tableName, columnName))
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $@"ALTER TABLE ""{tableName}"" ADD COLUMN ""{columnName}"" {columnDefinition};";
                    cmd.ExecuteNonQuery();
                    logger.LogInformation("Added missing column {ColumnName} to table {TableName}", columnName, tableName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to add column {ColumnName} to table {TableName} (may already exist)", columnName, tableName);
                }
            }
        }
    }
    finally
    {
        connection.Close();
    }
}

static bool ColumnExists(System.Data.Common.DbConnection connection, string tableName, string columnName)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = '{columnName}'";
    var result = cmd.ExecuteScalar();
    return Convert.ToInt32(result) > 0;
}
