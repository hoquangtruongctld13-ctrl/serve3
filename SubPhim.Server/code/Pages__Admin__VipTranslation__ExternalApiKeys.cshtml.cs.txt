using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;

namespace SubPhim.Server.Pages.Admin.VipTranslation
{
    public class ExternalApiKeysModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IExternalApiKeyService _keyService;
        private readonly IExternalApiCreditService _creditService;
        private readonly ILogger<ExternalApiKeysModel> _logger;

        public ExternalApiKeysModel(
            AppDbContext context,
            IExternalApiKeyService keyService,
            IExternalApiCreditService creditService,
            ILogger<ExternalApiKeysModel> logger)
        {
            _context = context;
            _keyService = keyService;
            _creditService = creditService;
            _logger = logger;
        }

        public List<ExternalApiKey> ApiKeys { get; set; } = new();
        public ExternalApiSettings Settings { get; set; } = new();
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? NewApiKey { get; set; }

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        public async Task<IActionResult> OnPostCreateKeyAsync(
            string? displayName,
            string? assignedTo,
            string? email,
            string? notes,
            int rpmLimit,
            long initialCredits)
        {
            try
            {
                var (key, plainTextKey) = await _keyService.CreateApiKeyAsync(
                    displayName,
                    assignedTo,
                    email,
                    notes,
                    rpmLimit,
                    initialCredits,
                    null);

                NewApiKey = plainTextKey;
                SuccessMessage = $"API Key đã được tạo thành công! Hãy sao chép key ngay bây giờ.";
                
                _logger.LogInformation("Admin created new API key {KeyId} for {AssignedTo}", key.Id, assignedTo ?? "N/A");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating API key");
                ErrorMessage = $"Lỗi khi tạo API key: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDisableKeyAsync(int id)
        {
            try
            {
                var success = await _keyService.DisableApiKeyAsync(id);
                if (success)
                {
                    SuccessMessage = "API key đã được vô hiệu hóa.";
                    _logger.LogInformation("Admin disabled API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm thấy API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling API key {KeyId}", id);
                ErrorMessage = $"Lỗi: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostEnableKeyAsync(int id)
        {
            try
            {
                var success = await _keyService.EnableApiKeyAsync(id);
                if (success)
                {
                    SuccessMessage = "API key đã được kích hoạt.";
                    _logger.LogInformation("Admin enabled API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm thấy API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling API key {KeyId}", id);
                ErrorMessage = $"Lỗi: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteKeyAsync(int id)
        {
            try
            {
                var success = await _keyService.DeleteApiKeyAsync(id);
                if (success)
                {
                    SuccessMessage = "API key đã được xóa vĩnh viễn.";
                    _logger.LogInformation("Admin deleted API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm thấy API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting API key {KeyId}", id);
                ErrorMessage = $"Lỗi: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAddCreditsAsync(int apiKeyId, long amount, string description)
        {
            try
            {
                var adminUsername = User.Identity?.Name ?? "Admin";
                await _creditService.AddCredits(apiKeyId, amount, description, adminUsername);
                
                SuccessMessage = $"Đã nạp {amount:N0} credits thành công.";
                _logger.LogInformation("Admin {Admin} added {Amount} credits to API key {KeyId}", 
                    adminUsername, amount, apiKeyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding credits to API key {KeyId}", apiKeyId);
                ErrorMessage = $"Lỗi khi nạp credits: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateSettingsAsync(ExternalApiSettings settings)
        {
            try
            {
                var existingSettings = await _context.ExternalApiSettings
                    .FirstOrDefaultAsync(s => s.Id == 1);

                if (existingSettings == null)
                {
                    existingSettings = new ExternalApiSettings { Id = 1 };
                    _context.ExternalApiSettings.Add(existingSettings);
                }

                existingSettings.CreditsPerCharacter = settings.CreditsPerCharacter;
                existingSettings.VndPerCredit = settings.VndPerCredit;
                existingSettings.DefaultRpm = settings.DefaultRpm;
                existingSettings.DefaultInitialCredits = settings.DefaultInitialCredits;
                existingSettings.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                SuccessMessage = "Cài đặt đã được cập nhật thành công.";
                _logger.LogInformation("Admin updated External API settings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings");
                ErrorMessage = $"Lỗi khi cập nhật cài đặt: {ex.Message}";
            }

            await LoadDataAsync();
            return Page();
        }

        private async Task LoadDataAsync()
        {
            ApiKeys = await _keyService.GetAllApiKeysAsync();
            
            Settings = await _context.ExternalApiSettings
                .FirstOrDefaultAsync(s => s.Id == 1) 
                ?? new ExternalApiSettings { Id = 1 };
        }
    }
}
