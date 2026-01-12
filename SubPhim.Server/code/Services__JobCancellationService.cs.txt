using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service quản lý CancellationToken cho các job dịch SRT.
    /// Cho phép user hủy job đang chạy từ client.
    /// </summary>
    public class JobCancellationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<JobCancellationService> _logger;

        // Lưu trữ CancellationTokenSource cho mỗi job theo sessionId (instance field thay vì static)
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();

        // Lưu trữ mapping userId -> danh sách sessionId để hủy nhanh tất cả job của một user
        // Sử dụng ConcurrentDictionary<string, bool> thay vì ConcurrentBag để hỗ trợ O(1) removal
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, bool>> _userJobs = new();

        public JobCancellationService(IServiceProvider serviceProvider, ILogger<JobCancellationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Đăng ký một job mới và tạo CancellationTokenSource cho job đó.
        /// </summary>
        /// <param name="sessionId">ID của job</param>
        /// <param name="userId">ID của user sở hữu job</param>
        /// <param name="timeoutMinutes">Thời gian timeout tự động (phút)</param>
        /// <returns>CancellationToken để sử dụng trong xử lý job</returns>
        public CancellationToken RegisterJob(string sessionId, int userId, int timeoutMinutes = 15)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));

            if (_jobCancellationTokens.TryAdd(sessionId, cts))
            {
                // Thêm sessionId vào danh sách job của user (O(1) operation)
                var userJobs = _userJobs.GetOrAdd(userId, _ => new ConcurrentDictionary<string, bool>());
                userJobs.TryAdd(sessionId, true);

                _logger.LogInformation("Registered job {SessionId} for User ID {UserId} with {Timeout} minute timeout",
                    sessionId, userId, timeoutMinutes);

                return cts.Token;
            }

            _logger.LogWarning("Job {SessionId} already registered, returning existing token", sessionId);
            return _jobCancellationTokens[sessionId].Token;
        }

        /// <summary>
        /// Hủy một job theo sessionId.
        /// </summary>
        /// <param name="sessionId">ID của job cần hủy</param>
        /// <param name="userId">ID của user (để xác thực quyền hủy)</param>
        /// <returns>Tuple (Success, ErrorCode, Message) - ErrorCode: null nếu thành công, "NOT_FOUND", "FORBIDDEN", "ALREADY_COMPLETED", "ALREADY_FAILED"</returns>
        public async Task<(bool Success, string? ErrorCode, string Message)> CancelJobAsync(string sessionId, int userId)
        {
            // Kiểm tra quyền sở hữu job
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await context.TranslationJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.SessionId == sessionId);

            if (job == null)
            {
                _logger.LogWarning("Cancel request for non-existent job {SessionId}", sessionId);
                return (false, "NOT_FOUND", "Không tìm thấy job.");
            }

            if (job.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to cancel job {SessionId} owned by User {OwnerId}",
                    userId, sessionId, job.UserId);
                return (false, "FORBIDDEN", "Bạn không có quyền hủy job này.");
            }

            // Kiểm tra trạng thái job
            if (job.Status == JobStatus.Completed)
            {
                return (false, "ALREADY_COMPLETED", "Job đã hoàn thành, không thể hủy.");
            }

            if (job.Status == JobStatus.Failed)
            {
                return (false, "ALREADY_FAILED", "Job đã thất bại trước đó.");
            }

            // Hủy CancellationToken
            if (_jobCancellationTokens.TryRemove(sessionId, out var cts))
            {
                try
                {
                    cts.Cancel();
                    _logger.LogInformation("Successfully cancelled job {SessionId} for User ID {UserId}", sessionId, userId);
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogWarning("CancellationTokenSource for job {SessionId} was already disposed", sessionId);
                }
                finally
                {
                    cts.Dispose();
                }
            }

            // Cập nhật trạng thái job trong database
            var jobToUpdate = await context.TranslationJobs.FindAsync(sessionId);
            if (jobToUpdate != null)
            {
                // ✅ Chỉ update nếu đang Processing/Pending
                if (jobToUpdate.Status == JobStatus.Processing || jobToUpdate.Status == JobStatus.Pending)
                {
                    jobToUpdate.Status = JobStatus.Failed;
                    jobToUpdate.ErrorMessage = "Job đã bị hủy bởi người dùng.";
                    await context.SaveChangesAsync();
                    _logger.LogInformation("✅ Cancel: Job {SessionId} -> Failed", sessionId);
                }
            }
            // Hoàn trả lượt dịch cho user
            await RefundCancelledJobAsync(sessionId, userId);

            return (true, null, "Đã hủy job thành công.");
        }

        /// <summary>
        /// Hủy tất cả job đang chạy của một user.
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <returns>Số lượng job đã hủy</returns>
        public async Task<int> CancelAllUserJobsAsync(int userId)
        {
            int cancelledCount = 0;

            if (_userJobs.TryGetValue(userId, out var userJobs))
            {
                // Lấy danh sách sessionIds từ keys của dictionary
                var sessionIds = userJobs.Keys.ToArray();

                foreach (var sessionId in sessionIds)
                {
                    var result = await CancelJobAsync(sessionId, userId);
                    if (result.Success)
                    {
                        cancelledCount++;
                    }
                }
            }

            _logger.LogInformation("Cancelled {Count} jobs for User ID {UserId}", cancelledCount, userId);
            return cancelledCount;
        }

        /// <summary>
        /// Kiểm tra xem job có bị hủy không.
        /// </summary>
        public bool IsJobCancelled(string sessionId)
        {
            if (_jobCancellationTokens.TryGetValue(sessionId, out var cts))
            {
                return cts.IsCancellationRequested;
            }
            return true; // Nếu không tìm thấy, coi như đã bị hủy hoặc hoàn thành
        }

        /// <summary>
        /// Lấy CancellationToken cho một job (dùng khi cần truy cập token sau khi đăng ký).
        /// </summary>
        public CancellationToken? GetJobToken(string sessionId)
        {
            if (_jobCancellationTokens.TryGetValue(sessionId, out var cts))
            {
                return cts.Token;
            }
            return null;
        }

        /// <summary>
        /// Hủy đăng ký job khi job hoàn thành (giải phóng bộ nhớ).
        /// </summary>
        public void UnregisterJob(string sessionId, int userId)
        {
            if (_jobCancellationTokens.TryRemove(sessionId, out var cts))
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Đã được dispose trước đó
                }

                _logger.LogDebug("Unregistered job {SessionId}", sessionId);
            }

            // Xóa khỏi danh sách user jobs với O(1) operation
            if (_userJobs.TryGetValue(userId, out var userJobs))
            {
                userJobs.TryRemove(sessionId, out _);
            }
        }

        /// <summary>
        /// Hoàn trả lượt dịch cho user khi hủy job.
        /// </summary>
        private async Task RefundCancelledJobAsync(string sessionId, int userId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await context.TranslationJobs
                    .Include(j => j.OriginalLines)
                    .Include(j => j.TranslatedLines)
                    .FirstOrDefaultAsync(j => j.SessionId == sessionId);

                if (job == null || job.HasRefunded)
                {
                    return;
                }

                // Tính số dòng chưa được dịch thành công
                var totalLines = job.OriginalLines?.Count ?? 0;
                var successfulLines = job.TranslatedLines?.Count(l => l.Success) ?? 0;
                var linesToRefund = totalLines - successfulLines;

                if (linesToRefund > 0)
                {
                    var user = await context.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.LocalSrtLinesUsedToday = Math.Max(0, user.LocalSrtLinesUsedToday - linesToRefund);
                        _logger.LogInformation("Refunded {Lines} lines to User ID {UserId} after cancellation. New usage: {Usage}/{Limit}",
                            linesToRefund, userId, user.LocalSrtLinesUsedToday, user.DailyLocalSrtLimit);
                    }
                }

                job.HasRefunded = true;
                job.FailedLinesCount = totalLines - successfulLines;
                job.ErrorDetails = $"{{\"reason\": \"USER_CANCELLED\", \"totalLines\": {totalLines}, \"successfulLines\": {successfulLines}}}";

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refunding cancelled job {SessionId}", sessionId);
            }
        }

        /// <summary>
        /// Lấy danh sách các job đang chạy của user.
        /// </summary>
        public async Task<List<ActiveJobInfo>> GetUserActiveJobsAsync(int userId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeJobs = await context.TranslationJobs
                .Where(j => j.UserId == userId &&
                           (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing))
                .Select(j => new ActiveJobInfo
                {
                    SessionId = j.SessionId,
                    Status = j.Status.ToString(),
                    CreatedAt = j.CreatedAt,
                    TotalLines = j.OriginalLines.Count
                })
                .ToListAsync();

            return activeJobs;
        }
    }

    /// <summary>
    /// DTO chứa thông tin job đang hoạt động.
    /// </summary>
    public class ActiveJobInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalLines { get; set; }
    }
}
