using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace SubPhim.Server.Middleware
{
    /// <summary>
    /// Middleware for rate limiting External API requests based on RPM (Requests Per Minute)
    /// Uses sliding window algorithm to enforce rate limits per API key
    /// </summary>
    public class ExternalApiRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ExternalApiRateLimitMiddleware> _logger;

        public ExternalApiRateLimitMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<ExternalApiRateLimitMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only apply rate limiting to external API routes
            if (!context.Request.Path.StartsWithSegments("/api/v1/external"))
            {
                await _next(context);
                return;
            }
            
            var apiKeyId = context.User.FindFirstValue("api_key_id");
            if (string.IsNullOrEmpty(apiKeyId))
            {
                // No API key authenticated, let it pass (will be handled by authentication)
                await _next(context);
                return;
            }
            
            var rpmLimitStr = context.User.FindFirstValue("rpm_limit");
            if (!int.TryParse(rpmLimitStr, out int rpmLimit))
            {
                rpmLimit = 100; // Default
            }
            
            // Sliding window rate limiting
            var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            var windowKey = $"rpm_{apiKeyId}_{currentMinute}";
            
            var currentCount = _cache.GetOrCreate(windowKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return 0;
            });
            
            if (currentCount >= rpmLimit)
            {
                _logger.LogWarning("Rate limit exceeded for API key {ApiKeyId}: {Count}/{Limit} requests", 
                    apiKeyId, currentCount, rpmLimit);
                
                context.Response.StatusCode = 429;
                context.Response.Headers.Add("Retry-After", "60");
                context.Response.Headers.Add("X-RateLimit-Limit", rpmLimit.ToString());
                context.Response.Headers.Add("X-RateLimit-Remaining", "0");
                context.Response.Headers.Add("X-RateLimit-Reset", GetResetTimestamp().ToString());
                
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "RateLimited",
                    retryAfter = 60,
                    message = $"Vượt quá giới hạn {rpmLimit} requests/phút. Vui lòng thử lại sau."
                });
                return;
            }
            
            // Increment counter
            _cache.Set(windowKey, currentCount + 1, TimeSpan.FromMinutes(2));
            
            // Add rate limit headers to response
            var remaining = rpmLimit - currentCount - 1;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Add("X-RateLimit-Limit", rpmLimit.ToString());
                context.Response.Headers.Add("X-RateLimit-Remaining", Math.Max(0, remaining).ToString());
                context.Response.Headers.Add("X-RateLimit-Reset", GetResetTimestamp().ToString());
                return Task.CompletedTask;
            });
            
            await _next(context);
        }

        private long GetResetTimestamp()
        {
            var nextMinute = DateTime.UtcNow.AddMinutes(1);
            var startOfNextMinute = new DateTime(nextMinute.Year, nextMinute.Month, nextMinute.Day, 
                nextMinute.Hour, nextMinute.Minute, 0, DateTimeKind.Utc);
            return new DateTimeOffset(startOfNextMinute).ToUnixTimeSeconds();
        }
    }
}
