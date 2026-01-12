using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubPhim.Server.Models;
using SubPhim.Server.Services;
using System.Security.Claims;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/viptranslation")]
    [Authorize]
    public class VipTranslationController : ControllerBase
    {
        private readonly VipTranslationService _vipTranslationService;
        private readonly ILogger<VipTranslationController> _logger;

        public VipTranslationController(
            VipTranslationService vipTranslationService,
            ILogger<VipTranslationController> logger)
        {
            _vipTranslationService = vipTranslationService;
            _logger = logger;
        }

        public record StartVipTranslationRequest(string TargetLanguage, List<SrtLine> Lines, string SystemInstruction);
        
        public class StartVipTranslationResponse
        {
            public string Status { get; set; }
            public string? Message { get; set; }
            public string? SessionId { get; set; }
        }

        public record GetVipResultsResponse(List<TranslatedSrtLine> NewLines, bool IsCompleted, string? ErrorMessage);
        
        public record CancelVipJobResponse(bool Success, string Message);

        /// <summary>
        /// Bắt đầu dịch VIP subtitle
        /// </summary>
        [HttpPost("start")]
        public async Task<IActionResult> StartTranslation([FromBody] StartVipTranslationRequest request)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("id"), out int userId))
                    return Unauthorized();

                _logger.LogInformation("VIP Translation request from User ID {UserId} with {LineCount} lines",
                    userId, request.Lines.Count);

                var result = await _vipTranslationService.CreateJobAsync(
                    userId, 
                    request.TargetLanguage, 
                    request.Lines, 
                    request.SystemInstruction);

                if (result.Status == "Accepted")
                {
                    return Ok(new StartVipTranslationResponse 
                    { 
                        Status = result.Status, 
                        SessionId = result.SessionId 
                    });
                }
                else // "Error"
                {
                    return StatusCode(429, new StartVipTranslationResponse 
                    { 
                        Status = result.Status, 
                        Message = result.Message 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Critical error in VIP Translation start endpoint");
                return StatusCode(500, new StartVipTranslationResponse 
                { 
                    Status = "Error", 
                    Message = $"Lỗi nghiêm trọng: {ex.Message}" 
                });
            }
        }

        /// <summary>
        /// Lấy kết quả dịch VIP theo sessionId
        /// </summary>
        [HttpGet("result/{sessionId}")]
        public async Task<IActionResult> GetResults(string sessionId)
        {
            var newLines = await _vipTranslationService.GetResultsAsync(sessionId);
            var (isCompleted, errorMessage) = await _vipTranslationService.GetStatusAsync(sessionId);

            if (newLines == null && isCompleted)
            {
                return NotFound("Session không hợp lệ hoặc đã hết hạn.");
            }

            return Ok(new GetVipResultsResponse(newLines ?? new List<TranslatedSrtLine>(), isCompleted, errorMessage));
        }

        /// <summary>
        /// Hủy job dịch VIP đang chạy
        /// </summary>
        [HttpPost("cancel/{sessionId}")]
        public async Task<IActionResult> CancelJob(string sessionId)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("id"), out int userId))
                    return Unauthorized();

                _logger.LogInformation("User ID {UserId} requested to cancel VIP job {SessionId}", userId, sessionId);

                bool success = await _vipTranslationService.CancelJobAsync(sessionId, userId);

                if (success)
                {
                    return Ok(new CancelVipJobResponse(true, "Đã hủy job thành công. Lượt dịch chưa sử dụng đã được hoàn trả."));
                }
                else
                {
                    return BadRequest(new CancelVipJobResponse(false, "Không thể hủy job. Job không tồn tại hoặc đã hoàn thành."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling VIP job {SessionId}", sessionId);
                return StatusCode(500, new CancelVipJobResponse(false, $"Lỗi hệ thống: {ex.Message}"));
            }
        }
    }
}
