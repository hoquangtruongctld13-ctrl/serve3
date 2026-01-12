using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Background service để dọn dẹp Vbee TTS sessions cũ/mất kết nối định kỳ
    /// </summary>
    public class VbeeTtsSessionCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<VbeeTtsSessionCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMinutes(2);
        private readonly TimeSpan _sessionMaxAge = TimeSpan.FromHours(24);

        public VbeeTtsSessionCleanupService(
            IServiceProvider serviceProvider,
            ILogger<VbeeTtsSessionCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("VbeeTtsSessionCleanupService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupSessionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during Vbee TTS session cleanup.");
                }

                await Task.Delay(_cleanupInterval, stoppingToken);
            }

            _logger.LogInformation("VbeeTtsSessionCleanupService stopped.");
        }

        private async Task CleanupSessionsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vbeeTtsService = scope.ServiceProvider.GetRequiredService<IVbeeTtsService>();

            var now = DateTime.UtcNow;
            var heartbeatThreshold = now.Subtract(_heartbeatTimeout);
            var ageThreshold = now.Subtract(_sessionMaxAge);

            // 1. Đánh dấu sessions không có heartbeat là Disconnected
            var disconnectedSessions = await context.VbeeTtsSessions
                .Where(s => (s.Status == VbeeTtsSessionStatus.Pending || s.Status == VbeeTtsSessionStatus.Processing)
                    && s.LastHeartbeatAt < heartbeatThreshold)
                .ToListAsync(cancellationToken);

            if (disconnectedSessions.Any())
            {
                foreach (var session in disconnectedSessions)
                {
                    session.Status = VbeeTtsSessionStatus.Disconnected;
                    _logger.LogWarning("Session {SessionId} marked as disconnected due to no heartbeat.", session.SessionId);
                }
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Marked {Count} sessions as disconnected.", disconnectedSessions.Count);
            }

            // 2. Dọn dẹp sessions quá cũ (> 24 giờ)
            var oldSessions = await context.VbeeTtsSessions
                .Where(s => s.CreatedAt < ageThreshold)
                .ToListAsync(cancellationToken);

            if (oldSessions.Any())
            {
                // Hoàn trả quota cho sessions chưa hoàn thành
                foreach (var session in oldSessions)
                {
                    if (session.Status == VbeeTtsSessionStatus.Pending || 
                        session.Status == VbeeTtsSessionStatus.Processing ||
                        session.Status == VbeeTtsSessionStatus.Disconnected)
                    {
                        // Hoàn trả quota đã tính phí
                        if (session.CharactersCharged > 0)
                        {
                            var user = await context.Users.FindAsync(new object[] { session.UserId }, cancellationToken);
                            if (user != null)
                            {
                                user.VbeeCharactersUsed = Math.Max(0, user.VbeeCharactersUsed - session.CharactersCharged);
                                _logger.LogInformation(
                                    "Refunded {CharactersCharged} characters to user {UserId} from expired session {SessionId}.",
                                    session.CharactersCharged, session.UserId, session.SessionId);
                            }
                        }
                    }
                }

                context.VbeeTtsSessions.RemoveRange(oldSessions);
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Cleaned up {Count} old Vbee TTS sessions.", oldSessions.Count);
            }

            // 3. Reset quota hàng tháng cho users có VbeeResetMonthly = true
            await ResetMonthlyQuotasAsync(context, cancellationToken);
        }

        private async Task ResetMonthlyQuotasAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);

            var usersToReset = await context.Users
                .Where(u => u.VbeeResetMonthly 
                    && u.VbeeQuotaStartedAt.HasValue 
                    && u.VbeeQuotaStartedAt.Value <= thirtyDaysAgo)
                .ToListAsync(cancellationToken);

            if (usersToReset.Any())
            {
                foreach (var user in usersToReset)
                {
                    var oldUsed = user.VbeeCharactersUsed;
                    user.VbeeCharactersUsed = 0;
                    user.VbeeQuotaStartedAt = now;
                    user.LastVbeeResetUtc = now;

                    _logger.LogInformation(
                        "Reset Vbee quota for user {UserId}: {OldUsed} -> 0. Next reset at {NextReset}.",
                        user.Id, oldUsed, now.AddDays(30));
                }

                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Reset Vbee quota for {Count} users.", usersToReset.Count);
            }
        }
    }
}
