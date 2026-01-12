// THAY THẾ TOÀN BỘ FILE

using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Helpers;

namespace SubPhim.Server.Services
{
    public class TtsKeyResetService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TtsKeyResetService> _logger;
        private static DateTime _lastDailyResetDate = DateTime.MinValue;
        // <<< THÊM MỚI >>>
        private static DateTime _lastMonthlyResetDate = DateTime.MinValue;

        public TtsKeyResetService(IServiceProvider serviceProvider, ILogger<TtsKeyResetService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var vietnamNow = TimeZoneHelper.ConvertToVietNamTime(DateTime.UtcNow);

                    // --- LOGIC RESET HÀNG NGÀY CHO GEMINI ---
                    if (vietnamNow.Hour >= 0 && vietnamNow.Date > _lastDailyResetDate.Date)
                    {
                        _logger.LogInformation("Đã sang ngày mới (VN). Bắt đầu quá trình reset RPD cho Gemini...");
                        await ResetDailyLimits(stoppingToken);
                        _lastDailyResetDate = vietnamNow;
                    }

                    // --- LOGIC RESET HÀNG THÁNG CHO ELEVENLABS ---
                    if (vietnamNow.Day == 1 && vietnamNow.Month > _lastMonthlyResetDate.Month)
                    {
                        _logger.LogInformation("Đã sang tháng mới (VN). Bắt đầu quá trình reset ký tự cho ElevenLabs...");
                        await ResetMonthlyLimits(stoppingToken);
                        _lastMonthlyResetDate = vietnamNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong TtsKeyResetService.");
                }

                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task ResetDailyLimits(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var geminiKeys = await context.TtsApiKeys
                .Where(k => k.Provider == TtsProvider.Gemini)
                .ToListAsync(stoppingToken);

            if (geminiKeys.Any())
            {
                foreach (var key in geminiKeys)
                {
                    key.RequestsToday = 0;
                    key.LastResetUtc = DateTime.UtcNow;
                    if (!key.IsEnabled && key.DisabledReason != null && key.DisabledReason.Contains("RPD"))
                    {
                        key.IsEnabled = true;
                        key.DisabledReason = null;
                    }
                }
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Đã reset RPD cho {Count} API key của Gemini.", geminiKeys.Count);
            }
        }

        private async Task ResetMonthlyLimits(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var elevenLabsKeys = await context.TtsApiKeys
                .Where(k => k.Provider == TtsProvider.ElevenLabs)
                .ToListAsync(stoppingToken);

            if (elevenLabsKeys.Any())
            {
                foreach (var key in elevenLabsKeys)
                {
                    key.CharactersUsed = 0;
                    key.LastResetUtc = DateTime.UtcNow;
                    // Kích hoạt lại các key đã bị tắt do hết ký tự
                    if (!key.IsEnabled && key.DisabledReason != null && key.DisabledReason.Contains("ký tự"))
                    {
                        key.IsEnabled = true;
                        key.DisabledReason = null;
                    }
                }
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Đã reset giới hạn ký tự cho {Count} API key của ElevenLabs.", elevenLabsKeys.Count);
            }
        }
    }
}