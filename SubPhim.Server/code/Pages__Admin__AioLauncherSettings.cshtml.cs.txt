using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin
{
    public class AioLauncherSettingsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;

        public AioLauncherSettingsModel(AppDbContext context, IEncryptionService encryptionService)
        {
            _context = context;
            _encryptionService = encryptionService;
        }

        [BindProperty]
        public AioTranslationSetting Settings { get; set; }
        public List<TranslationGenre> Genres { get; set; }
        public List<ApiKeyViewModel> ApiKeys { get; set; }

        [BindProperty]
        public GenreInputModel NewGenre { get; set; }
        [BindProperty]
        public ApiKeyInputModel NewApiKeys { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public record ApiKeyViewModel(int Id, string MaskedKey, bool IsEnabled, string DisabledReason, int RequestsToday);
        public class GenreInputModel
        {
            [Required(ErrorMessage = "Tên thể loại là bắt buộc.")]
            public string GenreName { get; set; }
            [Required(ErrorMessage = "Prompt là bắt buộc.")]
            public string SystemInstruction { get; set; }
        }
        public class ApiKeyInputModel
        {
            [Required(ErrorMessage = "Vui lòng nhập ít nhất một API key.")]
            public string ApiKeys { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteSelectedAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một API key để xóa.";
                return RedirectToPage();
            }

            var keysToDelete = await _context.AioApiKeys
                .Where(k => selectedKeyIds.Contains(k.Id))
                .ToListAsync();

            if (keysToDelete.Any())
            {
                _context.AioApiKeys.RemoveRange(keysToDelete);
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã xóa thành công {keysToDelete.Count} API key đã chọn.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleSelectedAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một API key để thay đổi trạng thái.";
                return RedirectToPage();
            }

            var keysToToggle = await _context.AioApiKeys
                .Where(k => selectedKeyIds.Contains(k.Id))
                .ToListAsync();

            if (keysToToggle.Any())
            {
                foreach (var key in keysToToggle)
                {
                    key.IsEnabled = !key.IsEnabled;
                    if (key.IsEnabled)
                    {
                        key.DisabledReason = null; // Xóa lý do khi bật lại
                    }
                }
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã thay đổi trạng thái của {keysToToggle.Count} API key đã chọn.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnGetAsync()
        {
            Settings = await _context.AioTranslationSettings.FindAsync(1) ?? new AioTranslationSetting { Id = 1 };
            Genres = await _context.TranslationGenres.OrderBy(g => g.GenreName).ToListAsync();

            var keysFromDb = await _context.AioApiKeys.ToListAsync();
            ApiKeys = keysFromDb.Select(k => new ApiKeyViewModel(
                k.Id,
                MaskAndDecrypt(k.EncryptedApiKey, k.Iv),
                k.IsEnabled,
                k.DisabledReason,
                k.RequestsToday // Thêm dòng này
            )).ToList();

            return Page();
        }
        public async Task<IActionResult> OnPostSaveSettingsAsync()
        {
            var settingsInDb = await _context.AioTranslationSettings.FindAsync(1);
            if (settingsInDb == null)
            {
                Settings.Id = 1;
                _context.AioTranslationSettings.Add(Settings);
                SuccessMessage = "Đã tạo và lưu cài đặt dịch thuật thành công!";
            }
            else
            {
                settingsInDb.DefaultModelName = Settings.DefaultModelName;
                settingsInDb.Temperature = Settings.Temperature;
                settingsInDb.MaxOutputTokens = Settings.MaxOutputTokens;
                settingsInDb.RpmPerKey = Settings.RpmPerKey;
                settingsInDb.RpdPerKey = Settings.RpdPerKey;
                settingsInDb.RpmPerProxy = Settings.RpmPerProxy;
                settingsInDb.RpdPerProxy = Settings.RpdPerProxy;
                settingsInDb.EnableThinkingBudget = Settings.EnableThinkingBudget;
                settingsInDb.ThinkingBudget = Settings.ThinkingBudget;
                settingsInDb.MaxApiRetries = Settings.MaxApiRetries;
                settingsInDb.RetryApiDelayMs = Settings.RetryApiDelayMs;
                settingsInDb.DelayBetweenFilesMs = Settings.DelayBetweenFilesMs;
                settingsInDb.DelayBetweenChunksMs = Settings.DelayBetweenChunksMs;
                settingsInDb.DirectSendThreshold = Settings.DirectSendThreshold;
                settingsInDb.ChunkSize = Settings.ChunkSize;

                SuccessMessage = "Đã cập nhật cài đặt dịch thuật thành công!";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage();
        }
        private string MaskAndDecrypt(string encrypted, string iv)
        {
            try
            {
                var decrypted = _encryptionService.Decrypt(encrypted, iv);
                return decrypted.Length > 8 ? $"{decrypted.Substring(0, 4)}...{decrypted.Substring(decrypted.Length - 4)}" : decrypted;
            }
            catch { return "!!! LỖI GIẢI MÃ !!!"; }
        }
        public async Task<IActionResult> OnPostAddGenreAsync()
        {
            // Bỏ hoàn toàn việc kiểm tra ModelState.IsValid.
            // Kiểm tra trực tiếp xem 2 ô input có trống hay không, giống hệt logic của form API Key.
            if (NewGenre == null || string.IsNullOrWhiteSpace(NewGenre.GenreName) || string.IsNullOrWhiteSpace(NewGenre.SystemInstruction))
            {
                ErrorMessage = "Vui lòng điền đầy đủ thông tin cho thể loại mới. Cả Tên và Prompt đều là bắt buộc.";
                await OnGetAsync(); // Tải lại dữ liệu cho trang để không bị lỗi
                return Page();
            }

            // Nếu không trống, tiến hành thêm vào DB
            var existing = await _context.TranslationGenres.FirstOrDefaultAsync(g => g.GenreName.ToLower() == NewGenre.GenreName.ToLower());
            if (existing != null)
            {
                ErrorMessage = $"Thể loại '{NewGenre.GenreName}' đã tồn tại.";
                await OnGetAsync();
                return Page();
            }

            var genre = new TranslationGenre
            {
                GenreName = NewGenre.GenreName.Trim(),
                SystemInstruction = NewGenre.SystemInstruction,
                IsActive = true
            };
            _context.TranslationGenres.Add(genre);
            await _context.SaveChangesAsync();

            SuccessMessage = "Đã thêm thể loại mới thành công!";
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostDeleteGenreAsync(int id)
        {
            var genre = await _context.TranslationGenres.FindAsync(id);
            if (genre != null)
            {
                _context.TranslationGenres.Remove(genre);
                await _context.SaveChangesAsync();
                SuccessMessage = "Đã xóa thể loại thành công.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddApiKeysAsync()
        {
            if (string.IsNullOrWhiteSpace(NewApiKeys?.ApiKeys))
            {
                ErrorMessage = "Vui lòng nhập API key.";
                return await OnGetAsync();
            }

            var keys = NewApiKeys.ApiKeys.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(k => k.Trim())
                                        .Where(k => !string.IsNullOrWhiteSpace(k));

            int addedCount = 0;
            foreach (var key in keys)
            {
                var (encrypted, iv) = _encryptionService.Encrypt(key);
                // Kiểm tra key đã tồn tại chưa để tránh trùng lặp
                if (!await _context.AioApiKeys.AnyAsync(k => k.EncryptedApiKey == encrypted))
                {
                    _context.AioApiKeys.Add(new AioApiKey { EncryptedApiKey = encrypted, Iv = iv });
                    addedCount++;
                }
            }

            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm thành công {addedCount} API key mới.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteApiKeyAsync(int id)
        {
            var key = await _context.AioApiKeys.FindAsync(id);
            if (key != null)
            {
                _context.AioApiKeys.Remove(key);
                await _context.SaveChangesAsync();
                SuccessMessage = "Đã xóa API key.";
            }
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostToggleApiKeyAsync(int id)
        {
            var key = await _context.AioApiKeys.FindAsync(id);
            if (key != null)
            {
                key.IsEnabled = !key.IsEnabled;
                if (key.IsEnabled) key.DisabledReason = null; // Xóa lý do khi bật lại
                await _context.SaveChangesAsync();
                SuccessMessage = "Đã thay đổi trạng thái API key.";
            }
            return RedirectToPage();
        }
    }
}