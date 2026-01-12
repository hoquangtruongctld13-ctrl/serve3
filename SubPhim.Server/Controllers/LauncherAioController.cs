using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubPhim.Server.Models;
using SubPhim.Server.Services;
using System.Security.Claims;

[ApiController]
[Route("api/launcheraio")]
[Authorize]
public class LauncherAioController : ControllerBase
{
    private readonly TranslationOrchestratorService _orchestrator;
    private readonly JobCancellationService _cancellationService;
    private readonly ILogger<LauncherAioController> _logger;

    public LauncherAioController(
        TranslationOrchestratorService orchestrator, 
        JobCancellationService cancellationService,
        ILogger<LauncherAioController> logger)
    {
        _orchestrator = orchestrator;
        _cancellationService = cancellationService;
        _logger = logger;
    }

    // Sửa đổi record này
    public record StartTranslationRequest(string Genre, string TargetLanguage, List<SrtLine> Lines, string SystemInstruction, bool AcceptPartial = false);

    public class StartTranslationResponse
    {
        // Status: "Accepted", "PartialContent", "Error"
        public string Status { get; set; }
        public string Message { get; set; }
        public string SessionId { get; set; }
        public int RemainingLines { get; set; }
    }
    public record GetResultsResponse(List<TranslatedSrtLine> NewLines, bool IsCompleted, string ErrorMessage);

    /// <summary>
    /// Response DTO cho cancel job.
    /// </summary>
    public record CancelJobResponse(bool Success, string Message);

    /// <summary>
    /// Response DTO cho danh sách job đang chạy.
    /// </summary>
    public record ActiveJobsResponse(List<ActiveJobInfo> Jobs);

    [HttpPost("start-translation")]
    public async Task<IActionResult> StartTranslation([FromBody] StartTranslationRequest request)
    {
        try
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId)) return Unauthorized();

            _logger.LogInformation("Received translation job from User ID {UserId} with {LineCount} lines. AcceptPartial={AcceptPartial}",
                userId, request.Lines.Count, request.AcceptPartial);

            // Cập nhật lời gọi service với tham số mới
            var result = await _orchestrator.CreateJobAsync(userId, request.Genre, request.TargetLanguage, request.Lines, request.SystemInstruction, request.AcceptPartial);

            // Dựa vào kết quả từ service để trả về các mã trạng thái khác nhau
            switch (result.Status)
            {
                case "Accepted":
                    // 200 OK: Job được chấp nhận hoàn toàn
                    return Ok(new StartTranslationResponse { Status = result.Status, SessionId = result.SessionId });

                case "PartialContent":
                    // 202 Accepted: Job chưa được tạo, server đang chờ xác nhận từ client
                    return StatusCode(202, new StartTranslationResponse { Status = result.Status, Message = result.Message, RemainingLines = result.RemainingLines });

                default: // "Error"
                    // 429 Too Many Requests: Hết sạch lượt, không thể dịch
                    return StatusCode(429, new StartTranslationResponse { Status = result.Status, Message = result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "!!!!!!!! CRITICAL ERROR in StartTranslation endpoint. !!!!!!!");
            return StatusCode(500, new StartTranslationResponse { Status = "Error", Message = $"Lỗi nghiêm trọng khi tạo job: {ex.Message}" });
        }
    }

    [HttpGet("get-results/{sessionId}")]
    public async Task<IActionResult> GetResults(string sessionId)
    {
        var newLines = await _orchestrator.GetJobResultsAsync(sessionId);
        var (isCompleted, errorMessage) = await _orchestrator.GetJobStatusAsync(sessionId);

        if (newLines == null && isCompleted)
        {
            return NotFound("Session không hợp lệ hoặc đã hết hạn.");
        }

        return Ok(new GetResultsResponse(newLines, isCompleted, errorMessage));
    }

    /// <summary>
    /// Hủy một job dịch đang chạy theo sessionId.
    /// </summary>
    /// <param name="sessionId">ID của job cần hủy</param>
    /// <returns>Kết quả hủy job</returns>
    /// <response code="200">Job đã được hủy thành công</response>
    /// <response code="400">Không thể hủy job (đã hoàn thành hoặc không tồn tại)</response>
    /// <response code="403">Không có quyền hủy job này</response>
    [HttpPost("cancel/{sessionId}")]
    public async Task<IActionResult> CancelJob(string sessionId)
    {
        try
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId))
            {
                return Unauthorized();
            }

            _logger.LogInformation("User ID {UserId} requested to cancel job {SessionId}", userId, sessionId);

            var (success, errorCode, message) = await _cancellationService.CancelJobAsync(sessionId, userId);

            if (success)
            {
                return Ok(new CancelJobResponse(true, message));
            }

            // Sử dụng error code để xác định HTTP status
            return errorCode switch
            {
                "FORBIDDEN" => StatusCode(403, new CancelJobResponse(false, message)),
                "NOT_FOUND" => NotFound(new CancelJobResponse(false, message)),
                _ => BadRequest(new CancelJobResponse(false, message))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling job {SessionId}", sessionId);
            return StatusCode(500, new CancelJobResponse(false, $"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Hủy tất cả các job đang chạy của user hiện tại.
    /// </summary>
    /// <returns>Số lượng job đã hủy</returns>
    [HttpPost("cancel-all")]
    public async Task<IActionResult> CancelAllJobs()
    {
        try
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId))
            {
                return Unauthorized();
            }

            _logger.LogInformation("User ID {UserId} requested to cancel all active jobs", userId);

            var cancelledCount = await _cancellationService.CancelAllUserJobsAsync(userId);

            return Ok(new { Success = true, CancelledJobsCount = cancelledCount, Message = $"Đã hủy {cancelledCount} job." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling all jobs for user");
            return StatusCode(500, new { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lấy danh sách các job đang chạy của user hiện tại.
    /// </summary>
    /// <returns>Danh sách job đang hoạt động</returns>
    [HttpGet("active-jobs")]
    public async Task<IActionResult> GetActiveJobs()
    {
        try
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId))
            {
                return Unauthorized();
            }

            var activeJobs = await _cancellationService.GetUserActiveJobsAsync(userId);

            return Ok(new ActiveJobsResponse(activeJobs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active jobs");
            return StatusCode(500, new { Success = false, Message = $"Lỗi hệ thống: {ex.Message}" });
        }
    }
}