using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    public class CleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CleanupService> _logger;
        private readonly VipTranslationService _vipTranslationService;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);

        public CleanupService(
            IServiceProvider serviceProvider, 
            ILogger<CleanupService> logger,
            VipTranslationService vipTranslationService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _vipTranslationService = vipTranslationService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            using var timer = new PeriodicTimer(_period);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation("Cleanup Service is running.");

                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var srtThreshold = DateTime.UtcNow.AddMinutes(-10);
                    var srtJobsToDelete = await context.TranslationJobs
                        .Where(j => (j.Status == JobStatus.Completed || j.Status == JobStatus.Failed) && j.CreatedAt < srtThreshold)
                        .Select(j => j.SessionId)
                        .ToListAsync(stoppingToken);

                    if (srtJobsToDelete.Any())
                    {
                        var deletedRows = await context.TranslationJobs
                            .Where(j => srtJobsToDelete.Contains(j.SessionId))
                            .ExecuteDeleteAsync(stoppingToken);
                        _logger.LogInformation("Cleanup Service successfully deleted {Count} old SRT job(s).", deletedRows);
                    }
                    else
                    {
                        _logger.LogInformation("No old SRT jobs found to delete.");
                    }
                    _logger.LogInformation("Checking for old AIO translation jobs to delete...");
                    var aioThreshold = DateTime.UtcNow.AddMinutes(-5);

                    var aioDeletedRows = await context.AioTranslationJobs
                        .Where(j => (j.Status == AioJobStatus.Completed || j.Status == AioJobStatus.Failed)
                                    && j.CompletedAt != null 
                                    && j.CompletedAt < aioThreshold)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (aioDeletedRows > 0)
                    {
                        _logger.LogInformation("Cleanup Service successfully deleted {Count} old AIO job(s).", aioDeletedRows);
                    }
                    else
                    {
                        _logger.LogInformation("No old AIO jobs found to delete.");
                    }
                    _logger.LogInformation("Checking for old AIO TTS Batch jobs to delete...");
                    var aioTtsThreshold = DateTime.UtcNow.AddHours(-1); // Xóa các job đã hoàn thành/lỗi sau 1 giờ

                    var aioTtsJobsToDelete = await context.AioTtsBatchJobs
                        .Where(j => (j.Status == AioTtsJobStatus.Completed || j.Status == AioTtsJobStatus.Failed)
                                    && j.CreatedAt < aioTtsThreshold)
                        .ToListAsync(stoppingToken);

                    if (aioTtsJobsToDelete.Any())
                    {
                        // Xóa các file vật lý còn sót lại (nếu có)
                        foreach (var job in aioTtsJobsToDelete)
                        {
                            if (!string.IsNullOrEmpty(job.ResultZipFilePath) && File.Exists(job.ResultZipFilePath))
                            {
                                try { File.Delete(job.ResultZipFilePath); } catch { }
                            }
                            if (!string.IsNullOrEmpty(job.OriginalSrtFilePath) && File.Exists(job.OriginalSrtFilePath))
                            {
                                try { File.Delete(job.OriginalSrtFilePath); } catch { }
                            }
                        }

                        // Xóa bản ghi trong DB
                        context.AioTtsBatchJobs.RemoveRange(aioTtsJobsToDelete);
                        var deletedCount = await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Cleanup Service successfully deleted {Count} old AIO TTS Batch job(s).", deletedCount);
                    }
                    else
                    {
                        _logger.LogInformation("No old AIO TTS Batch jobs found to delete.");
                    }

                    // Cleanup stale VIP translation sessions for API key requests
                    _logger.LogInformation("Checking for stale VIP translation API key sessions to cleanup...");
                    var vipCleanedCount = _vipTranslationService.CleanupStaleApiKeySessions();
                    if (vipCleanedCount > 0)
                    {
                        _logger.LogInformation("Cleanup Service successfully cleaned up {Count} stale VIP translation API key session(s).", vipCleanedCount);
                    }
                    else
                    {
                        _logger.LogInformation("No stale VIP translation API key sessions found to cleanup.");
                    }

                    _logger.LogInformation("Performing database WAL checkpoint...");
                    var connection = context.Database.GetDbConnection() as SqliteConnection;
                    if (connection != null)
                    {
                        await using var command = connection.CreateCommand();
                        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        try
                        {
                            await connection.OpenAsync(stoppingToken);
                            await command.ExecuteNonQueryAsync(stoppingToken);
                            _logger.LogInformation("WAL checkpoint completed successfully.");
                        }
                        finally
                        {
                            await connection.CloseAsync();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Could not perform WAL checkpoint because the DB connection is not a SqliteConnection.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred in the Cleanup Service.");
                }
            }
        }
    }
}