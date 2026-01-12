namespace SubPhim.Server.Services
{
    /// <summary>
    /// Interface for managing External API credit system
    /// </summary>
    public interface IExternalApiCreditService
    {
        /// <summary>
        /// Check if an API key has sufficient credits
        /// </summary>
        Task<bool> HasSufficientCredits(int apiKeyId, long requiredCredits);
        
        /// <summary>
        /// Estimate credits required for a given character count
        /// </summary>
        Task<long> EstimateCredits(int characterCount);
        
        /// <summary>
        /// Reserve credits before starting a job (optional, for prepayment model)
        /// </summary>
        Task<bool> ReserveCredits(int apiKeyId, string sessionId, long amount);
        
        /// <summary>
        /// Charge credits after job completion based on actual output characters
        /// </summary>
        Task ChargeCredits(int apiKeyId, string sessionId, int outputCharacters);
        
        /// <summary>
        /// Refund credits if job fails or is cancelled
        /// </summary>
        Task RefundCredits(int apiKeyId, string sessionId, string reason);
        
        /// <summary>
        /// Get current credit balance for an API key
        /// </summary>
        Task<long> GetBalance(int apiKeyId);
        
        /// <summary>
        /// Add credits to an API key (admin operation)
        /// </summary>
        Task AddCredits(int apiKeyId, long amount, string description, string adminUsername);
        
        /// <summary>
        /// Get current pricing settings
        /// </summary>
        Task<(int CreditsPerCharacter, decimal VndPerCredit)> GetPricingSettings();
    }
}
