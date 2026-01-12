using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Interface for managing External API keys
    /// </summary>
    public interface IExternalApiKeyService
    {
        /// <summary>
        /// Generate a new API key
        /// </summary>
        Task<(ExternalApiKey Key, string PlainTextKey)> CreateApiKeyAsync(
            string? displayName,
            string? assignedTo,
            string? email,
            string? notes,
            int? rpmLimit = null,
            long? initialCredits = null,
            DateTime? expiresAt = null);
        
        /// <summary>
        /// Validate and retrieve an API key by its hash
        /// </summary>
        Task<ExternalApiKey?> ValidateApiKeyAsync(string plainTextKey);
        
        /// <summary>
        /// Disable an API key
        /// </summary>
        Task<bool> DisableApiKeyAsync(int apiKeyId);
        
        /// <summary>
        /// Enable an API key
        /// </summary>
        Task<bool> EnableApiKeyAsync(int apiKeyId);
        
        /// <summary>
        /// Delete an API key
        /// </summary>
        Task<bool> DeleteApiKeyAsync(int apiKeyId);
        
        /// <summary>
        /// Update API key details
        /// </summary>
        Task<bool> UpdateApiKeyAsync(int apiKeyId, string? displayName, string? assignedTo, string? email, string? notes, int? rpmLimit);
        
        /// <summary>
        /// Get API key by ID
        /// </summary>
        Task<ExternalApiKey?> GetApiKeyByIdAsync(int apiKeyId);
        
        /// <summary>
        /// Get all API keys with optional filters
        /// </summary>
        Task<List<ExternalApiKey>> GetAllApiKeysAsync(bool? isEnabled = null);
    }
}
