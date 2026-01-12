using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin.VipTranslation
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<IndexModel> _logger;
        
        private const int DEFAULT_SETTINGS_ID = 1;

        public IndexModel(AppDbContext context, IEncryptionService encryptionService, ILogger<IndexModel> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _logger = logger;
        }

        public List<ApiKeyViewModel> ApiKeys { get; set; } = new();
        public List<VipAvailableApiModel> Models { get; set; }
        public string ActiveModelName { get; set; }

        [BindProperty]
        public GlobalSettingsInputModel GlobalSettings { get; set; }

        [TempData] public string SuccessMessage { get; set; }
        [TempData] public string ErrorMessage { get; set; }

        #region ViewModels and InputModels
        public class ApiKeyViewModel
        {
            public VipApiKey KeyData { get; set; }
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
            [Required][Range(1, 1000)] public int RpmPerProxy { get; set; }
            [Required][Range(100, 10000)] public int MaxSrtLineLength { get; set; }
        }
        #endregion

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
            var settingsFromDb = await _context.VipTranslationSettings.FindAsync(DEFAULT_SETTINGS_ID) ?? new VipTranslationSetting();
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
                RpmPerProxy = settingsFromDb.RpmPerProxy,
                MaxSrtLineLength = settingsFromDb.MaxSrtLineLength
            };
        }

        public async Task<IActionResult> OnPostDeleteSelectedKeysAsync([FromForm] int[] selectedKeyIds)
        {
            if (selectedKeyIds == null || !selectedKeyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một API key để xóa.";
                return RedirectToPage();
            }

            var keysToDelete = await _context.VipApiKeys
                .Where(k => selectedKeyIds.Contains(k.Id))
                .ToListAsync();

            if (keysToDelete.Any())
            {
                _context.VipApiKeys.RemoveRange(keysToDelete);
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

            var keysToDisable = await _context.VipApiKeys
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
            ApiKeys = await GetApiKeysAsync();
            Models = await _context.VipAvailableApiModels.OrderByDescending(m => m.CreatedAt).ToListAsync();
            ActiveModelName = Models.FirstOrDefault(m => m.IsActive)?.ModelName ?? "Chưa có";
        }

        private async Task<List<ApiKeyViewModel>> GetApiKeysAsync()
        {
            var apiKeys = new List<ApiKeyViewModel>();
            var keysFromDb = await _context.VipApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync();
            foreach (var key in keysFromDb)
            {
                try 
                { 
                    apiKeys.Add(new ApiKeyViewModel 
                    { 
                        KeyData = key, 
                        DecryptedApiKey = _encryptionService.Decrypt(key.EncryptedApiKey, key.Iv) 
                    }); 
                }
                catch 
                { 
                    apiKeys.Add(new ApiKeyViewModel 
                    { 
                        KeyData = key, 
                        DecryptedApiKey = "!!! LỖI GIẢI MÃ !!!" 
                    }); 
                }
            }
            return apiKeys;
        }

        public async Task<IActionResult> OnPostUpdateSettingsAsync()
        {
            if (!TryValidateModel(GlobalSettings, nameof(GlobalSettings)))
            {
                TempData["ErrorMessage"] = "Dữ liệu cài đặt chung không hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToPage();
            }
            try
            {
                var settingsInDb = await _context.VipTranslationSettings.FindAsync(DEFAULT_SETTINGS_ID);
                if (settingsInDb == null)
                {
                    settingsInDb = new VipTranslationSetting { Id = DEFAULT_SETTINGS_ID };
                    _context.VipTranslationSettings.Add(settingsInDb);
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
                settingsInDb.MaxSrtLineLength = GlobalSettings.MaxSrtLineLength;
                await _context.SaveChangesAsync();
                SuccessMessage = "Đã lưu thành công cài đặt chung.";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Lỗi khi lưu cài đặt: " + ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCreateKeyAsync([FromForm] ApiKeyInputModel apiKeyInput)
        {
            ModelState.Clear();

            if (!TryValidateModel(apiKeyInput, nameof(apiKeyInput)))
            {
                TempData["ErrorMessage"] = "Bạn phải nhập ít nhất một API Key.";
                return RedirectToPage();
            }

            var keys = apiKeyInput.ApiKeys.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var key in keys)
            {
                var trimmedKey = key.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedKey))
                {
                    var (encryptedText, iv) = _encryptionService.Encrypt(trimmedKey);
                    _context.VipApiKeys.Add(new VipApiKey { EncryptedApiKey = encryptedText, Iv = iv });
                }
            }
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm thành công {keys.Length} API key.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddModelAsync([FromForm] NewModelInputModel newModelInput)
        {
            ModelState.Clear();

            if (!TryValidateModel(newModelInput, nameof(newModelInput)))
            {
                TempData["ErrorMessage"] = "Tên model không được để trống.";
                return RedirectToPage();
            }

            var modelName = newModelInput.ModelName.Trim();
            if (await _context.VipAvailableApiModels.AnyAsync(m => m.ModelName == modelName))
            {
                ErrorMessage = $"Model '{modelName}' đã tồn tại.";
                return RedirectToPage();
            }
            var isFirstModel = !await _context.VipAvailableApiModels.AnyAsync();
            _context.VipAvailableApiModels.Add(new VipAvailableApiModel 
            { 
                ModelName = modelName, 
                IsActive = isFirstModel 
            });
            await _context.SaveChangesAsync();
            SuccessMessage = $"Đã thêm model '{modelName}'.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSetActiveModelAsync(int id)
        {
            var allModels = await _context.VipAvailableApiModels.ToListAsync();
            foreach (var model in allModels)
            {
                model.IsActive = (model.Id == id);
            }
            await _context.SaveChangesAsync();
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleEnabledAsync(int id)
        {
            var keyInDb = await _context.VipApiKeys.FindAsync(id);
            if (keyInDb != null)
            {
                keyInDb.IsEnabled = !keyInDb.IsEnabled;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteKeyAsync(int id)
        {
            var keyToDelete = await _context.VipApiKeys.FindAsync(id);
            if (keyToDelete != null)
            {
                _context.VipApiKeys.Remove(keyToDelete);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteModelAsync(int id)
        {
            var modelToDelete = await _context.VipAvailableApiModels.FindAsync(id);
            if (modelToDelete != null)
            {
                if (modelToDelete.IsActive)
                {
                    ErrorMessage = "Không thể xóa model đang được kích hoạt.";
                }
                else
                {
                    _context.VipAvailableApiModels.Remove(modelToDelete);
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToPage();
        }
    }
}
