using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Background service t? ??ng re-enable keys sau khi h?t cooldown
    /// </summary>
    public class ManagedApiKeyResetService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ManagedApiKeyResetService> _logger;

        public ManagedApiKeyResetService(IServiceProvider serviceProvider, ILogger<ManagedApiKeyResetService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ManagedApiKeyResetService started");
            
            // Ch? 1 phút sau khi kh?i ??ng
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var cooldownService = scope.ServiceProvider.GetRequiredService<ApiKeyCooldownService>();
                    
                    // X? lý expired cooldowns
                    await cooldownService.ProcessExpiredCooldownsAsync();
                    
                    // Reset daily counters n?u c?n
                    await ResetDailyCountersAsync(scope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ManagedApiKeyResetService execution loop");
                }

                // Ch?y m?i 30 giây ?? check cooldown expirations
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            
            _logger.LogInformation("ManagedApiKeyResetService stopped");
        }

        private async Task ResetDailyCountersAsync(IServiceScope scope)
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);

            var keysToReset = await context.ManagedApiKeys
                .Where(k => k.RequestsToday > 0)
                .ToListAsync();

            int resetCount = 0;
            foreach (var key in keysToReset)
            {
                var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(key.LastRequestCountResetUtc, vietnamTimeZone);
                
                if (lastResetInVietnam.Date < vietnamNow.Date)
                {
                    key.RequestsToday = 0;
                    key.LastRequestCountResetUtc = DateTime.UtcNow.Date;
                    resetCount++;
                }
            }

            if (resetCount > 0)
            {
                await context.SaveChangesAsync();
                _logger.LogInformation("Reset daily request count for {Count} managed API keys", resetCount);
            }
        }
    }
}
