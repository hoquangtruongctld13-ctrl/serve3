using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static SubPhim.Server.Controllers.TtsController;

namespace SubPhim.Server.Services
{
    #region Gemini TTS API Models (Dựa trên code mẫu)
    // Các lớp này dùng để xây dựng payload và parse response cho Gemini TTS API
    internal class GeminiTtsRequest
    {
        [JsonPropertyName("contents")]
        public List<ContentPart> Contents { get; set; }

        [JsonPropertyName("generationConfig")]
        public GenerationConfigData GenerationConfig { get; set; }
    }

    internal class ContentPart
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("parts")]
        public List<PartData> Parts { get; set; }
    }

    internal class PartData
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    internal class GenerationConfigData
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.1f; // Nhiệt độ thấp cho giọng đọc ổn định

        [JsonPropertyName("responseModalities")]
        public List<string> ResponseModalities { get; set; } // << YẾU TỐ QUAN TRỌNG NHẤT

        [JsonPropertyName("speechConfig")]
        public SpeechConfigData SpeechConfig { get; set; }
    }

    internal class SpeechConfigData
    {
        [JsonPropertyName("voiceConfig")]
        public VoiceConfigData VoiceConfig { get; set; }
    }

    internal class VoiceConfigData
    {
        [JsonPropertyName("prebuiltVoiceConfig")]
        public PrebuiltVoiceConfigData PrebuiltVoiceConfig { get; set; }
    }

    internal class PrebuiltVoiceConfigData
    {
        [JsonPropertyName("voiceName")]
        public string VoiceName { get; set; }
    }

    // Các lớp để parse response từ stream
    internal class GeminiStreamResponse { [JsonPropertyName("candidates")] public List<Candidate> Candidates { get; set; } }
    internal class Candidate { [JsonPropertyName("content")] public ContentPartResponse Content { get; set; } }
    internal class ContentPartResponse { [JsonPropertyName("parts")] public List<PartDataResponse> Parts { get; set; } }
    internal class PartDataResponse { [JsonPropertyName("inlineData")] public InlineDataResponse InlineData { get; set; } }
    internal class InlineDataResponse { [JsonPropertyName("mimeType")] public string MimeType { get; set; } [JsonPropertyName("data")] public string Data { get; set; } }

    #endregion

    public class TtsOrchestratorService : ITtsOrchestratorService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TtsOrchestratorService> _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly ITtsSettingsService _ttsSettingsService;

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _rpmLimiters = new();

        public TtsOrchestratorService(
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory,
            ILogger<TtsOrchestratorService> logger,
            IEncryptionService encryptionService,
            ITtsSettingsService ttsSettingsService)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _encryptionService = encryptionService;
            _ttsSettingsService = ttsSettingsService;
        }

        public async Task<TtsOrchestrationResult> GenerateTtsAsync(TtsProvider provider, string modelIdentifier, string text, string? voiceId, VoiceSettingsDto? voiceSettings, string? systemInstruction)
        {
            Debug.WriteLine($"[TtsOrchestrator] Received job. Provider: {provider}, Model/Identifier: {modelIdentifier}, Text Length: {text.Length}");

            if (provider == TtsProvider.ElevenLabs)
            {
                if (string.IsNullOrEmpty(voiceId))
                {
                    Debug.WriteLine("[TtsOrchestrator] Error: Voice ID is required for ElevenLabs.");
                    return new TtsOrchestrationResult { IsSuccess = false, ErrorMessage = "Voice ID là bắt buộc cho ElevenLabs." };
                }

                return await ProcessElevenLabsRequestAsync(modelIdentifier, voiceId, text, voiceSettings);
            }
            else // Gemini
            {
                return await ProcessGeminiRequestAsync(modelIdentifier, text, voiceId, systemInstruction);
            }
        }

        private async Task<TtsOrchestrationResult> ProcessGeminiRequestAsync(string modelIdentifier, string text, string? voiceName, string? systemInstruction)
        {
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                TtsApiKey? apiKeyRecord = null;
                try
                {
                    apiKeyRecord = await GetAvailableKeyAsync(TtsProvider.Gemini, modelIdentifier, 0);
                    if (apiKeyRecord == null)
                    {
                        string errorMessage = $"Hết API key khả dụng cho Gemini (Model: {modelIdentifier}).";
                        _logger.LogWarning(errorMessage);
                        return new TtsOrchestrationResult { IsSuccess = false, ErrorMessage = errorMessage };
                    }

                    var apiKey = _encryptionService.Decrypt(apiKeyRecord.EncryptedApiKey, apiKeyRecord.Iv);
                    var httpClient = _httpClientFactory.CreateClient("TtsClient");

                    var (isSuccess, audioData, mimeType, error) = await CallGeminiTtsAsync(httpClient, apiKey, apiKeyRecord.ModelName!, text, voiceName, systemInstruction);

                    if (isSuccess)
                    {
                        await UpdateKeyUsageAsync(apiKeyRecord.Id, isSuccess: true, 0);
                        _logger.LogInformation("Tạo TTS Gemini thành công cho key ID {KeyId}", apiKeyRecord.Id);
                        return new TtsOrchestrationResult { IsSuccess = true, AudioChunks = new List<byte[]> { audioData }, MimeType = mimeType };
                    }

                    _logger.LogWarning("Lần thử Gemini {Attempt}/{MaxAttempts} thất bại cho key ID {KeyId}. Lỗi: {Error}", i + 1, maxRetries, apiKeyRecord.Id, error);
                    if (error.Contains("401") || error.Contains("Unauthorized") || error.Contains("quota") || error.Contains("API Key không hợp lệ") || error.Contains("INVALID_ARGUMENT"))
                    {
                        await DisableKeyAsync(apiKeyRecord.Id, $"Lỗi API: {error}");
                        i--;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi hệ thống khi xử lý Gemini lần thử {Attempt}/{MaxAttempts}", i + 1, maxRetries);
                    if (apiKeyRecord != null) await DisableKeyAsync(apiKeyRecord.Id, "Lỗi hệ thống nghiêm trọng.");
                }
                await Task.Delay(1000 * (i + 1));
            }
            return new TtsOrchestrationResult { IsSuccess = false, ErrorMessage = "Tạo TTS Gemini thất bại sau nhiều lần thử." };
        }

        private async Task<TtsOrchestrationResult> ProcessElevenLabsRequestAsync(string modelId, string voiceId, string fullText, VoiceSettingsDto? voiceSettings)
        {
            var allAudioChunks = new List<byte[]>();
            var remainingText = fullText;
            int safetyBreak = 20; // Tăng giới hạn để xử lý các đoạn văn bản rất dài

            while (!string.IsNullOrEmpty(remainingText) && safetyBreak > 0)
            {
                safetyBreak--;
                TtsApiKey? apiKeyRecord = null;
                try
                {
                    apiKeyRecord = await GetAvailableKeyAsync(TtsProvider.ElevenLabs, "ElevenLabs", remainingText.Length);
                    if (apiKeyRecord == null)
                    {
                        string errorMessage = "Hết API key ElevenLabs khả dụng hoặc tất cả key đã hết ký tự.";
                        _logger.LogWarning(errorMessage);
                        return new TtsOrchestrationResult { IsSuccess = false, ErrorMessage = errorMessage };
                    }

                    long charsLeftOnKey = apiKeyRecord.CharacterLimit - apiKeyRecord.CharactersUsed;
                    string chunkToSend = GetNextTextChunk(remainingText, charsLeftOnKey);

                    if (string.IsNullOrEmpty(chunkToSend))
                    {
                        Debug.WriteLine($"[ElevenLabs] Key ID {apiKeyRecord.Id} không đủ ký tự. Vô hiệu hóa tạm thời và thử key khác.");
                        await DisableKeyAsync(apiKeyRecord.Id, "Không đủ ký tự còn lại để xử lý.");
                        continue; // Thử với key tiếp theo
                    }

                    Debug.WriteLine($"[ElevenLabs] Using Key ID {apiKeyRecord.Id} (Chars left: {charsLeftOnKey}). Sending chunk of {chunkToSend.Length} chars.");

                    var apiKey = _encryptionService.Decrypt(apiKeyRecord.EncryptedApiKey, apiKeyRecord.Iv);
                    var httpClient = _httpClientFactory.CreateClient("TtsClient");

                    var (isSuccess, audioData, error) = await CallElevenLabsTtsAsync(httpClient, apiKey, voiceId, modelId, chunkToSend, voiceSettings);

                    if (isSuccess)
                    {
                        allAudioChunks.Add(audioData);
                        remainingText = remainingText.Substring(chunkToSend.Length).TrimStart();
                        await UpdateKeyUsageAsync(apiKeyRecord.Id, isSuccess: true, chunkToSend.Length);
                        Debug.WriteLine($"[ElevenLabs] Chunk successful. Remaining text length: {remainingText.Length}");
                    }
                    else
                    {
                        _logger.LogWarning("Lỗi API ElevenLabs cho key ID {KeyId}: {Error}. Vô hiệu hóa key và thử lại.", apiKeyRecord.Id, error);
                        await DisableKeyAsync(apiKeyRecord.Id, $"Lỗi API: {error}");
                        // Không giảm `remainingText`, vòng lặp sẽ thử lại chunk này với key khác
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi hệ thống khi xử lý ElevenLabs.");
                    if (apiKeyRecord != null) await DisableKeyAsync(apiKeyRecord.Id, "Lỗi hệ thống nghiêm trọng.");
                    await Task.Delay(1000); // Chờ một chút trước khi thử lại
                }
            }

            if (allAudioChunks.Any())
            {
                Debug.WriteLine($"[ElevenLabs] Processing complete. Returning {allAudioChunks.Count} audio chunks.");
                return new TtsOrchestrationResult { IsSuccess = true, AudioChunks = allAudioChunks, MimeType = "audio/mpeg" };
            }

            string finalError = string.IsNullOrEmpty(remainingText) ? "Tạo TTS ElevenLabs thất bại." : "Không thể hoàn thành TTS cho toàn bộ văn bản do hết key hợp lệ.";
            return new TtsOrchestrationResult { IsSuccess = false, ErrorMessage = finalError };
        }

        private string GetNextTextChunk(string text, long charLimit)
        {
            if (charLimit <= 0) return string.Empty;

            if (text.Length <= charLimit)
            {
                return text;
            }

            string potentialChunk = text.Substring(0, (int)charLimit);
            int lastPunctuation = potentialChunk.LastIndexOfAny(new[] { '.', '!', '?', '\n' });

            if (lastPunctuation > 0)
            {
                return potentialChunk.Substring(0, lastPunctuation + 1);
            }

            int lastSpace = potentialChunk.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                return potentialChunk.Substring(0, lastSpace + 1);
            }

            return potentialChunk;
        }
        private static string Capitalize(string voice)
        {
            if (string.IsNullOrEmpty(voice)) return voice;
            return char.ToUpper(voice[0], CultureInfo.InvariantCulture) + voice.Substring(1).ToLowerInvariant();
        }
        private async Task<(bool isSuccess, byte[] audioData, string mimeType, string error)> CallGeminiTtsAsync(HttpClient client, string apiKey, string modelName, string text, string voiceName, string? systemInstruction)
        {
            var requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:streamGenerateContent?key={apiKey}";
            var payloadObject = new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text } } } },
                system_instruction = string.IsNullOrWhiteSpace(systemInstruction) ? null : new { parts = new[] { new { text = systemInstruction } } },
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    temperature = 0.1f,
                    speech_config = new { voice_config = new { prebuilt_voice_config = new { voice_name = Capitalize(voiceName) } } }
                }
            };

            var serializerOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            string jsonRequestBody = JsonSerializer.Serialize(payloadObject, serializerOptions);
            var httpContent = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");

            try
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = httpContent };
                using (var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        return (false, null, null, $"Lỗi Gemini API ({(int)response.StatusCode}): {errorContent}");
                    }

                    using (var memoryStream = new MemoryStream())
                    {
                        string mimeType = "audio/L16;rate=24000";
                        using (var responseStream = await response.Content.ReadAsStreamAsync())
                        {
                            var jsonBuffer = new StringBuilder();
                            int braceDepth = 0;
                            var buffer = new byte[8192];
                            int bytesRead;
                            while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                string chunkString = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                foreach (char c in chunkString)
                                {
                                    if (c == '{') { if (braceDepth == 0) jsonBuffer.Clear(); braceDepth++; }
                                    if (braceDepth > 0) jsonBuffer.Append(c);
                                    if (c == '}')
                                    {
                                        braceDepth--;
                                        if (braceDepth == 0 && jsonBuffer.Length > 0)
                                        {
                                            string completeJson = jsonBuffer.ToString();
                                            try
                                            {
                                                var streamResponse = JsonSerializer.Deserialize<GeminiStreamResponse>(completeJson);
                                                var audioPart = streamResponse?.Candidates?.SelectMany(c => c.Content?.Parts ?? Enumerable.Empty<PartDataResponse>()).FirstOrDefault(p => p.InlineData?.Data != null);
                                                if (audioPart != null)
                                                {
                                                    if (memoryStream.Length == 0) mimeType = audioPart.InlineData.MimeType;
                                                    byte[] audioChunkBytes = Convert.FromBase64String(audioPart.InlineData.Data);
                                                    await memoryStream.WriteAsync(audioChunkBytes, 0, audioChunkBytes.Length);
                                                }
                                            }
                                            catch (JsonException jsonEx) { _logger.LogWarning(jsonEx, "Bỏ qua chunk JSON không hợp lệ từ stream Gemini. JSON: {JsonChunk}", completeJson); }
                                            jsonBuffer.Clear();
                                        }
                                    }
                                }
                            }
                        }
                        if (memoryStream.Length > 0)
                        {
                            return (true, memoryStream.ToArray(), mimeType, null);
                        }
                        return (false, null, null, "Phản hồi từ Gemini không chứa dữ liệu âm thanh.");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, null, $"Lỗi mạng khi gọi Gemini: {ex.Message}");
            }
        }

        private async Task<(bool isSuccess, byte[] audioData, string error)> CallElevenLabsTtsAsync(HttpClient client, string apiKey, string voiceId, string modelId, string text, VoiceSettingsDto? voiceSettings)
        {
            var requestUrl = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

            // <<< BẮT ĐẦU SỬA ĐỔI: Xây dựng payload động >>>
            object payload;
            if (voiceSettings != null)
            {
                // Nếu có voiceSettings, tạo payload đầy đủ
                payload = new
                {
                    text,
                    model_id = modelId,
                    voice_settings = new
                    {
                        stability = voiceSettings.Stability,
                        similarity_boost = voiceSettings.Similarity_boost,
                        style = voiceSettings.Style,
                        use_speaker_boost = voiceSettings.Use_speaker_boost
                    }
                };
                Debug.WriteLine("[CallElevenLabsTtsAsync] Sending request with detailed voice settings.");
            }
            else
            {
                // Nếu không có, tạo payload đơn giản
                payload = new { text, model_id = modelId };
                Debug.WriteLine("[CallElevenLabsTtsAsync] Sending request with simple payload (no voice settings).");
            }
            // <<< KẾT THÚC SỬA ĐỔI >>>

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Add("xi-api-key", apiKey);

                // Sử dụng System.Text.Json để serialize
                var jsonPayload = JsonSerializer.Serialize(payload);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    byte[] audioData = await response.Content.ReadAsByteArrayAsync();
                    return (true, audioData, null);
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                return (false, null, $"Lỗi ElevenLabs API ({(int)response.StatusCode}): {errorBody}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Lỗi mạng khi gọi ElevenLabs: {ex.Message}");
            }
        }

        private async Task<TtsApiKey?> GetAvailableKeyAsync(TtsProvider provider, string modelIdentifier, int requestedChars = 0)
        {
            Debug.WriteLine($"[GetAvailableKey] Finding key for Provider: {provider}, Identifier: {modelIdentifier}, Requested Chars: {requestedChars}");
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            IQueryable<TtsApiKey> query = context.TtsApiKeys.Where(k => k.Provider == provider && k.IsEnabled);

            if (provider == TtsProvider.Gemini)
            {
                var modelSettings = await _ttsSettingsService.GetModelSettingsAsync(provider, modelIdentifier);
                if (modelSettings == null)
                {
                    _logger.LogWarning("Không tìm thấy cấu hình cho Model '{Identifier}' của Provider '{Provider}' trong DB.", modelIdentifier, provider);
                    return null;
                }
                query = query.Where(k => k.ModelName == modelSettings.ModelName && (modelSettings.MaxRequestsPerDay == -1 || k.RequestsToday < modelSettings.MaxRequestsPerDay));

                var availableKeys = await query.ToListAsync();
                if (!availableKeys.Any()) return null;
                return availableKeys[Random.Shared.Next(availableKeys.Count)];
            }
            else // ElevenLabs - Áp dụng logic mới
            {
                // <<< BẮT ĐẦU SỬA ĐỔI: Logic cho ElevenLabs không cần modelIdentifier nữa >>>
                const int LOW_CHAR_THRESHOLD = 500;
                const int CRITICAL_CHAR_THRESHOLD = 100;

                // Lấy tất cả các key ElevenLabs hợp lệ
                var allEligibleKeys = await query.Where(k => k.CharactersUsed < k.CharacterLimit).ToListAsync();

                // Tự động khóa các key đã cạn kiệt
                var criticalKeys = allEligibleKeys.Where(k => (k.CharacterLimit - k.CharactersUsed) < CRITICAL_CHAR_THRESHOLD).ToList();
                if (criticalKeys.Any())
                {
                    foreach (var key in criticalKeys)
                    {
                        key.IsEnabled = false;
                        key.DisabledReason = $"Tự động tắt: Còn dưới {CRITICAL_CHAR_THRESHOLD} ký tự.";
                        _logger.LogWarning("API Key ID {KeyId} tự động bị vô hiệu hóa do còn dưới {Threshold} ký tự.", key.Id, CRITICAL_CHAR_THRESHOLD);
                    }
                    await context.SaveChangesAsync();
                    // Loại bỏ các key vừa bị khóa khỏi danh sách xét duyệt
                    allEligibleKeys = allEligibleKeys.Except(criticalKeys).ToList();
                }

                if (!allEligibleKeys.Any())
                {
                    Debug.WriteLine("[GetAvailableKey] No eligible keys found for ElevenLabs after filtering.");
                    return null;
                }

                // Ưu tiên các key "khỏe mạnh" (còn nhiều hơn 500 ký tự)
                var healthyKeys = allEligibleKeys.Where(k => (k.CharacterLimit - k.CharactersUsed) >= LOW_CHAR_THRESHOLD).ToList();
                if (healthyKeys.Any())
                {
                    Debug.WriteLine($"[GetAvailableKey] Found {healthyKeys.Count} healthy keys. Selecting one randomly.");
                    return healthyKeys[Random.Shared.Next(healthyKeys.Count)];
                }

                // Nếu không có key khỏe mạnh, tìm một key "yếu" nhưng vẫn đủ cho request hiện tại
                // Sắp xếp theo số ký tự còn lại giảm dần để ưu tiên key "khỏe" nhất trong nhóm yếu
                var weakButSufficientKeys = allEligibleKeys
                    .Where(k => (k.CharacterLimit - k.CharactersUsed) >= requestedChars)
                    .OrderByDescending(k => k.CharacterLimit - k.CharactersUsed)
                    .ToList();

                if (weakButSufficientKeys.Any())
                {
                    Debug.WriteLine($"[GetAvailableKey] No healthy keys. Found {weakButSufficientKeys.Count} weak but sufficient keys. Selecting the one with most chars left.");
                    return weakButSufficientKeys.First();
                }

                Debug.WriteLine("[GetAvailableKey] No key found that can satisfy the request.");
                return null; // Không có key nào phù hợp
            }
        }
        private async Task UpdateKeyUsageAsync(int keyId, bool isSuccess, long charactersUsed)
        {
            if (!isSuccess) return;

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await context.TtsApiKeys.FindAsync(keyId);
            if (key == null) return;

            if (key.Provider == TtsProvider.Gemini)
            {
                key.RequestsToday++;
                // Logic cho Gemini vẫn cần TtsSettingsService để biết giới hạn RPD
                var modelSettings = await _ttsSettingsService.GetModelSettingsAsync(key.Provider, key.ModelName!);
                if (modelSettings != null && modelSettings.MaxRequestsPerDay > 0 && key.RequestsToday >= modelSettings.MaxRequestsPerDay)
                {
                    key.IsEnabled = false;
                    key.DisabledReason = $"Tự động tắt: Đã đạt giới hạn {modelSettings.MaxRequestsPerDay} RPD.";
                    _logger.LogWarning("API Key ID {KeyId} đã đạt giới hạn RPD và bị vô hiệu hóa.", key.Id);
                }
            }
            else // ElevenLabs
            {
                // <<< BẮT ĐẦU SỬA ĐỔI: Logic cho ElevenLabs không cần TtsSettingsService >>>
                key.CharactersUsed += charactersUsed;
                if (key.CharactersUsed >= key.CharacterLimit)
                {
                    key.IsEnabled = false;
                    key.DisabledReason = $"Tự động tắt: Đã hết {key.CharacterLimit:N0} ký tự.";
                    _logger.LogWarning("API Key ID {KeyId} đã đạt giới hạn ký tự và bị vô hiệu hóa.", key.Id);
                }
                // <<< KẾT THÚC SỬA ĐỔI >>>
            }
            await context.SaveChangesAsync();
        }

        private async Task DisableKeyAsync(int keyId, string reason)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await context.TtsApiKeys.FindAsync(keyId);
            if (key != null && key.IsEnabled)
            {
                key.IsEnabled = false;
                key.DisabledReason = reason.Length > 200 ? reason.Substring(0, 200) : reason;
                await context.SaveChangesAsync();
                _logger.LogWarning("Đã tự động vô hiệu hóa API Key ID {KeyId}. Lý do: {Reason}", keyId, reason);
            }
        }
    }
}