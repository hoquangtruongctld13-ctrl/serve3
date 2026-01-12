
using SubPhim.Server.Data;
using static SubPhim.Server.Controllers.TtsController;

namespace SubPhim.Server.Services
{
    public class TtsOrchestrationResult
    {
        public bool IsSuccess { get; set; }
        // <<< SỬA ĐỔI: Chuyển từ một mảng byte thành một danh sách các mảng byte >>>
        public List<byte[]>? AudioChunks { get; set; }
        public string MimeType { get; set; } = "audio/mpeg";
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public interface ITtsOrchestratorService
    {
        // Thêm tham số systemInstruction
        Task<TtsOrchestrationResult> GenerateTtsAsync(TtsProvider provider, string modelIdentifier, string text, string? voiceId, VoiceSettingsDto? voiceSettings, string? systemInstruction);
    }
}