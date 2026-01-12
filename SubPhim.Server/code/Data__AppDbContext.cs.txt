// VỊ TRÍ: Data/AppDbContext.cs

using Microsoft.EntityFrameworkCore;

namespace SubPhim.Server.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<AioTtsServiceAccount> AioTtsServiceAccounts { get; set; }
        public DbSet<AioTtsBatchJob> AioTtsBatchJobs { get; set; }
        public DbSet<GoogleTtsModelConfig> GoogleTtsModelConfigs { get; set; }
        public DbSet<TierDefaultSetting> TierDefaultSettings { get; set; }
        public DbSet<LocalApiSetting> LocalApiSettings { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Device> Devices { get; set; }
        public DbSet<BannedDevice> BannedDevices { get; set; }
        public DbSet<ManagedApiKey> ManagedApiKeys { get; set; }
        public DbSet<TranslationLog> TranslationLogs { get; set; }
        public DbSet<AvailableApiModel> AvailableApiModels { get; set; }
        public DbSet<TranslationJobDb> TranslationJobs { get; set; }
        public DbSet<OriginalSrtLineDb> OriginalSrtLines { get; set; }
        public DbSet<TranslatedSrtLineDb> TranslatedSrtLines { get; set; }
        public DbSet<TtsApiKey> TtsApiKeys { get; set; }
        public DbSet<AioTranslationSetting> AioTranslationSettings { get; set; }
        public DbSet<AioApiKey> AioApiKeys { get; set; }
        public DbSet<TranslationGenre> TranslationGenres { get; set; }
        public DbSet<AioTranslationJob> AioTranslationJobs { get; set; }
        public DbSet<TtsModelSetting> TtsModelSettings { get; set; }
        public DbSet<UpdateInfo> UpdateInfos { get; set; }
        public DbSet<SaOcrServiceAccount> SaOcrServiceAccounts { get; set; }
        public DbSet<Proxy> Proxies { get; set; }
        
        // VIP Translation DbSets
        public DbSet<VipTranslationSetting> VipTranslationSettings { get; set; }
        public DbSet<VipApiKey> VipApiKeys { get; set; }
        public DbSet<VipAvailableApiModel> VipAvailableApiModels { get; set; }
        
        // External API Key Management DbSets
        public DbSet<ExternalApiKey> ExternalApiKeys { get; set; }
        public DbSet<ExternalApiUsageLog> ExternalApiUsageLogs { get; set; }
        public DbSet<ExternalApiCreditTransaction> ExternalApiCreditTransactions { get; set; }
        public DbSet<ExternalApiSettings> ExternalApiSettings { get; set; }

        // Subtitle Translation (Distributed Servers) DbSets
        public DbSet<SubtitleApiSetting> SubtitleApiSettings { get; set; }
        public DbSet<SubtitleApiKey> SubtitleApiKeys { get; set; }
        public DbSet<SubtitleTranslationServer> SubtitleTranslationServers { get; set; }
        public DbSet<SubtitleTranslationJob> SubtitleTranslationJobs { get; set; }
        public DbSet<SubtitleServerTask> SubtitleServerTasks { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) 
        {
            // Configure SQLite for better concurrency on every connection
            // Note: These PRAGMA commands are lightweight and properly scoped:
            // - journal_mode=WAL: Idempotent, persists in DB file, enables concurrent reads/writes
            // - busy_timeout: Connection-level, must be set for each connection
            // - synchronous=NORMAL: Connection-level, balances safety and performance
            // EF Core's connection pooling ensures these are not executed excessively
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
            Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TranslationJobDb>()
                .HasMany(j => j.OriginalLines)
                .WithOne(l => l.Job)
                .HasForeignKey(l => l.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TranslationJobDb>()
                .HasMany(j => j.TranslatedLines)
                .WithOne(l => l.Job)
                .HasForeignKey(l => l.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<User>()
                .HasMany(u => u.Devices)
                .WithOne(d => d.User)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Configure TierDefaultSetting with composite unique index
            modelBuilder.Entity<TierDefaultSetting>()
                .HasIndex(t => new { t.Tier, t.IsYearlyProSettings })
                .IsUnique();
            
            modelBuilder.Entity<AioTranslationSetting>()
                .HasData(new AioTranslationSetting { Id = 1 });
            modelBuilder.Entity<AioTtsServiceAccount>()
               .HasIndex(sa => new { sa.ClientEmail, sa.ModelType })
               .IsUnique();
            modelBuilder.Entity<AioTtsBatchJob>()
       .HasOne(j => j.User)
       .WithMany() // Nếu User không cần danh sách các job thì để trống
       .HasForeignKey(j => j.UserId)
       .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UpdateInfo>()
    .HasData(new UpdateInfo
    {
        Id = 1,
        LatestVersion = "1.0.0",
        DownloadUrl = "https://example.com/download/latest",
        ReleaseNotes = "Phiên bản đầu tiên.",
        LastUpdated = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    });
            modelBuilder.Entity<SaOcrServiceAccount>()
               .HasIndex(sa => sa.ClientEmail)
               .IsUnique();

            // Cấu hình GoogleTtsModelConfig với dữ liệu mặc định
            modelBuilder.Entity<GoogleTtsModelConfig>()
                .HasIndex(c => c.ModelType)
                .IsUnique();

            modelBuilder.Entity<GoogleTtsModelConfig>()
                .HasData(
                    new GoogleTtsModelConfig
                    {
                        Id = 1,
                        ModelType = GoogleTtsModelType.Standard,
                        ModelIdentifier = "Standard",
                        MonthlyFreeLimit = 4_000_000,
                        PricePerMillionChars = 4.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "Standard voices - Cost-efficient general purpose TTS",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 2,
                        ModelType = GoogleTtsModelType.WaveNet,
                        ModelIdentifier = "Wavenet",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "WaveNet voices - Premium synthetic speech with human-like quality",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 3,
                        ModelType = GoogleTtsModelType.Neural2,
                        ModelIdentifier = "Neural2",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "Neural2 voices - Premium with custom voice technology",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 4,
                        ModelType = GoogleTtsModelType.Chirp3HD,
                        ModelIdentifier = "Chirp3-HD",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 30.00m,
                        SupportsSsml = false,
                        SupportsSpeakingRate = false,
                        SupportsPitch = false,
                        IsEnabled = true,
                        Description = "Chirp 3: HD voices - Conversational agents with 30 distinct styles",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 5,
                        ModelType = GoogleTtsModelType.ChirpHD,
                        ModelIdentifier = "Chirp-HD",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 30.00m,
                        SupportsSsml = false,
                        SupportsSpeakingRate = false,
                        SupportsPitch = false,
                        IsEnabled = false,
                        Description = "Chirp HD voices (Legacy) - Earlier generation Chirp voices",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 6,
                        ModelType = GoogleTtsModelType.Studio,
                        ModelIdentifier = "Studio",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "Studio voices - News reading and broadcast content",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 7,
                        ModelType = GoogleTtsModelType.Polyglot,
                        ModelIdentifier = "Polyglot",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "Polyglot voices - Multilingual capability",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 8,
                        ModelType = GoogleTtsModelType.News,
                        ModelIdentifier = "News",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "News voices - Specialized for news reading",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new GoogleTtsModelConfig
                    {
                        Id = 9,
                        ModelType = GoogleTtsModelType.Casual,
                        ModelIdentifier = "Casual",
                        MonthlyFreeLimit = 1_000_000,
                        PricePerMillionChars = 16.00m,
                        SupportsSsml = true,
                        SupportsSpeakingRate = true,
                        SupportsPitch = true,
                        IsEnabled = true,
                        Description = "Casual voices - Relaxed conversational style",
                        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    }
                );
            
            // Configure External API Key entities
            modelBuilder.Entity<ExternalApiKey>(entity =>
            {
                entity.HasIndex(e => e.KeyHash).IsUnique();
                entity.HasIndex(e => e.IsEnabled);
                entity.HasIndex(e => e.CreatedAt);
            });

            modelBuilder.Entity<ExternalApiUsageLog>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ApiKeyId);
                entity.HasIndex(e => e.StartedAt);
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<ExternalApiCreditTransaction>(entity =>
            {
                entity.HasIndex(e => e.ApiKeyId);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.Type);
            });
            
            // Seed default External API Settings
            modelBuilder.Entity<ExternalApiSettings>()
                .HasData(new ExternalApiSettings
                {
                    Id = 1,
                    CreditsPerCharacter = 5,
                    VndPerCredit = 10,
                    DefaultRpm = 100,
                    DefaultInitialCredits = 0,
                    UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });

            // Configure Subtitle Translation entities
            modelBuilder.Entity<SubtitleTranslationJob>(entity =>
            {
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.UserId);

                entity.HasMany(j => j.ServerTasks)
                    .WithOne(t => t.Job)
                    .HasForeignKey(t => t.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SubtitleServerTask>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ServerId);
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<SubtitleTranslationServer>(entity =>
            {
                entity.HasIndex(e => e.IsEnabled);
                entity.HasIndex(e => e.IsBusy);
                entity.HasIndex(e => e.ServerUrl).IsUnique();
            });

            modelBuilder.Entity<SubtitleApiKey>(entity =>
            {
                entity.HasIndex(e => e.IsEnabled);
                entity.HasIndex(e => e.CooldownUntil);
            });

            // Seed default Subtitle API Settings
            modelBuilder.Entity<SubtitleApiSetting>()
                .HasData(new SubtitleApiSetting
                {
                    Id = 1,
                    LinesPerServer = 120,
                    BatchSizePerServer = 40,
                    ApiKeysPerServer = 5,
                    MergeBatchThreshold = 10,
                    ServerTimeoutSeconds = 300,
                    MaxServerRetries = 3,
                    DelayBetweenServerBatchesMs = 500,
                    ApiKeyCooldownMinutes = 5,
                    EnableCallback = true,
                    DefaultModel = "gemini-2.5-flash",
                    Temperature = 0.3m,
                    ThinkingBudget = 0,
                    UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
        }
    }
}