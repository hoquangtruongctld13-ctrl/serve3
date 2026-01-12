// VỊ TRÍ: Controllers/VbeeTtsController.cs
// Controller cho Vbee TTS với obfuscation, fake endpoints và SSE cho realtime tracking

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SubPhim.Server.Controllers
{
    [Route("api/vbeettsbridge")]
    [ApiController]
    [Authorize]
    public class VbeeTtsController : ControllerBase
    {
        private readonly IVbeeTtsService _vbeeTtsService;
        private readonly AppDbContext _context;
        private readonly ILogger<VbeeTtsController> _logger;

        public VbeeTtsController(
            IVbeeTtsService vbeeTtsService,
            AppDbContext context,
            ILogger<VbeeTtsController> logger)
        {
            _vbeeTtsService = vbeeTtsService;
            _context = context;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst("id")?.Value;
            return int.TryParse(userIdClaim, out var id) ? id : 0;
        }

        #region Voices (Encrypted responses)

        /// <summary>
        /// Lấy danh sách voices với voice ID đã được obfuscate
        /// </summary>
        [HttpGet("voices")]
        public async Task<IActionResult> GetVoices()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var voices = await _vbeeTtsService.GetVoicesForClientAsync(userId);
            
            // Trả về danh sách voices trực tiếp (voice ID đã được obfuscate trong VbeeTtsService)
            return Ok(new
            {
                voices,
                total = voices.Count
            });
        }

        #endregion

        #region Quota

        /// <summary>
        /// Lấy thông tin quota Vbee của user
        /// </summary>
        [HttpGet("quota")]
        public async Task<IActionResult> GetQuota()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var quota = await _vbeeTtsService.GetUserQuotaAsync(userId);
            return Ok(quota);
        }

        /// <summary>
        /// Check nếu user đủ quota cho request
        /// </summary>
        [HttpPost("check-quota")]
        public async Task<IActionResult> CheckQuota([FromBody] CheckQuotaRequest request)
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var hasQuota = await _vbeeTtsService.CheckUserHasQuotaAsync(userId, request.EstimatedCharacters);
            return Ok(new { hasQuota, estimatedCharacters = request.EstimatedCharacters });
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Khởi tạo session TTS - trả về token và config (không có API URL thực)
        /// </summary>
        [HttpPost("init-session")]
        public async Task<IActionResult> InitializeSession([FromBody] InitSessionRequest request)
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            // Decrypt voice ID nếu được encrypt từ client
            var voiceId = request.VoiceId;
            if (request.Encrypted)
            {
                voiceId = _vbeeTtsService.DecryptFromClient(request.VoiceId);
            }

            var result = await _vbeeTtsService.InitializeSessionAsync(userId, voiceId, request.EstimatedCharacters);
            if (result == null)
            {
                return BadRequest(new { message = "Không thể khởi tạo session. Quota không đủ hoặc voice không hợp lệ." });
            }

            return Ok(new
            {
                sessionId = result.Value.sessionId,
                token = result.Value.obfuscationToken,
                config = result.Value.config
            });
        }

        /// <summary>
        /// Heartbeat để duy trì session
        /// </summary>
        [HttpPost("heartbeat/{sessionId}")]
        public async Task<IActionResult> Heartbeat(string sessionId, [FromHeader(Name = "X-Vbee-Token")] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var success = await _vbeeTtsService.HeartbeatAsync(sessionId, token);
            return success ? Ok(new { alive = true }) : NotFound();
        }

        /// <summary>
        /// Báo cáo ký tự đã sử dụng (gọi sau mỗi chunk)
        /// </summary>
        [HttpPost("report-usage/{sessionId}")]
        public async Task<IActionResult> ReportUsage(
            string sessionId, 
            [FromHeader(Name = "X-Vbee-Token")] string token,
            [FromBody] ReportUsageRequest request)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var success = await _vbeeTtsService.ReportCharacterUsageAsync(sessionId, token, request.CharactersUsed);
            if (!success)
            {
                return NotFound(new { message = "Session không tồn tại hoặc token không hợp lệ" });
            }

            var quota = await _vbeeTtsService.GetUserQuotaAsync(GetUserId());
            return Ok(new { success = true, remainingQuota = quota.Remaining });
        }

        /// <summary>
        /// Hoàn thành session
        /// </summary>
        [HttpPost("complete/{sessionId}")]
        public async Task<IActionResult> CompleteSession(
            string sessionId,
            [FromHeader(Name = "X-Vbee-Token")] string token,
            [FromBody] CompleteSessionRequest request)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var success = await _vbeeTtsService.CompleteSessionAsync(sessionId, token, request.TotalCharactersUsed);
            if (!success)
            {
                return NotFound(new { message = "Session không tồn tại" });
            }

            var quota = await _vbeeTtsService.GetUserQuotaAsync(GetUserId());
            return Ok(new { success = true, finalQuota = quota });
        }

        /// <summary>
        /// Hủy session và hoàn trả quota
        /// </summary>
        [HttpPost("cancel/{sessionId}")]
        public async Task<IActionResult> CancelSession(
            string sessionId,
            [FromHeader(Name = "X-Vbee-Token")] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var success = await _vbeeTtsService.CancelSessionAsync(sessionId, token);
            if (!success)
            {
                return NotFound(new { message = "Session không tồn tại" });
            }

            var quota = await _vbeeTtsService.GetUserQuotaAsync(GetUserId());
            return Ok(new { success = true, refundedQuota = quota });
        }

        #endregion

        #region TTS Synthesis (Proxy)

        /// <summary>
        /// Proxy endpoint để tạo TTS - gọi đến API thực
        /// </summary>
        [HttpPost("synthesize/{sessionId}")]
        public async Task<IActionResult> Synthesize(
            string sessionId,
            [FromHeader(Name = "X-Vbee-Token")] string token,
            [FromBody] VbeeSynthesizeRequest request)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var (success, audioData, error) = await _vbeeTtsService.SynthesizeAsync(sessionId, token, request);
            
            if (!success)
            {
                return BadRequest(new { message = error });
            }

            // Xác định content type dựa trên format
            var contentType = request.Format?.ToLower() switch
            {
                "wav" => "audio/wav",
                "ogg" => "audio/ogg",
                "flac" => "audio/flac",
                _ => "audio/mpeg"
            };

            return File(audioData, contentType);
        }

        #endregion

        #region SSE for Real-time Updates

        /// <summary>
        /// Server-Sent Events endpoint cho realtime quota updates
        /// </summary>
        [HttpGet("stream/{sessionId}")]
        public async Task StreamSessionUpdates(
            string sessionId,
            [FromQuery] string token,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(token) || !await _vbeeTtsService.ValidateSessionTokenAsync(sessionId, token))
            {
                Response.StatusCode = 401;
                return;
            }

            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            var userId = GetUserId();
            var lastQuotaUsed = -1L;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var session = await _context.VbeeTtsSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

                    if (session == null || 
                        session.Status == VbeeTtsSessionStatus.Completed ||
                        session.Status == VbeeTtsSessionStatus.Cancelled ||
                        session.Status == VbeeTtsSessionStatus.Failed)
                    {
                        await SendSSEEvent("session-ended", new { status = session?.Status.ToString() ?? "NotFound" });
                        break;
                    }

                    var quota = await _vbeeTtsService.GetUserQuotaAsync(userId);
                    
                    // Chỉ gửi khi có thay đổi
                    if (quota.Used != lastQuotaUsed)
                    {
                        lastQuotaUsed = quota.Used;
                        await SendSSEEvent("quota-update", new
                        {
                            quota.Used,
                            quota.Remaining,
                            quota.Limit,
                            session.CharactersProcessed,
                            session.TotalCharactersRequested
                        });
                    }

                    // Heartbeat
                    await SendSSEEvent("heartbeat", new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

                    await Task.Delay(2000, cancellationToken); // Poll every 2 seconds
                }
            }
            catch (TaskCanceledException)
            {
                // Client disconnected - mark session as disconnected
                await _vbeeTtsService.CancelSessionAsync(sessionId, token);
                _logger.LogInformation("Client disconnected from SSE stream for session {SessionId}", sessionId);
            }
        }

        private async Task SendSSEEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            await Response.WriteAsync($"event: {eventName}\n");
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }

        #endregion

        #region Recovery

        /// <summary>
        /// Phục hồi session sau khi mất kết nối
        /// </summary>
        [HttpPost("recover/{sessionId}")]
        public async Task<IActionResult> RecoverSession(
            string sessionId,
            [FromHeader(Name = "X-Vbee-Token")] string token,
            [FromBody] RecoverSessionRequest request)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var session = await _context.VbeeTtsSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.ObfuscationToken == token);

            if (session == null)
            {
                return NotFound(new { message = "Session không tồn tại" });
            }

            if (session.Status != VbeeTtsSessionStatus.Disconnected)
            {
                return BadRequest(new { message = "Session không ở trạng thái mất kết nối" });
            }

            // Verify backup data
            if (!string.IsNullOrEmpty(request.EncryptedBackupData))
            {
                var decryptedBackup = _vbeeTtsService.DecryptFromClient(request.EncryptedBackupData);
                if (decryptedBackup != _vbeeTtsService.DecryptFromClient(session.EncryptedBackupData ?? ""))
                {
                    return BadRequest(new { message = "Dữ liệu backup không khớp" });
                }
            }

            // Cập nhật với dữ liệu từ client
            session.CharactersProcessed = request.CharactersUsed;
            session.Status = VbeeTtsSessionStatus.Processing;
            session.LastHeartbeatAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Session đã được phục hồi" });
        }

        #endregion

        #region Helpers

        private string GenerateResponseSignature(object data)
        {
            var json = JsonSerializer.Serialize(data);
            using var hmac = new System.Security.Cryptography.HMACSHA256(
                Encoding.UTF8.GetBytes("VbeeTtsResponseSignature2025"));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }

        #endregion
    }

    #region Request DTOs

    public class CheckQuotaRequest
    {
        public long EstimatedCharacters { get; set; }
    }

    public class InitSessionRequest
    {
        public string VoiceId { get; set; } = string.Empty;
        public long EstimatedCharacters { get; set; }
        public bool Encrypted { get; set; } = false;
    }

    public class ReportUsageRequest
    {
        public long CharactersUsed { get; set; }
    }

    public class CompleteSessionRequest
    {
        public long TotalCharactersUsed { get; set; }
    }

    public class RecoverSessionRequest
    {
        public long CharactersUsed { get; set; }
        public string? EncryptedBackupData { get; set; }
    }

    #endregion
}
