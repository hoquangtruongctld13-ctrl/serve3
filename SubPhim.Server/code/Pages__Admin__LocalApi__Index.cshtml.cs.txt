using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin.LocalApi
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

        // Dữ liệu để hiển thị (GET)
        public List<ApiKeyViewModel> PaidApiKeys { get; set; } = new();
        public List<AvailableApiModel> PaidModels { get; set; }
        public string ActivePaidModelName { get; set; }

        public List<ApiKeyViewModel> FreeApiKeys { get; set; } = new();
        public List<AvailableApiModel> FreeModels { get; set; }
        public string ActiveFreeModelName { get; set; }

        // Chỉ BindProperty cho GlobalSettings vì nó được load sẵn khi GET
        [BindProperty]
        public GlobalSettingsInputModel GlobalSettings { get; set; }

        [TempData] public string SuccessMessage { get; set; }
        [TempData] public string ErrorMessage { get; set; }

        #region ViewModels and InputModels (Nested classes)
        public class ApiKeyViewModel
        {
            public ManagedApiKey KeyData { get; set; }
            public string DecryptedApiKey { get; set; }
        }

        public class ApiKeyInputModel
        {
            [Required(ErrorMessage = "Bạn phải nhập ít nhất một API Key.")]
            public string ApiKeys { get; set; }
        }

        public class NewModelInputModel
        {
            [Required(ErrorMessage = "Tên model không được để trống.")]
            public string ModelName { get; set; }
        }

        public class GlobalSettingsInputModel
        {
            [Required][Range(1, 1000)] public int Rpm { get; set; }
            [Required][Range(1, 500)] public int BatchSize { get; set; }
            [Required][Range(0, 10)] public int MaxRetries { get; set; }
            [Required][Range(0, 60000)] public int RetryDelayMs { get; set; }
            [Required][Range(0, 60000)] public int DelayBetweenBatchesMs { get; set; }
            [Required][Range(0.0, 2.0)] public decimal Temperature { get; set; }
            [Required][Range(1, 32000)] public int MaxOutputTokens { get; set; }
            public bool EnableThinkingBudget { get; set; }
            [Range(0, 16384)] public int ThinkingBudget { get; set; }
            
            // Proxy Rate Limiting Settings
            [Required][Range(1, 1000)] public int RpmPerProxy { get; set; }
        }
        #endregion

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
            var settingsFromDb = await _context.LocalApiSettings.FindAsync(1) ?? new LocalApiSetting();
            GlobalSettings = new GlobalSettingsInputModel
            {
                Rpm = settingsFromDb.Rpm,
                BatchSize = settingsFromDb.BatchSize,
                MaxRetries = settingsFromDb.MaxRetries,
                RetryDelayMs = settingsFromDb.RetryDelayMs,
                DelayBetweenBatchesMs = settingsFromDb.DelayBetweenBatchesMs,
                Temperature = settingsFromDb.Temperature,
                MaxOutputTokens = settingsFromDb.MaxOutputTokens,
                EnableThinkingBudget = settingsFromDb.EnableThinkingBudget,
                ThinkingBudget = settingsFromDb.ThinkingBudget,
                RpmPerProxy = settingsFromDb.RpmPerProxy
            };
        }
        public async Task<IActionResult> OnPostDeleteSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một API key để xóa.";
                return RedirectToPage();
            }

            var keysToDelete = await _context.ManagedApiKeys
                .Where(k => selectedKeyIds.Contains(k.Id))
                .ToListAsync();

            if (keysToDelete.Any())
            {
                _context.ManagedApiKeys.RemoveRange(keysToDelete);
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã xóa thành công {keysToDelete.Count} API key.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDisableSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một API key để tắt.";
                return RedirectToPage();
            }

            var keysToDisable = await _context.ManagedApiKeys
                .Where(k => selectedKeyIds.Contains(k.Id))
                .ToListAsync();

            if (keysToDisable.Any())
            {
                foreach (var key in keysToDisable)
                {
                    key.IsEnabled = false;
                }
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã vô hiệu hóa thành công {keysToDisable.Count} API key.";
            }

            return RedirectToPage();
        }
        private async Task LoadDataAsync()
        {
            PaidApiKeys = await GetApiKeysByPool(ApiPoolType.Paid);
            PaidModels = await _context.AvailableApiModels.Where(m => m.PoolType == ApiPoolType.Paid).OrderByDescending(m => m.CreatedAt).ToListAsync();
            ActivePaidModelName = PaidModels.FirstOrDefault(m => m.IsActive)?.ModelName ?? "Chưa có";

            FreeApiKeys = await GetApiKeysByPool(ApiPoolType.Free);
            FreeModels = await _context.AvailableApiModels.Where(m => m.PoolType == ApiPoolType.Free).OrderByDescending(m => m.CreatedAt).ToListAsync();
            ActiveFreeModelName = FreeModels.FirstOrDefault(m => m.IsActive)?.ModelName ?? "Chưa có";
        }

        private async Task<List<ApiKeyViewModel>> GetApiKeysByPool(ApiPoolType pool)
        {
            var apiKeys = new List<ApiKeyViewModel>();
            var keysFromDb = await _context.ManagedApiKeys.Where(k => k.PoolType == pool).OrderByDescending(k => k.CreatedAt).ToListAsync();
            foreach (var key in keysFromDb)
            {
                try { apiKeys.Add(new ApiKeyViewModel { KeyData = key, DecryptedApiKey = _encryptionService.Decrypt(key.EncryptedApiKey, key.Iv) }); }
                catch { apiKeys.Add(new ApiKeyViewModel { KeyData = key, DecryptedApiKey = "!!! LỖI GIẢI MÃ !!!" }); }
            }
            return apiKeys;
        }

        // ==========================================================
        // === PUBLIC HANDLERS ===
        // ==========================================================

        public async Task<IActionResult> OnPostUpdateSettingsAsync()
        {
            if (!TryValidateModel(GlobalSettings, nameof(GlobalSettings)))
            {
                TempData["ErrorMessage"] = "Dữ liệu cài đặt chung không hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToPage();
            }
            try
            {
                var settingsInDb = await _context.LocalApiSettings.FindAsync(1);
                if (settingsInDb == null)
                {
                    settingsInDb = new LocalApiSetting { Id = 1 };
                    _context.LocalApiSettings.Add(settingsInDb);
                }
                settingsInDb.Rpm = GlobalSettings.Rpm;
                settingsInDb.BatchSize = GlobalSettings.BatchSize;
                settingsInDb.MaxRetries = GlobalSettings.MaxRetries;
                settingsInDb.RetryDelayMs = GlobalSettings.RetryDelayMs;
                settingsInDb.DelayBetweenBatchesMs = GlobalSettings.DelayBetweenBatchesMs;
                settingsInDb.Temperature = GlobalSettings.Temperature;
                settingsInDb.MaxOutputTokens = GlobalSettings.MaxOutputTokens;
                settingsInDb.EnableThinkingBudget = GlobalSettings.EnableThinkingBudget;
                settingsInDb.ThinkingBudget = GlobalSettings.ThinkingBudget;
                settingsInDb.RpmPerProxy = GlobalSettings.RpmPerProxy;
                await _context.SaveChangesAsync();
                SuccessMessage = "Đã lưu thành công cài đặt chung.";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Lỗi khi lưu cài đặt: " + ex.Message;
            }
            return RedirectToPage();
        }

        public Task<IActionResult> OnPostCreatePaidKeyAsync([FromForm] ApiKeyInputModel apiKeyInput)
            => CreateKeyHandler(apiKeyInput, ApiPoolType.Paid);

        public Task<IActionResult> OnPostCreateFreeKeyAsync([FromForm] ApiKeyInputModel apiKeyInput)
            => CreateKeyHandler(apiKeyInput, ApiPoolType.Free);

        public Task<IActionResult> OnPostAddPaidModelAsync([FromForm] NewModelInputModel newModelInput)
            => AddModelHandler(newModelInput, ApiPoolType.Paid);

        public Task<IActionResult> OnPostAddFreeModelAsync([FromForm] NewModelInputModel newModelInput)
            => AddModelHandler(newModelInput, ApiPoolType.Free);

        public Task<IActionResult> OnPostSetActivePaidModelAsync(int id)
            => SetActiveModelHandler(id, ApiPoolType.Paid);

        public Task<IActionResult> OnPostSetActiveFreeModelAsync(int id)
            => SetActiveModelHandler(id, ApiPoolType.Free);

        public async Task<IActionResult> OnPostToggleEnabledAsync(int id)
        {
            var keyInDb = await _context.ManagedApiKeys.FindAsync(id);
            if (keyInDb != null)
            {
                keyInDb.IsEnabled = !keyInDb.IsEnabled;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteKeyAsync(int id)
        {
            var keyToDelete = await _context.ManagedApiKeys.FindAsync(id);
            if (keyToDelete != null)
            {
                _context.ManagedApiKeys.Remove(keyToDelete);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteModelAsync(int id)
        {
            var modelToDelete = await _context.AvailableApiModels.FindAsync(id);
            if (modelToDelete != null)
            {
                if (modelToDelete.IsActive)
                {
                    ErrorMessage = "Không thể xóa model đang được kích hoạt.";
                }
                else
                {
                    _context.AvailableApiModels.Remove(modelToDelete);
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage();
        }
        private async Task<IActionResult> CreateKeyHandler(ApiKeyInputModel apiKeyInput, ApiPoolType pool)
        {
            // BƯỚC 1: XÓA SẠCH LỖI CŨ - ĐÂY LÀ CHÌA KHÓA QUAN TRỌNG NHẤT
            ModelState.Clear();

            // BƯỚC 2: CHỈ VALIDATE MODEL CỦA FORM NÀY
            if (!TryValidateModel(apiKeyInput, nameof(apiKeyInput)))
            {
                TempData["ErrorMessage"] = "Bạn phải nhập ít nhất một API Key.";
                return RedirectToPage();
            }

            // BƯỚC 3: XỬ LÝ LOGIC
            var keys = apiKeyInput.ApiKeys.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var key in keys)
            {
                var trimmedKey = key.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedKey))
                {
                    var (encryptedText, iv) = _encryptionService.Encrypt(trimmedKey);
                    _context.ManagedApiKeys.Add(new ManagedApiKey { EncryptedApiKey = encryptedText, Iv = iv, PoolType = pool });
                }
            }
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm thành công {keys.Length} API key vào nhóm {pool}.";
            return RedirectToPage();
        }


        private async Task<IActionResult> AddModelHandler(NewModelInputModel newModelInput, ApiPoolType pool)
        {
            // BƯỚC 1: XÓA SẠCH LỖI CŨ
            ModelState.Clear();

            // BƯỚC 2: CHỈ VALIDATE MODEL CỦA FORM NÀY
            if (!TryValidateModel(newModelInput, nameof(newModelInput)))
            {
                TempData["ErrorMessage"] = "Tên model không được để trống.";
                return RedirectToPage();
            }

            // BƯỚC 3: XỬ LÝ LOGIC
            var modelName = newModelInput.ModelName.Trim();
            if (await _context.AvailableApiModels.AnyAsync(m => m.ModelName == modelName && m.PoolType == pool))
            {
                ErrorMessage = $"Model '{modelName}' đã tồn tại trong nhóm {pool}.";
                return RedirectToPage();
            }
            var isFirstModelInPool = !await _context.AvailableApiModels.AnyAsync(m => m.PoolType == pool);
            _context.AvailableApiModels.Add(new AvailableApiModel { ModelName = modelName, IsActive = isFirstModelInPool, PoolType = pool });
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm model '{modelName}' vào nhóm {pool}.";
            return RedirectToPage();
        }


        private async Task<IActionResult> SetActiveModelHandler(int id, ApiPoolType pool)
        {
            var allModelsInPool = await _context.AvailableApiModels.Where(m => m.PoolType == pool).ToListAsync();
            foreach (var model in allModelsInPool)
            {
                model.IsActive = (model.Id == id);
            }
            await _context.SaveChangesAsync();
            return RedirectToPage();
        }
    }
}