using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;

namespace SubPhim.Server.Pages.Admin.VipTranslation
{
    public class ExternalApiKeyDetailModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IExternalApiKeyService _keyService;
        private readonly IExternalApiCreditService _creditService;
        private readonly ILogger<ExternalApiKeyDetailModel> _logger;

        public ExternalApiKeyDetailModel(
            AppDbContext context,
            IExternalApiKeyService keyService,
            IExternalApiCreditService creditService,
            ILogger<ExternalApiKeyDetailModel> logger)
        {
            _context = context;
            _keyService = keyService;
            _creditService = creditService;
            _logger = logger;
        }

        public ExternalApiKey? ApiKey { get; set; }
        public ExternalApiSettings Settings { get; set; } = new();
        public List<ExternalApiUsageLog> UsageLogs { get; set; } = new();
        public List<ExternalApiCreditTransaction> CreditTransactions { get; set; } = new();
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        // Statistics
        public long TotalCreditsUsed { get; set; }
        public int TotalJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            ApiKey = await _keyService.GetApiKeyByIdAsync(id);
            
            if (ApiKey == null)
            {
                return NotFound();
            }

            await LoadDataAsync(id);
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateKeyAsync(
            int id,
            string? displayName,
            string? assignedTo,
            string? email,
            string? notes,
            int rpmLimit)
        {
            try
            {
                var success = await _keyService.UpdateApiKeyAsync(id, displayName, assignedTo, email, notes, rpmLimit);
                if (success)
                {
                    SuccessMessage = "Thông tin API key ?ã ???c c?p nh?t.";
                    _logger.LogInformation("Admin updated API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm th?y API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating API key {KeyId}", id);
                ErrorMessage = $"L?i: {ex.Message}";
            }

            ApiKey = await _keyService.GetApiKeyByIdAsync(id);
            if (ApiKey == null) return NotFound();
            
            await LoadDataAsync(id);
            return Page();
        }

        public async Task<IActionResult> OnPostAddCreditsAsync(int id, long amount, string description)
        {
            try
            {
                var adminUsername = User.Identity?.Name ?? "Admin";
                await _creditService.AddCredits(id, amount, description, adminUsername);
                
                SuccessMessage = $"?ã n?p {amount:N0} credits thành công.";
                _logger.LogInformation("Admin {Admin} added {Amount} credits to API key {KeyId}", 
                    adminUsername, amount, id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding credits to API key {KeyId}", id);
                ErrorMessage = $"L?i khi n?p credits: {ex.Message}";
            }

            ApiKey = await _keyService.GetApiKeyByIdAsync(id);
            if (ApiKey == null) return NotFound();
            
            await LoadDataAsync(id);
            return Page();
        }

        public async Task<IActionResult> OnPostDisableKeyAsync(int id)
        {
            try
            {
                var success = await _keyService.DisableApiKeyAsync(id);
                if (success)
                {
                    SuccessMessage = "API key ?ã ???c vô hi?u hóa.";
                    _logger.LogInformation("Admin disabled API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm th?y API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling API key {KeyId}", id);
                ErrorMessage = $"L?i: {ex.Message}";
            }

            ApiKey = await _keyService.GetApiKeyByIdAsync(id);
            if (ApiKey == null) return NotFound();
            
            await LoadDataAsync(id);
            return Page();
        }

        public async Task<IActionResult> OnPostEnableKeyAsync(int id)
        {
            try
            {
                var success = await _keyService.EnableApiKeyAsync(id);
                if (success)
                {
                    SuccessMessage = "API key ?ã ???c kích ho?t.";
                    _logger.LogInformation("Admin enabled API key {KeyId}", id);
                }
                else
                {
                    ErrorMessage = "Không tìm th?y API key.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling API key {KeyId}", id);
                ErrorMessage = $"L?i: {ex.Message}";
            }

            ApiKey = await _keyService.GetApiKeyByIdAsync(id);
            if (ApiKey == null) return NotFound();
            
            await LoadDataAsync(id);
            return Page();
        }

        private async Task LoadDataAsync(int id)
        {
            Settings = await _context.ExternalApiSettings
                .FirstOrDefaultAsync(s => s.Id == 1) 
                ?? new ExternalApiSettings { Id = 1 };

            // Get usage logs (last 50)
            UsageLogs = await _context.ExternalApiUsageLogs
                .Where(l => l.ApiKeyId == id)
                .OrderByDescending(l => l.StartedAt)
                .Take(50)
                .ToListAsync();

            // Get credit transactions (last 50)
            CreditTransactions = await _context.ExternalApiCreditTransactions
                .Where(t => t.ApiKeyId == id)
                .OrderByDescending(t => t.CreatedAt)
                .Take(50)
                .ToListAsync();

            // Calculate statistics
            var stats = await _context.ExternalApiUsageLogs
                .Where(l => l.ApiKeyId == id)
                .GroupBy(l => 1)
                .Select(g => new
                {
                    TotalJobs = g.Count(),
                    CompletedJobs = g.Count(l => l.Status == UsageStatus.Completed),
                    FailedJobs = g.Count(l => l.Status == UsageStatus.Failed),
                    TotalCreditsUsed = g.Sum(l => l.CreditsCharged)
                })
                .FirstOrDefaultAsync();

            if (stats != null)
            {
                TotalJobs = stats.TotalJobs;
                CompletedJobs = stats.CompletedJobs;
                FailedJobs = stats.FailedJobs;
                TotalCreditsUsed = stats.TotalCreditsUsed;
            }
        }
    }
}
