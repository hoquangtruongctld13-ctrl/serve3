using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Security.Cryptography;
using System.Text;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service for managing External API keys
    /// Handles key generation, validation, and CRUD operations
    /// </summary>
    public class ExternalApiKeyService : IExternalApiKeyService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExternalApiKeyService> _logger;
        private const string KEY_PREFIX = "AIO_";
        private const int KEY_LENGTH = 48; // 48 characters after prefix

        public ExternalApiKeyService(
            IServiceProvider serviceProvider,
            ILogger<ExternalApiKeyService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<(ExternalApiKey Key, string PlainTextKey)> CreateApiKeyAsync(
            string? displayName,
            string? assignedTo,
            string? email,
            string? notes,
            int? rpmLimit = null,
            long? initialCredits = null,
            DateTime? expiresAt = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Get default settings
            var settings = await context.ExternalApiSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = new ExternalApiSettings { Id = 1 };
                context.ExternalApiSettings.Add(settings);
                await context.SaveChangesAsync();
            }
            
            // Generate random API key
            var plainTextKey = GenerateApiKey();
            var keyHash = ComputeSha256Hash(plainTextKey);
            var keySuffix = plainTextKey.Substring(plainTextKey.Length - 4);
            
            var apiKey = new ExternalApiKey
            {
                KeyHash = keyHash,
                KeyPrefix = KEY_PREFIX,
                KeySuffix = keySuffix,
                DisplayName = displayName,
                AssignedTo = assignedTo,
                Email = email,
                Notes = notes,
                RpmLimit = rpmLimit ?? settings.DefaultRpm,
                CreditBalance = initialCredits ?? settings.DefaultInitialCredits,
                TotalCreditsAdded = initialCredits ?? settings.DefaultInitialCredits,
                ExpiresAt = expiresAt,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };
            
            context.ExternalApiKeys.Add(apiKey);
            
            // Add initial credit transaction if credits were added
            if (apiKey.CreditBalance > 0)
            {
                context.ExternalApiCreditTransactions.Add(new ExternalApiCreditTransaction
                {
                    ApiKey = apiKey,
                    Type = TransactionType.Deposit,
                    Amount = apiKey.CreditBalance,
                    BalanceAfter = apiKey.CreditBalance,
                    Description = "Credit khởi tạo khi tạo API key",
                    CreatedBy = "System"
                });
            }
            
            await context.SaveChangesAsync();
            
            _logger.LogInformation(
                "Created new API key {KeyId} ({DisplayName}) for {AssignedTo}",
                apiKey.Id, displayName ?? "Unnamed", assignedTo ?? "Unassigned");
            
            return (apiKey, plainTextKey);
        }

        public async Task<ExternalApiKey?> ValidateApiKeyAsync(string plainTextKey)
        {
            if (string.IsNullOrEmpty(plainTextKey) || !plainTextKey.StartsWith(KEY_PREFIX))
            {
                return null;
            }
            
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var keyHash = ComputeSha256Hash(plainTextKey);
            
            var apiKey = await context.ExternalApiKeys
                .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsEnabled);
            
            if (apiKey == null)
            {
                return null;
            }
            
            // Check expiration
            if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("API Key {KeyId} has expired", apiKey.Id);
                return null;
            }
            
            return apiKey;
        }

        public async Task<bool> DisableApiKeyAsync(int apiKeyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            if (apiKey == null) return false;
            
            apiKey.IsEnabled = false;
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Disabled API key {KeyId}", apiKeyId);
            return true;
        }

        public async Task<bool> EnableApiKeyAsync(int apiKeyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            if (apiKey == null) return false;
            
            apiKey.IsEnabled = true;
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Enabled API key {KeyId}", apiKeyId);
            return true;
        }

        public async Task<bool> DeleteApiKeyAsync(int apiKeyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            if (apiKey == null) return false;
            
            context.ExternalApiKeys.Remove(apiKey);
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Deleted API key {KeyId}", apiKeyId);
            return true;
        }

        public async Task<bool> UpdateApiKeyAsync(int apiKeyId, string? displayName, string? assignedTo, string? email, string? notes, int? rpmLimit)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            if (apiKey == null) return false;
            
            if (displayName != null) apiKey.DisplayName = displayName;
            if (assignedTo != null) apiKey.AssignedTo = assignedTo;
            if (email != null) apiKey.Email = email;
            if (notes != null) apiKey.Notes = notes;
            if (rpmLimit.HasValue) apiKey.RpmLimit = rpmLimit.Value;
            
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Updated API key {KeyId}", apiKeyId);
            return true;
        }

        public async Task<ExternalApiKey?> GetApiKeyByIdAsync(int apiKeyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            return await context.ExternalApiKeys
                .Include(k => k.UsageLogs)
                .Include(k => k.CreditTransactions)
                .FirstOrDefaultAsync(k => k.Id == apiKeyId);
        }

        public async Task<List<ExternalApiKey>> GetAllApiKeysAsync(bool? isEnabled = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var query = context.ExternalApiKeys.AsQueryable();
            
            if (isEnabled.HasValue)
            {
                query = query.Where(k => k.IsEnabled == isEnabled.Value);
            }
            
            return await query
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync();
        }

        // Private helper methods
        private string GenerateApiKey()
        {
            // Generate a secure random API key using cryptographic random number generator
            // Key format: AIO_ + 48 Base64URL-safe characters
            var randomBytes = new byte[36]; // 36 bytes = 48 base64 characters
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            
            // Convert to Base64 and make URL-safe
            var base64 = Convert.ToBase64String(randomBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");
            
            return KEY_PREFIX + base64;
        }

        /// <summary>
        /// Compute SHA-256 hash of the API key for secure storage
        /// </summary>
        public static string ComputeSha256Hash(string rawData)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            return Convert.ToBase64String(bytes);
        }
    }
}
