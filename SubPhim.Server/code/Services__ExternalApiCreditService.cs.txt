using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service for managing External API credit system
    /// Handles credit estimation, charging, refunding, and balance management
    /// </summary>
    public class ExternalApiCreditService : IExternalApiCreditService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExternalApiCreditService> _logger;

        public ExternalApiCreditService(
            IServiceProvider serviceProvider,
            ILogger<ExternalApiCreditService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<bool> HasSufficientCredits(int apiKeyId, long requiredCredits)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            if (apiKey == null) return false;
            
            return apiKey.CreditBalance >= requiredCredits;
        }

        public async Task<long> EstimateCredits(int characterCount)
        {
            var settings = await GetSettingsAsync();
            return characterCount * settings.CreditsPerCharacter;
        }

        public async Task<bool> ReserveCredits(int apiKeyId, string sessionId, long amount)
        {
            // For now, we don't actually reserve credits upfront
            // We charge after completion based on actual output
            // This method is kept for future prepayment model if needed
            return await HasSufficientCredits(apiKeyId, amount);
        }

        public async Task ChargeCredits(int apiKeyId, string sessionId, int outputCharacters)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var settings = await GetSettingsInternalAsync(context);
                var creditsToCharge = outputCharacters * settings.CreditsPerCharacter;
                
                var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
                if (apiKey == null)
                {
                    _logger.LogError("API Key {ApiKeyId} not found when charging credits", apiKeyId);
                    throw new InvalidOperationException("API Key not found");
                }
                
                // Deduct credits
                apiKey.CreditBalance -= creditsToCharge;
                apiKey.TotalCreditsUsed += creditsToCharge;
                apiKey.LastUsedAt = DateTime.UtcNow;
                
                // Record transaction
                context.ExternalApiCreditTransactions.Add(new ExternalApiCreditTransaction
                {
                    ApiKeyId = apiKeyId,
                    Type = TransactionType.Usage,
                    Amount = -creditsToCharge,
                    BalanceAfter = apiKey.CreditBalance,
                    Description = $"Dịch job {sessionId} - {outputCharacters} ký tự",
                    RelatedUsageLogId = await GetUsageLogId(context, sessionId)
                });
                
                // Update usage log
                var usageLog = await context.ExternalApiUsageLogs
                    .FirstOrDefaultAsync(l => l.SessionId == sessionId);
                if (usageLog != null)
                {
                    usageLog.OutputCharacters = outputCharacters;
                    usageLog.CreditsCharged = creditsToCharge;
                    usageLog.Status = UsageStatus.Completed;
                    usageLog.CompletedAt = DateTime.UtcNow;
                    
                    if (usageLog.StartedAt != DateTime.MinValue)
                    {
                        usageLog.DurationMs = (int)(DateTime.UtcNow - usageLog.StartedAt).TotalMilliseconds;
                    }
                }
                
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                _logger.LogInformation(
                    "Charged {Credits} credits from API Key {KeyId} for session {SessionId}",
                    creditsToCharge, apiKeyId, sessionId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error charging credits for API Key {ApiKeyId}, session {SessionId}", apiKeyId, sessionId);
                throw;
            }
        }

        public async Task RefundCredits(int apiKeyId, string sessionId, string reason)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var usageLog = await context.ExternalApiUsageLogs
                    .FirstOrDefaultAsync(l => l.SessionId == sessionId && l.ApiKeyId == apiKeyId);
                
                if (usageLog == null || usageLog.CreditsCharged == 0)
                {
                    _logger.LogInformation("No credits to refund for session {SessionId}", sessionId);
                    return;
                }
                
                var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
                if (apiKey == null)
                {
                    _logger.LogError("API Key {ApiKeyId} not found when refunding credits", apiKeyId);
                    return;
                }
                
                var refundAmount = usageLog.CreditsCharged;
                
                // Refund credits
                apiKey.CreditBalance += refundAmount;
                apiKey.TotalCreditsUsed -= refundAmount;
                
                // Record transaction
                context.ExternalApiCreditTransactions.Add(new ExternalApiCreditTransaction
                {
                    ApiKeyId = apiKeyId,
                    Type = TransactionType.Refund,
                    Amount = refundAmount,
                    BalanceAfter = apiKey.CreditBalance,
                    Description = $"Hoàn tiền job {sessionId}: {reason}",
                    RelatedUsageLogId = usageLog.Id
                });
                
                // Update usage log
                usageLog.Status = UsageStatus.Refunded;
                usageLog.CreditsCharged = 0;
                usageLog.ErrorMessage = reason;
                
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                _logger.LogInformation(
                    "Refunded {Credits} credits to API Key {KeyId} for session {SessionId}. Reason: {Reason}",
                    refundAmount, apiKeyId, sessionId, reason);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error refunding credits for API Key {ApiKeyId}, session {SessionId}", apiKeyId, sessionId);
                throw;
            }
        }

        public async Task<long> GetBalance(int apiKeyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
            return apiKey?.CreditBalance ?? 0;
        }

        public async Task AddCredits(int apiKeyId, long amount, string description, string adminUsername)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var apiKey = await context.ExternalApiKeys.FindAsync(apiKeyId);
                if (apiKey == null)
                {
                    throw new InvalidOperationException("API Key not found");
                }
                
                // Add credits
                apiKey.CreditBalance += amount;
                apiKey.TotalCreditsAdded += amount;
                
                // Record transaction
                context.ExternalApiCreditTransactions.Add(new ExternalApiCreditTransaction
                {
                    ApiKeyId = apiKeyId,
                    Type = TransactionType.Deposit,
                    Amount = amount,
                    BalanceAfter = apiKey.CreditBalance,
                    Description = description,
                    CreatedBy = adminUsername
                });
                
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                _logger.LogInformation(
                    "Added {Credits} credits to API Key {KeyId} by admin {Admin}",
                    amount, apiKeyId, adminUsername);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error adding credits to API Key {ApiKeyId}", apiKeyId);
                throw;
            }
        }

        public async Task<(int CreditsPerCharacter, decimal VndPerCredit)> GetPricingSettings()
        {
            var settings = await GetSettingsAsync();
            return (settings.CreditsPerCharacter, settings.VndPerCredit);
        }

        // Private helper methods
        private async Task<ExternalApiSettings> GetSettingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await GetSettingsInternalAsync(context);
        }

        private async Task<ExternalApiSettings> GetSettingsInternalAsync(AppDbContext context)
        {
            var settings = await context.ExternalApiSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                // Create default settings if not exists
                settings = new ExternalApiSettings { Id = 1 };
                context.ExternalApiSettings.Add(settings);
                await context.SaveChangesAsync();
            }
            return settings;
        }

        private async Task<long?> GetUsageLogId(AppDbContext context, string sessionId)
        {
            var log = await context.ExternalApiUsageLogs
                .FirstOrDefaultAsync(l => l.SessionId == sessionId);
            return log?.Id;
        }
    }
}
