using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin
{
    public class TtsManagementModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ITtsSettingsService _ttsSettingsService;

        public TtsManagementModel(AppDbContext context, IEncryptionService encryptionService, ITtsSettingsService ttsSettingsService)
        {
            _context = context;
            _encryptionService = encryptionService;
            _ttsSettingsService = ttsSettingsService;
        }

        public List<ApiKeyViewModel> GeminiProKeys { get; set; } = new();
        public List<ApiKeyViewModel> GeminiFlashKeys { get; set; } = new();
        public List<ApiKeyViewModel> ElevenLabsKeys { get; set; } = new();

        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? ErrorMessage { get; set; }

        public class ApiKeyViewModel
        {
            public TtsApiKey KeyData { get; set; } = null!;
            public string DecryptedApiKey { get; set; } = string.Empty;
        }

        public class ApiKeyInputModel
        {
            [Required(ErrorMessage = "B?n ph?i nh?p ít nh?t 1 API Key.")]
            public string ApiKeys { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            var allTtsKeys = await _context.TtsApiKeys
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync();

            foreach (var key in allTtsKeys)
            {
                var viewModel = new ApiKeyViewModel { KeyData = key };
                try
                {
                    viewModel.DecryptedApiKey = _encryptionService.Decrypt(key.EncryptedApiKey, key.Iv);
                }
                catch
                {
                    viewModel.DecryptedApiKey = "!!! L?i g?i MÃ !!!";
                }

                if (key.Provider == TtsProvider.Gemini)
                {
                    if (key.ModelName != null && key.ModelName.Contains("pro"))
                        GeminiProKeys.Add(viewModel);
                    else
                        GeminiFlashKeys.Add(viewModel);
                }
                else if (key.Provider == TtsProvider.ElevenLabs)
                {
                    ElevenLabsKeys.Add(viewModel);
                }
            }
        }

        public Task<IActionResult> OnPostAddGeminiProKeysAsync([FromForm] ApiKeyInputModel apiKeyInput) =>
            CreateKeysHandler(apiKeyInput, TtsProvider.Gemini, "Pro");

        public Task<IActionResult> OnPostAddGeminiFlashKeysAsync([FromForm] ApiKeyInputModel apiKeyInput) =>
            CreateKeysHandler(apiKeyInput, TtsProvider.Gemini, "Flash");

        public Task<IActionResult> OnPostAddElevenLabsKeysAsync([FromForm] ApiKeyInputModel apiKeyInput) =>
            CreateKeysHandler(apiKeyInput, TtsProvider.ElevenLabs, "ElevenLabs");

        private async Task<IActionResult> CreateKeysHandler(ApiKeyInputModel apiKeyInput, TtsProvider provider, string identifier)
        {
            ModelState.Clear();
            if (!TryValidateModel(apiKeyInput, nameof(apiKeyInput)))
            {
                ErrorMessage = "B?n ph?i nh?p ít nh?t 1 API Key.";
                return RedirectToPage();
            }

            var keys = apiKeyInput.ApiKeys.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;

            foreach (var key in keys)
            {
                var trimmedKey = key.Trim();
                if (string.IsNullOrWhiteSpace(trimmedKey)) continue;

                var (encryptedText, iv) = _encryptionService.Encrypt(trimmedKey);
                var newApiKey = new TtsApiKey
                {
                    EncryptedApiKey = encryptedText,
                    Iv = iv,
                    Provider = provider,
                    IsEnabled = true,
                };

                // <<< B?T ??U LOGIC S?A L?I >>>
                if (provider == TtsProvider.Gemini)
                {
                    // N?u là Gemini, chúng ta c?n tìm ModelName t? settings
                    var modelSettings = await _ttsSettingsService.GetModelSettingsAsync(provider, identifier);
                    if (modelSettings == null)
                    {
                        ErrorMessage = $"Không tìm th?y c?u hình cho model '{identifier}'. Vui lòng thêm c?u hình trong trang 'C?u hình Model TTS' tr??c.";
                        return RedirectToPage();
                    }
                    newApiKey.ModelName = modelSettings.ModelName;
                }
                else if (provider == TtsProvider.ElevenLabs)
                {
                    newApiKey.ModelName = null; // ??m b?o tr??ng này là null
                }
                // <<< K?T THÚC LOGIC S?A L?I >>>

                _context.TtsApiKeys.Add(newApiKey);
                count++;
            }

            if (count > 0)
            {
                await _context.SaveChangesAsync();
                SuccessMessage = $"?ã thêm thành công {count} API key cho {provider} ({identifier}).";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleEnabledAsync(int id)
        {
            var keyInDb = await _context.TtsApiKeys.FindAsync(id);
            if (keyInDb != null)
            {
                keyInDb.IsEnabled = !keyInDb.IsEnabled;
                if (keyInDb.IsEnabled)
                {
                    keyInDb.DisabledReason = null;
                }
                else
                {
                    keyInDb.DisabledReason = "Vô hi?u hóa th? công b?i Admin.";
                }
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteKeyAsync(int id)
        {
            var keyToDelete = await _context.TtsApiKeys.FindAsync(id);
            if (keyToDelete != null)
            {
                _context.TtsApiKeys.Remove(keyToDelete);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }
    }
}