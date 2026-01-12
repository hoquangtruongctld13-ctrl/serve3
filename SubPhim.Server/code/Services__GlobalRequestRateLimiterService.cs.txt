using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Collections.Concurrent;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// NO-OP Service: Global rate limiting has been removed.
    /// This service is kept for backward compatibility but does nothing.
    /// Rate limiting is now only applied per-key (RPM/RPD) and per-proxy (RPM).
    /// </summary>
    public class GlobalRequestRateLimiterService
    {
        private readonly ILogger<GlobalRequestRateLimiterService> _logger;

        public GlobalRequestRateLimiterService(
            ILogger<GlobalRequestRateLimiterService> logger)
        {
            _logger = logger;
            _logger.LogInformation("GlobalRequestRateLimiter initialized as NO-OP (global rate limiting disabled)");
        }

        /// <summary>
        /// NO-OP: Does nothing. Kept for backward compatibility.
        /// </summary>
        public Task RefreshSettingsAsync(bool forceRefresh = false)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// NO-OP: Returns dummy values indicating unlimited availability. Kept for backward compatibility.
        /// </summary>
        public (int maxRequests, int windowMinutes, int availableSlots, int activeRequests) GetCurrentStatus()
        {
            return (0, 0, int.MaxValue, 0);
        }

        /// <summary>
        /// NO-OP: Returns immediately without blocking. Kept for backward compatibility.
        /// </summary>
        public Task<string> AcquireSlotAsync(string jobId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"{jobId}_noop");
        }

        /// <summary>
        /// NO-OP: Does nothing. Kept for backward compatibility.
        /// </summary>
        public void ReleaseSlot(string requestId)
        {
            // Do nothing
        }

        /// <summary>
        /// NO-OP: Always returns true. Kept for backward compatibility.
        /// </summary>
        public bool HasAvailableSlot()
        {
            return true;
        }

        /// <summary>
        /// NO-OP: Always returns int.MaxValue to indicate unlimited availability. Kept for backward compatibility.
        /// </summary>
        public int GetAvailableSlots()
        {
            return int.MaxValue;
        }
    }
}
