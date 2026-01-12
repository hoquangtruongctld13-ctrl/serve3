using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin.SubtitleApi
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(AppDbContext context, IEncryptionService encryptionService, ILogger<IndexModel> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _logger = logger;
        }

        // Data for display
        public List<ApiKeyViewModel> ApiKeys { get; set; } = new();
        public List<SubtitleTranslationServer> Servers { get; set; } = new();
        public List<SubtitleTranslationJob> RecentJobs { get; set; } = new();

        [BindProperty]
        public SettingsInputModel Settings { get; set; }

        [TempData] public string SuccessMessage { get; set; }
        [TempData] public string ErrorMessage { get; set; }

        #region ViewModels
        public class ApiKeyViewModel
        {
            public SubtitleApiKey KeyData { get; set; }
            public string MaskedKey { get; set; }
        }

        public class SettingsInputModel
        {
            [Required][Range(10, 500)] public int LinesPerServer { get; set; }
            [Required][Range(5, 100)] public int BatchSizePerServer { get; set; }
            [Required][Range(1, 20)] public int ApiKeysPerServer { get; set; }
            [Required][Range(1, 50)] public int MergeBatchThreshold { get; set; }
            [Required][Range(30, 600)] public int ServerTimeoutSeconds { get; set; }
            [Required][Range(1, 10)] public int MaxServerRetries { get; set; }
            [Required][Range(0, 10000)] public int DelayBetweenServerBatchesMs { get; set; }
            [Required][Range(1, 60)] public int ApiKeyCooldownMinutes { get; set; }
            public bool EnableCallback { get; set; }
            [StringLength(100)] public string DefaultModel { get; set; }
            [Range(0.0, 2.0)] public decimal Temperature { get; set; }
            [Range(0, 24576)] public int ThinkingBudget { get; set; }
            [Url(ErrorMessage = "URL không hợp lệ.")]
            [StringLength(500)]
            [Display(Name = "URL Server Chính (Callback)")]
            public string? MainServerUrl { get; set; }
        }
        #endregion

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            // Load settings
            var settingsFromDb = await _context.SubtitleApiSettings.FindAsync(1);
            if (settingsFromDb == null)
            {
                settingsFromDb = new SubtitleApiSetting { Id = 1 };
                _context.SubtitleApiSettings.Add(settingsFromDb);
                await _context.SaveChangesAsync();
            }

            Settings = new SettingsInputModel
            {
                LinesPerServer = settingsFromDb.LinesPerServer,
                BatchSizePerServer = settingsFromDb.BatchSizePerServer,
                ApiKeysPerServer = settingsFromDb.ApiKeysPerServer,
                MergeBatchThreshold = settingsFromDb.MergeBatchThreshold,
                ServerTimeoutSeconds = settingsFromDb.ServerTimeoutSeconds,
                MaxServerRetries = settingsFromDb.MaxServerRetries,
                DelayBetweenServerBatchesMs = settingsFromDb.DelayBetweenServerBatchesMs,
                ApiKeyCooldownMinutes = settingsFromDb.ApiKeyCooldownMinutes,
                EnableCallback = settingsFromDb.EnableCallback,
                DefaultModel = settingsFromDb.DefaultModel,
                Temperature = settingsFromDb.Temperature,
                ThinkingBudget = settingsFromDb.ThinkingBudget,
                MainServerUrl = settingsFromDb.MainServerUrl
            };

            // Load API Keys
            var keysFromDb = await _context.SubtitleApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync();
            ApiKeys = new List<ApiKeyViewModel>();
            foreach (var key in keysFromDb)
            {
                try
                {
                    var decrypted = _encryptionService.Decrypt(key.EncryptedApiKey, key.Iv);
                    var masked = MaskApiKey(decrypted);
                    ApiKeys.Add(new ApiKeyViewModel { KeyData = key, MaskedKey = masked });
                }
                catch
                {
                    ApiKeys.Add(new ApiKeyViewModel { KeyData = key, MaskedKey = "!!! LỖI GIẢI MÃ !!!" });
                }
            }

            // Load Servers
            Servers = await _context.SubtitleTranslationServers.OrderBy(s => s.Priority).ThenBy(s => s.Id).ToListAsync();

            // Load Recent Jobs
            RecentJobs = await _context.SubtitleTranslationJobs
                .OrderByDescending(j => j.CreatedAt)
                .Take(20)
                .ToListAsync();
        }

        private string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 12)
                return apiKey;
            return apiKey.Substring(0, 8) + "****" + apiKey.Substring(apiKey.Length - 4);
        }

        #region Settings Handlers
        public async Task<IActionResult> OnPostUpdateSettingsAsync()
        {
            if (!TryValidateModel(Settings, nameof(Settings)))
            {
                ErrorMessage = "Dữ liệu không hợp lệ.";
                return RedirectToPage();
            }

            try
            {
                var settingsInDb = await _context.SubtitleApiSettings.FindAsync(1);
                if (settingsInDb == null)
                {
                    settingsInDb = new SubtitleApiSetting { Id = 1 };
                    _context.SubtitleApiSettings.Add(settingsInDb);
                }

                settingsInDb.LinesPerServer = Settings.LinesPerServer;
                settingsInDb.BatchSizePerServer = Settings.BatchSizePerServer;
                settingsInDb.ApiKeysPerServer = Settings.ApiKeysPerServer;
                settingsInDb.MergeBatchThreshold = Settings.MergeBatchThreshold;
                settingsInDb.ServerTimeoutSeconds = Settings.ServerTimeoutSeconds;
                settingsInDb.MaxServerRetries = Settings.MaxServerRetries;
                settingsInDb.DelayBetweenServerBatchesMs = Settings.DelayBetweenServerBatchesMs;
                settingsInDb.ApiKeyCooldownMinutes = Settings.ApiKeyCooldownMinutes;
                settingsInDb.EnableCallback = Settings.EnableCallback;
                settingsInDb.DefaultModel = Settings.DefaultModel;
                settingsInDb.Temperature = Settings.Temperature;
                settingsInDb.ThinkingBudget = Settings.ThinkingBudget;
                settingsInDb.MainServerUrl = string.IsNullOrWhiteSpace(Settings.MainServerUrl) ? null : Settings.MainServerUrl.Trim().TrimEnd('/');
                settingsInDb.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                SuccessMessage = "Đã lưu cài đặt thành công.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
                ErrorMessage = $"Lỗi: {ex.Message}";
            }

            return RedirectToPage();
        }
        #endregion

        #region API Key Handlers
        public async Task<IActionResult> OnPostAddApiKeysAsync([FromForm] string apiKeysInput)
        {
            if (string.IsNullOrWhiteSpace(apiKeysInput))
            {
                ErrorMessage = "Vui lòng nhập ít nhất một API key.";
                return RedirectToPage();
            }

            var keys = apiKeysInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int added = 0;

            foreach (var key in keys)
            {
                var trimmedKey = key.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedKey))
                {
                    var (encryptedText, iv) = _encryptionService.Encrypt(trimmedKey);
                    _context.SubtitleApiKeys.Add(new SubtitleApiKey
                    {
                        EncryptedApiKey = encryptedText,
                        Iv = iv,
                        IsEnabled = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    added++;
                }
            }

            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm {added} API key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một key.";
                return RedirectToPage();
            }

            var keysToDelete = await _context.SubtitleApiKeys.Where(k => selectedKeyIds.Contains(k.Id)).ToListAsync();
            _context.SubtitleApiKeys.RemoveRange(keysToDelete);
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã xóa {keysToDelete.Count} key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDisableSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một key.";
                return RedirectToPage();
            }

            var keys = await _context.SubtitleApiKeys.Where(k => selectedKeyIds.Contains(k.Id)).ToListAsync();
            foreach (var key in keys)
            {
                key.IsEnabled = false;
            }
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã tắt {keys.Count} key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEnableSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một key.";
                return RedirectToPage();
            }

            var keys = await _context.SubtitleApiKeys.Where(k => selectedKeyIds.Contains(k.Id)).ToListAsync();
            foreach (var key in keys)
            {
                key.IsEnabled = true;
                key.DisabledReason = null;
            }
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã bật {keys.Count} key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostClearCooldownAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một key.";
                return RedirectToPage();
            }

            var keys = await _context.SubtitleApiKeys.Where(k => selectedKeyIds.Contains(k.Id)).ToListAsync();
            foreach (var key in keys)
            {
                key.CooldownUntil = null;
                key.Consecutive429Count = 0;
            }
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã xóa cooldown cho {keys.Count} key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleKeyAsync(int id)
        {
            var key = await _context.SubtitleApiKeys.FindAsync(id);
            if (key != null)
            {
                key.IsEnabled = !key.IsEnabled;
                if (key.IsEnabled)
                {
                    key.DisabledReason = null;
                }
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }
        #endregion

        #region Server Handlers
        public async Task<IActionResult> OnPostAddServerAsync([FromForm] string serverUrl, [FromForm] int rpmLimit, [FromForm] string displayName)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                ErrorMessage = "Vui lòng nhập URL server.";
                return RedirectToPage();
            }

            serverUrl = serverUrl.Trim().TrimEnd('/');

            // Check duplicate
            if (await _context.SubtitleTranslationServers.AnyAsync(s => s.ServerUrl == serverUrl))
            {
                ErrorMessage = "Server này đã tồn tại.";
                return RedirectToPage();
            }

            _context.SubtitleTranslationServers.Add(new SubtitleTranslationServer
            {
                ServerUrl = serverUrl,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                RpmLimit = rpmLimit > 0 ? rpmLimit : 5,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            SuccessMessage = "Đã thêm server.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteSelectedServersAsync([FromForm] int[] selectedServerIds)
        {
            if (selectedServerIds == null || !selectedServerIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một server.";
                return RedirectToPage();
            }

            var serversToDelete = await _context.SubtitleTranslationServers.Where(s => selectedServerIds.Contains(s.Id)).ToListAsync();
            _context.SubtitleTranslationServers.RemoveRange(serversToDelete);
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã xóa {serversToDelete.Count} server.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleServerAsync(int id)
        {
            var server = await _context.SubtitleTranslationServers.FindAsync(id);
            if (server != null)
            {
                server.IsEnabled = !server.IsEnabled;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostTestSelectedServersAsync([FromForm] int[] selectedServerIds)
        {
            if (selectedServerIds == null || !selectedServerIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một server.";
                return RedirectToPage();
            }

            var servers = await _context.SubtitleTranslationServers.Where(s => selectedServerIds.Contains(s.Id)).ToListAsync();
            int successCount = 0;
            int failCount = 0;

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var server in servers)
            {
                try
                {
                    var response = await httpClient.GetAsync(server.ServerUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        successCount++;
                        server.LastUsedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        failCount++;
                        server.LastFailedAt = DateTime.UtcNow;
                        server.LastFailureReason = $"HTTP {(int)response.StatusCode}";
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    server.LastFailedAt = DateTime.UtcNow;
                    server.LastFailureReason = ex.Message;
                }
            }

            await _context.SaveChangesAsync();
            SuccessMessage = $"Test hoàn tất: {successCount} OK, {failCount} lỗi.";
            return RedirectToPage();
        }
        #endregion

        #region Job Handlers
        public async Task<IActionResult> OnPostCleanupJobsAsync()
        {
            var completedJobs = await _context.SubtitleTranslationJobs
                .Where(j => j.Status == SubtitleJobStatus.Completed || j.Status == SubtitleJobStatus.Failed || j.Status == SubtitleJobStatus.PartialCompleted)
                .ToListAsync();

            _context.SubtitleTranslationJobs.RemoveRange(completedJobs);
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã dọn dẹp {completedJobs.Count} job.";
            return RedirectToPage();
        }
        #endregion
    }
}
