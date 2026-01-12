using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Helpers; // Giả sử bạn có một helper để chuyển giờ, nếu không sẽ tạo ngay dưới đây

namespace SubPhim.Server.Services
{
    public class AioKeyResetService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AioKeyResetService> _logger;
        private static DateTime _lastResetDate = DateTime.MinValue;

        public AioKeyResetService(IServiceProvider serviceProvider, ILogger<AioKeyResetService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Chờ 1 phút sau khi khởi động để DB ổn định
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var vietnamNow = TimeZoneHelper.ConvertToVietNamTime(DateTime.UtcNow);

                    // Chỉ chạy một lần mỗi ngày, vào hoặc sau 12:30 trưa
                    if (vietnamNow.Hour >= 12 && vietnamNow.Minute >= 30 && vietnamNow.Date > _lastResetDate.Date)
                    {
                        _logger.LogInformation("It's time for the daily AIO Key reset (VN Time). Starting process...");

                        await ResetAioKeys(stoppingToken);

                        _lastResetDate = vietnamNow; // Ghi nhận ngày đã reset
                        _logger.LogInformation("Daily AIO Key reset process finished.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred in the AioKeyResetService execution loop.");
                }

                // Kiểm tra lại sau mỗi 10 phút
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task ResetAioKeys(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Lấy tất cả các key AIO
            var keysToReset = await context.AioApiKeys.ToListAsync(stoppingToken);
            int reactivatedCount = 0;

            if (keysToReset.Any())
            {
                foreach (var key in keysToReset)
                {
                    // Reset bộ đếm
                    key.RequestsToday = 0;
                    key.LastResetUtc = DateTime.UtcNow;

                    // Bật lại các key đã bị tắt do hết RPD
                    if (!key.IsEnabled && key.DisabledReason != null && key.DisabledReason.Contains("RPD limit"))
                    {
                        key.IsEnabled = true;
                        key.DisabledReason = null;
                        reactivatedCount++;
                    }
                }

                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Reset RPD for {TotalCount} AIO keys. Reactivated {ReactivatedCount} keys.", keysToReset.Count, reactivatedCount);
            }
        }
    }

    // Helper chuyển đổi múi giờ, nếu chưa có thì tạo file Helpers/TimeZoneHelper.cs
    namespace SubPhim.Server.Helpers
    {
        public static class TimeZoneHelper
        {
            public static DateTime ConvertToVietNamTime(DateTime utcDateTime)
            {
                try
                {
                    var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, vietnamTimeZone);
                }
                catch (TimeZoneNotFoundException)
                {
                    // Fallback cho môi trường Linux
                    try
                    {
                        var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, vietnamTimeZone);
                    }
                    catch
                    {
                        // Nếu không thể tìm thấy, trả về UTC + 7
                        return utcDateTime.AddHours(7);
                    }
                }
            }
        }
    }
}