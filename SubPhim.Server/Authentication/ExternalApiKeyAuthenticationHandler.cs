using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SubPhim.Server.Authentication
{
    /// <summary>
    /// Authentication handler for External API Key authentication
    /// Validates API keys from X-API-Key header or Authorization: Bearer header
    /// </summary>
    public class ExternalApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IExternalApiKeyService _apiKeyService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ExternalApiKeyAuthenticationHandler> _logger;

        public ExternalApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IExternalApiKeyService apiKeyService,
            IMemoryCache cache)
            : base(options, logger, encoder)
        {
            _apiKeyService = apiKeyService;
            _cache = cache;
            _logger = logger.CreateLogger<ExternalApiKeyAuthenticationHandler>();
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Only authenticate for external API routes
            if (!Request.Path.StartsWithSegments("/api/v1/external"))
            {
                return AuthenticateResult.NoResult();
            }

            // 1. Extract API key from headers
            string? apiKey = null;
            
            if (Request.Headers.TryGetValue("X-API-Key", out var xApiKey))
            {
                apiKey = xApiKey.FirstOrDefault();
            }
            else if (Request.Headers.TryGetValue("Authorization", out var auth))
            {
                var authHeader = auth.FirstOrDefault();
                if (authHeader?.StartsWith("Bearer AIO_", StringComparison.OrdinalIgnoreCase) == true)
                {
                    apiKey = authHeader.Substring("Bearer ".Length);
                }
            }
            
            if (string.IsNullOrEmpty(apiKey) || !apiKey.StartsWith("AIO_"))
            {
                return AuthenticateResult.NoResult();
            }
            
            // 2. Validate API key (with caching)
            var keyHash = ExternalApiKeyService.ComputeSha256Hash(apiKey);
            var cacheKey = $"external_api_key_{keyHash}";
            
            ExternalApiKey? keyEntity;
            
            if (!_cache.TryGetValue(cacheKey, out keyEntity))
            {
                keyEntity = await _apiKeyService.ValidateApiKeyAsync(apiKey);
                
                if (keyEntity != null)
                {
                    // Cache for 5 minutes
                    _cache.Set(cacheKey, keyEntity, TimeSpan.FromMinutes(5));
                }
            }
            
            if (keyEntity == null)
            {
                _logger.LogWarning("Invalid or disabled API key attempted: {KeyPrefix}...{KeySuffix}", 
                    apiKey.Substring(0, Math.Min(8, apiKey.Length)),
                    apiKey.Length > 4 ? apiKey.Substring(apiKey.Length - 4) : "");
                return AuthenticateResult.Fail("API key không hợp lệ hoặc đã bị vô hiệu hóa");
            }
            
            // 3. Create claims and principal
            var claims = new[]
            {
                new Claim("api_key_id", keyEntity.Id.ToString()),
                new Claim("api_key_name", keyEntity.DisplayName ?? ""),
                new Claim("assigned_to", keyEntity.AssignedTo ?? ""),
                new Claim(ClaimTypes.AuthenticationMethod, "ExternalApiKey"),
                new Claim("rpm_limit", keyEntity.RpmLimit.ToString())
            };
            
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            
            _logger.LogDebug("Successfully authenticated API key {KeyId} ({DisplayName})", 
                keyEntity.Id, keyEntity.DisplayName ?? "Unnamed");
            
            return AuthenticateResult.Success(ticket);
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            Response.Headers.Add("WWW-Authenticate", "Bearer realm=\"External API\"");
            return Task.CompletedTask;
        }
    }
}
