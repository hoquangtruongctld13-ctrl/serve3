// VỊ TRÍ: Pages/Admin/BackupRestore.cshtml.cs
// THAY THẾ TOÀN BỘ FILE

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
    /// </summary>
    public class ComprehensiveBackupModel
    {
        // Dữ liệu người dùng và thiết bị
        public List<User> Users { get; set; }
        public List<Device> Devices { get; set; }
        public List<BannedDevice> BannedDevices { get; set; }

        // Toàn bộ các loại API Keys
        public List<ManagedApiKey> ManagedApiKeys { get; set; } // Local API
        public List<TtsApiKey> TtsApiKeys { get; set; }
        public List<AioApiKey> AioApiKeys { get; set; }
        public List<AioTtsServiceAccount> AioTtsServiceAccounts { get; set; }

        // Toàn bộ các bảng Cấu hình
        public List<TierDefaultSetting> TierDefaultSettings { get; set; }
        public List<LocalApiSetting> LocalApiSettings { get; set; }
        public List<AvailableApiModel> AvailableApiModels { get; set; }
        public List<AioTranslationSetting> AioTranslationSettings { get; set; }
        public List<TranslationGenre> TranslationGenres { get; set; }
        public List<TtsModelSetting> TtsModelSettings { get; set; }
        public List<UpdateInfo> UpdateInfos { get; set; }
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

        public void OnGet() { }

        public async Task<IActionResult> OnGetDownloadBackupAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu quá trình tạo backup toàn diện...");

                var backupData = new ComprehensiveBackupModel
                {
                    // Lấy dữ liệu từ tất cả các bảng cần thiết
                    Users = await _context.Users.AsNoTracking().ToListAsync(),
                    Devices = await _context.Devices.AsNoTracking().ToListAsync(),
                    BannedDevices = await _context.BannedDevices.AsNoTracking().ToListAsync(),
                    ManagedApiKeys = await _context.ManagedApiKeys.AsNoTracking().ToListAsync(),
                    TtsApiKeys = await _context.TtsApiKeys.AsNoTracking().ToListAsync(),
                    AioApiKeys = await _context.AioApiKeys.AsNoTracking().ToListAsync(),
                    AioTtsServiceAccounts = await _context.AioTtsServiceAccounts.AsNoTracking().ToListAsync(),
                    TierDefaultSettings = await _context.TierDefaultSettings.AsNoTracking().ToListAsync(),
                    LocalApiSettings = await _context.LocalApiSettings.AsNoTracking().ToListAsync(),
                    AvailableApiModels = await _context.AvailableApiModels.AsNoTracking().ToListAsync(),
                    AioTranslationSettings = await _context.AioTranslationSettings.AsNoTracking().ToListAsync(),
                    TranslationGenres = await _context.TranslationGenres.AsNoTracking().ToListAsync(),
                    TtsModelSettings = await _context.TtsModelSettings.AsNoTracking().ToListAsync(),
                    UpdateInfos = await _context.UpdateInfos.AsNoTracking().ToListAsync()
                };

                // Cấu hình serializer để xử lý các mối quan hệ (ví dụ: User -> Devices)
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve // Rất quan trọng để tránh lỗi lặp vô hạn
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

            // Bắt đầu một transaction để đảm bảo an toàn dữ liệu
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _logger.LogWarning("Bắt đầu quá trình KHÔI PHỤC DỮ LIỆU. Dữ liệu hiện tại sẽ bị XÓA và THAY THẾ.");

                // XÓA DỮ LIỆU CŨ theo thứ tự để không vi phạm khóa ngoại
                // Bảng con -> bảng cha
                await _context.Devices.ExecuteDeleteAsync();
                await _context.Users.ExecuteDeleteAsync();
                await _context.BannedDevices.ExecuteDeleteAsync();
                await _context.ManagedApiKeys.ExecuteDeleteAsync();
                await _context.TtsApiKeys.ExecuteDeleteAsync();
                await _context.AioApiKeys.ExecuteDeleteAsync();
                await _context.AioTtsServiceAccounts.ExecuteDeleteAsync();
                await _context.TierDefaultSettings.ExecuteDeleteAsync();
                await _context.LocalApiSettings.ExecuteDeleteAsync();
                await _context.AvailableApiModels.ExecuteDeleteAsync();
                await _context.AioTranslationSettings.ExecuteDeleteAsync();
                await _context.TranslationGenres.ExecuteDeleteAsync();
                await _context.TtsModelSettings.ExecuteDeleteAsync();
                await _context.UpdateInfos.ExecuteDeleteAsync();
                _logger.LogInformation("Đã xóa dữ liệu cũ từ các bảng.");

                // THÊM DỮ LIỆU MỚI TỪ FILE BACKUP
                // Dùng AddRange để EF Core xử lý việc chèn hàng loạt hiệu quả
                if (backupData.Users?.Any() ?? false) _context.Users.AddRange(backupData.Users);
                if (backupData.Devices?.Any() ?? false) _context.Devices.AddRange(backupData.Devices);
                if (backupData.BannedDevices?.Any() ?? false) _context.BannedDevices.AddRange(backupData.BannedDevices);
                if (backupData.ManagedApiKeys?.Any() ?? false) _context.ManagedApiKeys.AddRange(backupData.ManagedApiKeys);
                if (backupData.TtsApiKeys?.Any() ?? false) _context.TtsApiKeys.AddRange(backupData.TtsApiKeys);
                if (backupData.AioApiKeys?.Any() ?? false) _context.AioApiKeys.AddRange(backupData.AioApiKeys);
                if (backupData.AioTtsServiceAccounts?.Any() ?? false) _context.AioTtsServiceAccounts.AddRange(backupData.AioTtsServiceAccounts);
                if (backupData.TierDefaultSettings?.Any() ?? false) _context.TierDefaultSettings.AddRange(backupData.TierDefaultSettings);
                if (backupData.LocalApiSettings?.Any() ?? false) _context.LocalApiSettings.AddRange(backupData.LocalApiSettings);
                if (backupData.AvailableApiModels?.Any() ?? false) _context.AvailableApiModels.AddRange(backupData.AvailableApiModels);
                if (backupData.AioTranslationSettings?.Any() ?? false) _context.AioTranslationSettings.AddRange(backupData.AioTranslationSettings);
                if (backupData.TranslationGenres?.Any() ?? false) _context.TranslationGenres.AddRange(backupData.TranslationGenres);
                if (backupData.TtsModelSettings?.Any() ?? false) _context.TtsModelSettings.AddRange(backupData.TtsModelSettings);
                if (backupData.UpdateInfos?.Any() ?? false) _context.UpdateInfos.AddRange(backupData.UpdateInfos);

                // Lưu tất cả các thay đổi vào DB
                await _context.SaveChangesAsync();

                // Hoàn tất transaction
                await transaction.CommitAsync();

                _logger.LogInformation("Khôi phục dữ liệu toàn diện thành công.");
                SuccessMessage = $"Khôi phục thành công! Đã import dữ liệu cho {backupData.Users?.Count ?? 0} người dùng và các cấu hình liên quan.";
            }
            catch (Exception ex)
            {
                // Nếu có lỗi, hoàn tác tất cả thay đổi
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi nghiêm trọng khi import dữ liệu, đã rollback transaction.");
                ErrorMessage = "Đã có lỗi xảy ra trong quá trình import, mọi thay đổi đã được hoàn tác. Chi tiết: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}