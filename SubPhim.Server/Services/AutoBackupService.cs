using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Pages.Admin;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Background service tự động backup database và gửi email định kỳ
    /// </summary>
    public class AutoBackupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoBackupService> _logger;
        
        // Check interval - kiểm tra mỗi 30 phút
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

        public AutoBackupService(
            IServiceProvider serviceProvider,
            ILogger<AutoBackupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AutoBackupService started");

            // Delay khởi động 2 phút để đợi server sẵn sàng
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndPerformBackupAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AutoBackupService check cycle");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _logger.LogInformation("AutoBackupService stopped");
        }

        private async Task CheckAndPerformBackupAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Lấy settings
            var settings = await context.BackupSettings.FindAsync(1);
            if (settings == null)
            {
                // Tạo default settings
                settings = new BackupSetting { Id = 1 };
                context.BackupSettings.Add(settings);
                await context.SaveChangesAsync(stoppingToken);
            }

            if (!settings.AutoBackupEnabled)
            {
                _logger.LogDebug("Auto backup is disabled");
                return;
            }

            if (string.IsNullOrEmpty(settings.NotificationEmail))
            {
                _logger.LogWarning("Auto backup enabled but no notification email configured");
                return;
            }

            // Kiểm tra xem đã đến lúc backup chưa
            var backupInterval = TimeSpan.FromHours(settings.BackupIntervalHours);
            var nextBackupTime = (settings.LastBackupUtc ?? DateTime.MinValue) + backupInterval;

            if (DateTime.UtcNow < nextBackupTime)
            {
                var timeUntilBackup = nextBackupTime - DateTime.UtcNow;
                _logger.LogDebug("Next backup in {TimeUntilBackup:g}", timeUntilBackup);
                return;
            }

            _logger.LogInformation("Starting scheduled auto backup...");

            try
            {
                // Tạo backup data
                var backupData = await CreateBackupDataAsync(context, stoppingToken);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve
                };

                var jsonString = JsonSerializer.Serialize(backupData, jsonOptions);
                var jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                var fileName = $"subphim_auto_backup_{DateTime.UtcNow:yyyyMMdd_HHmm}.json";

                // Gửi email
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await SendBackupEmailAsync(emailService, settings.NotificationEmail, jsonBytes, fileName, backupData);

                // Cập nhật thời gian backup
                settings.LastBackupUtc = DateTime.UtcNow;
                settings.LastEmailSentUtc = DateTime.UtcNow;
                await context.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Auto backup completed successfully. Email sent to {Email}", settings.NotificationEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform auto backup");
            }
        }

        private async Task<ComprehensiveBackupModel> CreateBackupDataAsync(AppDbContext context, CancellationToken stoppingToken)
        {
            return new ComprehensiveBackupModel
            {
                // Users và Devices
                Users = await context.Users.AsNoTracking().ToListAsync(stoppingToken),
                Devices = await context.Devices.AsNoTracking().ToListAsync(stoppingToken),
                BannedDevices = await context.BannedDevices.AsNoTracking().ToListAsync(stoppingToken),
                
                // API Keys
                ManagedApiKeys = await context.ManagedApiKeys.AsNoTracking().ToListAsync(stoppingToken),
                TtsApiKeys = await context.TtsApiKeys.AsNoTracking().ToListAsync(stoppingToken),
                AioApiKeys = await context.AioApiKeys.AsNoTracking().ToListAsync(stoppingToken),
                AioTtsServiceAccounts = await context.AioTtsServiceAccounts.AsNoTracking().ToListAsync(stoppingToken),
                VipApiKeys = await context.VipApiKeys.AsNoTracking().ToListAsync(stoppingToken),
                
                // Proxies (bao gồm latency)
                Proxies = await context.Proxies.AsNoTracking().ToListAsync(stoppingToken),
                
                // Cấu hình hệ thống
                TierDefaultSettings = await context.TierDefaultSettings.AsNoTracking().ToListAsync(stoppingToken),
                LocalApiSettings = await context.LocalApiSettings.AsNoTracking().ToListAsync(stoppingToken),
                AvailableApiModels = await context.AvailableApiModels.AsNoTracking().ToListAsync(stoppingToken),
                AioTranslationSettings = await context.AioTranslationSettings.AsNoTracking().ToListAsync(stoppingToken),
                TranslationGenres = await context.TranslationGenres.AsNoTracking().ToListAsync(stoppingToken),
                TtsModelSettings = await context.TtsModelSettings.AsNoTracking().ToListAsync(stoppingToken),
                UpdateInfos = await context.UpdateInfos.AsNoTracking().ToListAsync(stoppingToken),
                VipTranslationSettings = await context.VipTranslationSettings.AsNoTracking().ToListAsync(stoppingToken),
                VipAvailableApiModels = await context.VipAvailableApiModels.AsNoTracking().ToListAsync(stoppingToken),
                
                // Vbee TTS
                VbeeVoices = await context.VbeeVoices.AsNoTracking().ToListAsync(stoppingToken),
                VbeeTtsSettings = await context.VbeeTtsSettings.AsNoTracking().ToListAsync(stoppingToken),
                VbeeFakeEndpoints = await context.VbeeFakeEndpoints.AsNoTracking().ToListAsync(stoppingToken),
                
                // Backup settings
                BackupSettings = await context.BackupSettings.AsNoTracking().ToListAsync(stoppingToken),
                
                // Metadata
                BackupVersion = "2.0",
                BackupCreatedAt = DateTime.UtcNow
            };
        }

        private async Task SendBackupEmailAsync(
            IEmailService emailService, 
            string toEmail, 
            byte[] backupData,
            string fileName,
            ComprehensiveBackupModel backup)
        {
            var serverName = Environment.GetEnvironmentVariable("FLY_APP_NAME") ?? "SubPhim Server (Local)";
            
            var subject = $"[SubPhim] Auto Backup - {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            
            var body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2 style='color: #0d6efd;'>📦 SubPhim Auto Backup Report</h2>
    <p>Đây là email tự động từ hệ thống backup định kỳ của <strong>{serverName}</strong>.</p>
    
    <h3>📊 Thống kê backup:</h3>
    <table style='border-collapse: collapse; width: 100%; max-width: 500px;'>
        <tr><td style='padding: 8px; border: 1px solid #ddd;'>👥 Users</td><td style='padding: 8px; border: 1px solid #ddd;'><strong>{backup.Users?.Count ?? 0}</strong></td></tr>
        <tr><td style='padding: 8px; border: 1px solid #ddd;'>🌐 Proxies</td><td style='padding: 8px; border: 1px solid #ddd;'><strong>{backup.Proxies?.Count ?? 0}</strong></td></tr>
        <tr><td style='padding: 8px; border: 1px solid #ddd;'>🔑 API Keys (Local)</td><td style='padding: 8px; border: 1px solid #ddd;'><strong>{backup.ManagedApiKeys?.Count ?? 0}</strong></td></tr>
        <tr><td style='padding: 8px; border: 1px solid #ddd;'>🎤 Vbee Voices</td><td style='padding: 8px; border: 1px solid #ddd;'><strong>{backup.VbeeVoices?.Count ?? 0}</strong></td></tr>
        <tr><td style='padding: 8px; border: 1px solid #ddd;'>📄 File Size</td><td style='padding: 8px; border: 1px solid #ddd;'><strong>{backupData.Length / 1024.0:F1} KB</strong></td></tr>
    </table>
    
    <h3>📎 File đính kèm:</h3>
    <p>File backup <code>{fileName}</code> được đính kèm trong email này.</p>
    
    <hr style='margin: 20px 0;'>
    <p style='color: #6c757d; font-size: 12px;'>
        Backup time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC<br>
        Backup version: {backup.BackupVersion}
    </p>
</body>
</html>";

            await emailService.SendEmailWithAttachmentAsync(
                toEmail, 
                subject, 
                body,
                backupData,
                fileName,
                "application/json");
        }
    }
}
