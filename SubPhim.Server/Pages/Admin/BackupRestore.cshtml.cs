// VỊ TRÍ: Pages/Admin/BackupRestore.cshtml.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubPhim.Server.Pages.Admin
{
    /// <summary>
    /// Model chứa TOÀN BỘ dữ liệu quan trọng của server để backup.
    /// Bao gồm: Users, Devices, API Keys, Proxies (với latency), Vbee TTS settings, Cấu hình hệ thống
    /// </summary>
    public class ComprehensiveBackupModel
    {
        // === DỮ LIỆU NGƯỜI DÙNG ===
        public List<User> Users { get; set; } = new();
        public List<Device> Devices { get; set; } = new();
        public List<BannedDevice> BannedDevices { get; set; } = new();

        // === API KEYS ===
        public List<ManagedApiKey> ManagedApiKeys { get; set; } = new(); // Local API
        public List<TtsApiKey> TtsApiKeys { get; set; } = new();
        public List<AioApiKey> AioApiKeys { get; set; } = new();
        public List<AioTtsServiceAccount> AioTtsServiceAccounts { get; set; } = new();
        public List<VipApiKey> VipApiKeys { get; set; } = new(); // VIP Translation keys

        // === PROXIES (BAO GỒM LATENCY) ===
        public List<Proxy> Proxies { get; set; } = new();

        // === CẤU HÌNH HỆ THỐNG ===
        public List<TierDefaultSetting> TierDefaultSettings { get; set; } = new();
        public List<LocalApiSetting> LocalApiSettings { get; set; } = new();
        public List<AvailableApiModel> AvailableApiModels { get; set; } = new();
        public List<AioTranslationSetting> AioTranslationSettings { get; set; } = new();
        public List<TranslationGenre> TranslationGenres { get; set; } = new();
        public List<TtsModelSetting> TtsModelSettings { get; set; } = new();
        public List<UpdateInfo> UpdateInfos { get; set; } = new();
        public List<VipTranslationSetting> VipTranslationSettings { get; set; } = new();
        public List<VipAvailableApiModel> VipAvailableApiModels { get; set; } = new();

        // === VBEE TTS ===
        public List<VbeeVoice> VbeeVoices { get; set; } = new();
        public List<VbeeTtsSetting> VbeeTtsSettings { get; set; } = new();
        public List<VbeeFakeEndpoint> VbeeFakeEndpoints { get; set; } = new();

        // === BACKUP SETTINGS ===
        public List<BackupSetting> BackupSettings { get; set; } = new();
        
        // Metadata
        public string BackupVersion { get; set; } = "2.0";
        public DateTime BackupCreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BackupRestoreModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BackupRestoreModel> _logger;

        public BackupRestoreModel(AppDbContext context, ILogger<BackupRestoreModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        // Hiển thị settings
        public BackupSetting BackupSettings { get; set; }
        public DateTime? NextBackupTime { get; set; }

        public async Task OnGetAsync()
        {
            BackupSettings = await _context.BackupSettings.FindAsync(1) ?? new BackupSetting();
            
            if (BackupSettings.LastBackupUtc.HasValue && BackupSettings.AutoBackupEnabled)
            {
                NextBackupTime = BackupSettings.LastBackupUtc.Value.AddHours(BackupSettings.BackupIntervalHours);
            }
        }

        /// <summary>
        /// Cập nhật cài đặt auto backup
        /// </summary>
        public async Task<IActionResult> OnPostUpdateSettingsAsync(
            bool autoBackupEnabled,
            int backupIntervalHours,
            string notificationEmail)
        {
            var settings = await _context.BackupSettings.FindAsync(1);
            if (settings == null)
            {
                settings = new BackupSetting { Id = 1 };
                _context.BackupSettings.Add(settings);
            }

            settings.AutoBackupEnabled = autoBackupEnabled;
            settings.BackupIntervalHours = backupIntervalHours > 0 ? backupIntervalHours : 12;
            settings.NotificationEmail = notificationEmail;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            SuccessMessage = "Đã cập nhật cài đặt Auto Backup thành công!";
            return RedirectToPage();
        }

        /// <summary>
        /// Tải xuống file backup toàn bộ
        /// </summary>
        public async Task<IActionResult> OnGetDownloadBackupAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu quá trình tạo backup toàn diện...");

                var backupData = await CreateBackupDataAsync();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve
                };

                var jsonString = JsonSerializer.Serialize(backupData, jsonOptions);
                var fileName = $"subphim_full_backup_{DateTime.Now:yyyyMMdd_HHmm}.json";

                _logger.LogInformation("Tạo file backup '{FileName}' thành công.", fileName);
                return File(System.Text.Encoding.UTF8.GetBytes(jsonString), "application/json", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo file backup toàn diện.");
                ErrorMessage = "Đã có lỗi xảy ra trong quá trình tạo file backup. Vui lòng kiểm tra log.";
                return RedirectToPage();
            }
        }

        /// <summary>
        /// Tạo backup data object
        /// </summary>
        public async Task<ComprehensiveBackupModel> CreateBackupDataAsync()
        {
            return new ComprehensiveBackupModel
            {
                // Users và Devices
                Users = await _context.Users.AsNoTracking().ToListAsync(),
                Devices = await _context.Devices.AsNoTracking().ToListAsync(),
                BannedDevices = await _context.BannedDevices.AsNoTracking().ToListAsync(),
                
                // API Keys
                ManagedApiKeys = await _context.ManagedApiKeys.AsNoTracking().ToListAsync(),
                TtsApiKeys = await _context.TtsApiKeys.AsNoTracking().ToListAsync(),
                AioApiKeys = await _context.AioApiKeys.AsNoTracking().ToListAsync(),
                AioTtsServiceAccounts = await _context.AioTtsServiceAccounts.AsNoTracking().ToListAsync(),
                VipApiKeys = await _context.VipApiKeys.AsNoTracking().ToListAsync(),
                
                // Proxies (ĐÃ BAO GỒM LatencyMs, LastLatencyCheckUtc, LatencyCheckStatus)
                Proxies = await _context.Proxies.AsNoTracking().ToListAsync(),
                
                // Cấu hình hệ thống
                TierDefaultSettings = await _context.TierDefaultSettings.AsNoTracking().ToListAsync(),
                LocalApiSettings = await _context.LocalApiSettings.AsNoTracking().ToListAsync(),
                AvailableApiModels = await _context.AvailableApiModels.AsNoTracking().ToListAsync(),
                AioTranslationSettings = await _context.AioTranslationSettings.AsNoTracking().ToListAsync(),
                TranslationGenres = await _context.TranslationGenres.AsNoTracking().ToListAsync(),
                TtsModelSettings = await _context.TtsModelSettings.AsNoTracking().ToListAsync(),
                UpdateInfos = await _context.UpdateInfos.AsNoTracking().ToListAsync(),
                VipTranslationSettings = await _context.VipTranslationSettings.AsNoTracking().ToListAsync(),
                VipAvailableApiModels = await _context.VipAvailableApiModels.AsNoTracking().ToListAsync(),
                
                // Vbee TTS
                VbeeVoices = await _context.VbeeVoices.AsNoTracking().ToListAsync(),
                VbeeTtsSettings = await _context.VbeeTtsSettings.AsNoTracking().ToListAsync(),
                VbeeFakeEndpoints = await _context.VbeeFakeEndpoints.AsNoTracking().ToListAsync(),
                
                // Backup settings
                BackupSettings = await _context.BackupSettings.AsNoTracking().ToListAsync(),
                
                // Metadata
                BackupVersion = "2.0",
                BackupCreatedAt = DateTime.UtcNow
            };
        }

        public async Task<IActionResult> OnPostImportAsync(IFormFile backupFile)
        {
            if (backupFile == null || backupFile.Length == 0)
            {
                ErrorMessage = "Vui lòng chọn một file backup để tải lên.";
                return Page();
            }

            if (!backupFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "File không hợp lệ. Chỉ chấp nhận file .json.";
                return Page();
            }

            ComprehensiveBackupModel backupData;
            try
            {
                using var streamReader = new StreamReader(backupFile.OpenReadStream());
                var jsonString = await streamReader.ReadToEndAsync();
                var jsonOptions = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
                backupData = JsonSerializer.Deserialize<ComprehensiveBackupModel>(jsonString, jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc hoặc deserialize file JSON backup.");
                ErrorMessage = "File backup không thể đọc được hoặc cấu trúc JSON không hợp lệ. Lỗi: " + ex.Message;
                return Page();
            }

            if (backupData == null)
            {
                ErrorMessage = "Dữ liệu backup không hợp lệ.";
                return Page();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _logger.LogWarning("Bắt đầu quá trình KHÔI PHỤC DỮ LIỆU. Dữ liệu hiện tại sẽ bị XÓA và THAY THẾ.");

                // XÓA DỮ LIỆU CŨ theo thứ tự để không vi phạm khóa ngoại
                await _context.Devices.ExecuteDeleteAsync();
                await _context.Users.ExecuteDeleteAsync();
                await _context.BannedDevices.ExecuteDeleteAsync();
                await _context.ManagedApiKeys.ExecuteDeleteAsync();
                await _context.TtsApiKeys.ExecuteDeleteAsync();
                await _context.AioApiKeys.ExecuteDeleteAsync();
                await _context.AioTtsServiceAccounts.ExecuteDeleteAsync();
                await _context.VipApiKeys.ExecuteDeleteAsync();
                await _context.Proxies.ExecuteDeleteAsync();
                await _context.TierDefaultSettings.ExecuteDeleteAsync();
                await _context.LocalApiSettings.ExecuteDeleteAsync();
                await _context.AvailableApiModels.ExecuteDeleteAsync();
                await _context.AioTranslationSettings.ExecuteDeleteAsync();
                await _context.TranslationGenres.ExecuteDeleteAsync();
                await _context.TtsModelSettings.ExecuteDeleteAsync();
                await _context.UpdateInfos.ExecuteDeleteAsync();
                await _context.VipTranslationSettings.ExecuteDeleteAsync();
                await _context.VipAvailableApiModels.ExecuteDeleteAsync();
                await _context.VbeeVoices.ExecuteDeleteAsync();
                await _context.VbeeTtsSettings.ExecuteDeleteAsync();
                await _context.VbeeFakeEndpoints.ExecuteDeleteAsync();
                await _context.BackupSettings.ExecuteDeleteAsync();
                
                _logger.LogInformation("Đã xóa dữ liệu cũ từ các bảng.");

                // THÊM DỮ LIỆU MỚI TỪ FILE BACKUP
                if (backupData.Users?.Any() ?? false) _context.Users.AddRange(backupData.Users);
                if (backupData.Devices?.Any() ?? false) _context.Devices.AddRange(backupData.Devices);
                if (backupData.BannedDevices?.Any() ?? false) _context.BannedDevices.AddRange(backupData.BannedDevices);
                if (backupData.ManagedApiKeys?.Any() ?? false) _context.ManagedApiKeys.AddRange(backupData.ManagedApiKeys);
                if (backupData.TtsApiKeys?.Any() ?? false) _context.TtsApiKeys.AddRange(backupData.TtsApiKeys);
                if (backupData.AioApiKeys?.Any() ?? false) _context.AioApiKeys.AddRange(backupData.AioApiKeys);
                if (backupData.AioTtsServiceAccounts?.Any() ?? false) _context.AioTtsServiceAccounts.AddRange(backupData.AioTtsServiceAccounts);
                if (backupData.VipApiKeys?.Any() ?? false) _context.VipApiKeys.AddRange(backupData.VipApiKeys);
                if (backupData.Proxies?.Any() ?? false) _context.Proxies.AddRange(backupData.Proxies);
                if (backupData.TierDefaultSettings?.Any() ?? false) _context.TierDefaultSettings.AddRange(backupData.TierDefaultSettings);
                if (backupData.LocalApiSettings?.Any() ?? false) _context.LocalApiSettings.AddRange(backupData.LocalApiSettings);
                if (backupData.AvailableApiModels?.Any() ?? false) _context.AvailableApiModels.AddRange(backupData.AvailableApiModels);
                if (backupData.AioTranslationSettings?.Any() ?? false) _context.AioTranslationSettings.AddRange(backupData.AioTranslationSettings);
                if (backupData.TranslationGenres?.Any() ?? false) _context.TranslationGenres.AddRange(backupData.TranslationGenres);
                if (backupData.TtsModelSettings?.Any() ?? false) _context.TtsModelSettings.AddRange(backupData.TtsModelSettings);
                if (backupData.UpdateInfos?.Any() ?? false) _context.UpdateInfos.AddRange(backupData.UpdateInfos);
                if (backupData.VipTranslationSettings?.Any() ?? false) _context.VipTranslationSettings.AddRange(backupData.VipTranslationSettings);
                if (backupData.VipAvailableApiModels?.Any() ?? false) _context.VipAvailableApiModels.AddRange(backupData.VipAvailableApiModels);
                if (backupData.VbeeVoices?.Any() ?? false) _context.VbeeVoices.AddRange(backupData.VbeeVoices);
                if (backupData.VbeeTtsSettings?.Any() ?? false) _context.VbeeTtsSettings.AddRange(backupData.VbeeTtsSettings);
                if (backupData.VbeeFakeEndpoints?.Any() ?? false) _context.VbeeFakeEndpoints.AddRange(backupData.VbeeFakeEndpoints);
                if (backupData.BackupSettings?.Any() ?? false) _context.BackupSettings.AddRange(backupData.BackupSettings);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Khôi phục dữ liệu toàn diện thành công.");
                SuccessMessage = $"Khôi phục thành công! Đã import dữ liệu cho {backupData.Users?.Count ?? 0} người dùng, {backupData.Proxies?.Count ?? 0} proxy, và các cấu hình liên quan.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi nghiêm trọng khi import dữ liệu, đã rollback transaction.");
                ErrorMessage = "Đã có lỗi xảy ra trong quá trình import, mọi thay đổi đã được hoàn tác. Chi tiết: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}