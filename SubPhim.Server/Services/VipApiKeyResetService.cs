using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Background service tự động re-enable VIP API keys sau khi hết cooldown.
    /// Hoạt động giống ManagedApiKeyResetService cho LocalAPI.
    /// </summary>
    public class VipApiKeyResetService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<VipApiKeyResetService> _logger;

        public VipApiKeyResetService(IServiceProvider serviceProvider, ILogger<VipApiKeyResetService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("VipApiKeyResetService started");
            
            // Chờ 1 phút sau khi khởi động
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var cooldownService = scope.ServiceProvider.GetRequiredService<VipApiKeyCooldownService>();
                    
                    // Xử lý expired cooldowns
                    await cooldownService.ProcessExpiredCooldownsAsync();
                    
                    // Reset daily counters nếu cần
                    await ResetDailyCountersAsync(scope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in VipApiKeyResetService execution loop");
                }

                // Chạy mỗi 30 giây để check cooldown expirations
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            
            _logger.LogInformation("VipApiKeyResetService stopped");
        }

        private async Task ResetDailyCountersAsync(IServiceScope scope)
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);

            var keysToReset = await context.VipApiKeys
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
                _logger.LogInformation("Reset daily request count for {Count} VIP API keys", resetCount);
            }
        }
    }
}
