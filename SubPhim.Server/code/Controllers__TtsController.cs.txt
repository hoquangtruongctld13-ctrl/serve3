// THAY THẾ TOÀN BỘ FILE

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.Diagnostics;
using System.Security.Claims;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TtsController : ControllerBase
    {
        private readonly ITtsOrchestratorService _ttsOrchestrator;
        private readonly ILogger<TtsController> _logger;
        private readonly AppDbContext _context;

        public TtsController(ITtsOrchestratorService ttsOrchestrator, ILogger<TtsController> logger, AppDbContext context)
        {
            _ttsOrchestrator = ttsOrchestrator;
            _logger = logger;
            _context = context; 
        }

        public record VoiceSettingsDto(
     double Stability,
     double Similarity_boost,
     double Style,
     bool Use_speaker_boost
     );

        public record TtsRequest(
     string Provider,
     string Model,
     string Text,
     string? VoiceId,
     VoiceSettingsDto? VoiceSettings,
     string? SystemInstruction // Thêm thuộc tính này
 );

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateTts([FromBody] TtsRequest request)
        {
            Debug.WriteLine($"[TtsController.GenerateTts] Received request from User ID: {User.FindFirstValue("id")}, Provider: {request.Provider}, Text Length: {request.Text?.Length ?? 0}");

            if (!int.TryParse(User.FindFirstValue("id"), out int userId))
            {
                Debug.WriteLine("[TtsController.GenerateTts] Unauthorized: Invalid user ID in token.");
                return Unauthorized();
            }

            int characterCount = request.Text?.Length ?? 0;
            if (characterCount == 0)
            {
                Debug.WriteLine("[TtsController.GenerateTts] BadRequest: Text is empty.");
                return BadRequest("Văn bản không được để trống.");
            }

            // --- BẮT ĐẦU LOGIC KIỂM TRA KÝ TỰ ---
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                Debug.WriteLine($"[TtsController.GenerateTts] NotFound: User with ID {userId} not found.");
                return NotFound("Tài khoản không tồn tại.");
            }

            long remainingChars = user.TtsCharacterLimit - user.TtsCharactersUsed;

            if (characterCount > remainingChars)
            {
                string errorMessage = $"Không đủ ký tự để tạo TTS. Yêu cầu: {characterCount}, còn lại: {remainingChars}.";
                Debug.WriteLine($"[TtsController.GenerateTts] Forbidden (429): User '{user.Username}' has insufficient characters. {errorMessage}");
                return StatusCode(429, new { message = errorMessage });
            }

            // Trừ ký tự và lưu lại
            user.TtsCharactersUsed += characterCount;
            await _context.SaveChangesAsync();
            Debug.WriteLine($"[TtsController.GenerateTts] User '{user.Username}' used {characterCount} characters. New total used: {user.TtsCharactersUsed}/{user.TtsCharacterLimit}. Proceeding to generate audio.");
            // --- KẾT THÚC LOGIC KIỂM TRA KÝ TỰ ---

            if (!Enum.TryParse<TtsProvider>(request.Provider, true, out var providerType))
            {
                Debug.WriteLine($"[TtsController.GenerateTts] BadRequest: Invalid provider '{request.Provider}'.");
                return BadRequest("Provider không hợp lệ.");
            }

            _logger.LogInformation("Nhận yêu cầu TTS từ User ID {UserId} cho Provider {Provider}, Model {Model}, Chars: {CharCount}",
                userId, request.Provider, request.Model, characterCount);

            var result = await _ttsOrchestrator.GenerateTtsAsync(providerType, request.Model, request.Text, request.VoiceId, request.VoiceSettings, request.SystemInstruction);

            if (result.IsSuccess && result.AudioChunks != null && result.AudioChunks.Any())
            {
                if (result.AudioChunks.Count == 1)
                {
                    // Nếu chỉ có 1 chunk, trả về file như cũ
                    Debug.WriteLine($"[TtsController.GenerateTts] Returning single audio chunk ({result.AudioChunks.First().Length} bytes) as a file with MIME type {result.MimeType}.");
                    return File(result.AudioChunks.First(), result.MimeType);
                }
                else
                {
                    // Nếu có nhiều chunk, trả về JSON
                    Debug.WriteLine($"[TtsController.GenerateTts] Returning {result.AudioChunks.Count} audio chunks as JSON.");
                    var responseData = new
                    {
                        isChunked = true,
                        mimeType = result.MimeType,
                        audioChunks = result.AudioChunks.Select(Convert.ToBase64String).ToList()
                    };
                    return Ok(responseData);
                }
            }
            user.TtsCharactersUsed -= characterCount;
            await _context.SaveChangesAsync();
            Debug.WriteLine($"[TtsController.GenerateTts] TTS generation failed. Reverted {characterCount} characters for user '{user.Username}'. New total used: {user.TtsCharactersUsed}. Reason: {result.ErrorMessage}");

            _logger.LogError("Tạo TTS thất bại: {Error}", result.ErrorMessage);
            return StatusCode(503, new { message = "Không thể tạo âm thanh vào lúc này. " + result.ErrorMessage });
        }
    }
}