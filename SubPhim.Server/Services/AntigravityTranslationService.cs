using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SubPhim.Server.Data;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service để gọi Antigravity API (OpenAI-compatible endpoint)
    /// Dùng cho việc dịch phụ đề song song với Direct API
    /// </summary>
    public class AntigravityTranslationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AntigravityTranslationService> _logger;

        // Rate limiting cho Antigravity
        private static readonly SemaphoreSlim _rpmSemaphore = new SemaphoreSlim(60, 60); // Mặc định 60 RPM
        private static int _currentRpmCapacity = 60;
        private static readonly object _rpmLock = new object();

        // Request tracking cho load balancing
        private static readonly ConcurrentDictionary<int, DateTime> _userLastRequestTime = new();
        private static readonly ConcurrentQueue<(int userId, DateTime requestTime)> _requestQueue = new();

        public AntigravityTranslationService(
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory,
            ILogger<AntigravityTranslationService> logger)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        #region Health Check

        /// <summary>
        /// Kiểm tra xem Antigravity API có available không
        /// </summary>
        public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);

                if (settings == null || !settings.AntigravityEnabled)
                    return false;

                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", settings.AntigravityApiKey);

                // Test với một request nhỏ
                var testPayload = new
                {
                    model = settings.AntigravityDefaultModel,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 5
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(testPayload),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(
                    $"{settings.AntigravityBaseUrl.TrimEnd('/')}/chat/completions",
                    content, ct);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Antigravity health check failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Kiểm tra và lấy settings Antigravity, trả về null nếu không enabled
        /// </summary>
        public async Task<LocalApiSetting?> GetAntigravitySettingsIfEnabledAsync(CancellationToken ct = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);

            if (settings == null || !settings.AntigravityEnabled)
                return null;

            return settings;
        }

        #endregion

        #region Translation Methods

        /// <summary>
        /// Dịch một batch SRT lines qua Antigravity API
        /// </summary>
        public async Task<(Dictionary<int, string> translations, int tokensUsed, string? error)> TranslateBatchAsync(
            List<OriginalSrtLineDb> batch,
            string targetLanguage,
            string systemInstruction,
            string? modelName,
            int userId,
            CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);

                if (settings == null || !settings.AntigravityEnabled)
                    return (new Dictionary<int, string>(), 0, "Antigravity API is not enabled");

                // === Fair scheduling: Đợi nếu user này request quá gần ===
                await ApplyFairSchedulingAsync(userId, settings.AntigravityRpm, ct);

                // === Wait for RPM slot ===
                UpdateRpmCapacity(settings.AntigravityRpm);
                if (!await _rpmSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
                {
                    _logger.LogWarning("Antigravity RPM limit reached, request queued for user {UserId}", userId);
                    return (new Dictionary<int, string>(), 0, "Rate limit exceeded, please retry");
                }

                bool releaseScheduled = false;
                try
                {
                    // Schedule release after 1 minute
                    releaseScheduled = true;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(60000, CancellationToken.None);
                        try { _rpmSemaphore.Release(); } catch { /* Ignore if already released */ }
                    });

                    // Chọn model (ưu tiên tham số, sau đó fallback về default)
                    var model = modelName ?? settings.AntigravityDefaultModel;

                    var httpClient = _httpClientFactory.CreateClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(settings.AntigravityTimeoutSeconds);
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", settings.AntigravityApiKey);

                    // Build payload
                    var payloadBuilder = new StringBuilder();
                    foreach (var line in batch)
                    {
                        var cleanText = line.OriginalText
                            .Replace("\r\n", " ")
                            .Replace("\n", " ")
                            .Trim();
                        payloadBuilder.AppendLine($"{line.LineIndex}\t{cleanText}");
                    }

                    // Combine system instruction và user prompt theo format Antigravity
                    var fullPrompt = $@"{systemInstruction}

Hãy dịch các dòng phụ đề sau sang {targetLanguage}.
NHẮC LẠI format bắt buộc: index: nội dung dịch

DANH SÁCH CẦN DỊCH:
{payloadBuilder}";

                    var requestPayload = new
                    {
                        model = model,
                        messages = new[]
                        {
                            new { role = "user", content = fullPrompt }
                        },
                        temperature = 0.2
                    };

                    var jsonContent = new StringContent(
                        JsonConvert.SerializeObject(requestPayload),
                        Encoding.UTF8,
                        "application/json");

                    _logger.LogDebug("Calling Antigravity API for batch ({Count} lines) with model {Model}",
                        batch.Count, model);

                    var response = await httpClient.PostAsync(
                        $"{settings.AntigravityBaseUrl.TrimEnd('/')}/chat/completions",
                        jsonContent, ct);

                    var responseJson = await response.Content.ReadAsStringAsync(ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Antigravity API error: {StatusCode} - {Response}",
                            (int)response.StatusCode, responseJson);
                        return (new Dictionary<int, string>(), 0, $"API error: {response.StatusCode}");
                    }

                    // Parse response
                    var responseObj = JObject.Parse(responseJson);
                    var responseText = responseObj["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                    var tokensUsed = responseObj["usage"]?["total_tokens"]?.ToObject<int>() ?? 0;

                    // Parse translations
                    var translations = ParseModelOutput(responseText);

                    _logger.LogInformation("Antigravity batch completed: {Parsed}/{Total} lines, {Tokens} tokens",
                        translations.Count, batch.Count, tokensUsed);

                    return (translations, tokensUsed, null);
                }
                catch (Exception)
                {
                    // Không release ở đây vì đã lên lịch release sau 60 giây
                    // Nếu release ở đây sẽ gây SemaphoreFullException khi task lên lịch cũng release
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Antigravity translation error for user {UserId}", userId);
                return (new Dictionary<int, string>(), 0, ex.Message);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Parse output từ model theo format "index: translated text"
        /// </summary>
        private Dictionary<int, string> ParseModelOutput(string output)
        {
            var mapping = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(output))
                return mapping;

            output = output.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            var regex = new Regex(@"^\s*(\d+)\s*:\s*(.*)$", RegexOptions.Multiline);

            foreach (Match m in regex.Matches(output))
            {
                if (int.TryParse(m.Groups[1].Value, out int idx))
                {
                    var translated = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(translated))
                    {
                        mapping[idx] = translated;
                    }
                }
            }

            return mapping;
        }

        /// <summary>
        /// Cập nhật RPM capacity nếu thay đổi
        /// </summary>
        private void UpdateRpmCapacity(int newCapacity)
        {
            lock (_rpmLock)
            {
                if (_currentRpmCapacity != newCapacity)
                {
                    // Điều chỉnh semaphore (không thể resize trực tiếp, chỉ log warning)
                    _logger.LogInformation("Antigravity RPM capacity changed: {Old} -> {New}",
                        _currentRpmCapacity, newCapacity);
                    _currentRpmCapacity = newCapacity;
                }
            }
        }

        /// <summary>
        /// Áp dụng fair scheduling để chia đều request giữa các user
        /// </summary>
        private async Task ApplyFairSchedulingAsync(int userId, int rpm, CancellationToken ct)
        {
            // Tính khoảng cách tối thiểu giữa các request của cùng user
            // Để chia đều, mỗi user chỉ được gửi 1 request mỗi (60000/rpm) ms
            var minIntervalMs = 60000 / Math.Max(rpm, 1);

            if (_userLastRequestTime.TryGetValue(userId, out var lastRequest))
            {
                var elapsed = (DateTime.UtcNow - lastRequest).TotalMilliseconds;
                if (elapsed < minIntervalMs)
                {
                    var waitTime = (int)(minIntervalMs - elapsed);
                    _logger.LogDebug("Fair scheduling: User {UserId} waiting {WaitMs}ms", userId, waitTime);
                    await Task.Delay(waitTime, ct);
                }
            }

            _userLastRequestTime[userId] = DateTime.UtcNow;
        }

        /// <summary>
        /// Lấy danh sách models từ settings
        /// </summary>
        public async Task<List<AntigravityModel>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);

                if (settings == null || string.IsNullOrWhiteSpace(settings.AntigravityModelsJson))
                    return GetDefaultModels();

                return JsonConvert.DeserializeObject<List<AntigravityModel>>(settings.AntigravityModelsJson)
                    ?? GetDefaultModels();
            }
            catch
            {
                return GetDefaultModels();
            }
        }

        private List<AntigravityModel> GetDefaultModels()
        {
            return new List<AntigravityModel>
            {
                new("Gemini 3 Flash", "gemini-3-flash"),
                new("Gemini 3 Pro High", "gemini-3-pro-high"),
                new("Gemini 3 Pro Low", "gemini-3-pro-low"),
                new("Gemini 3 Pro (Image)", "gemini-3-pro-image"),
                new("Gemini 2.5 Flash", "gemini-2.5-flash"),
                new("Gemini 2.5 Flash Lite", "gemini-2.5-flash-lite"),
                new("Gemini 2.5 Pro", "gemini-2.5-pro"),
                new("Gemini 2.5 Flash (Thinking)", "gemini-2.5-flash-thinking"),
                new("Claude 4.5 Sonnet", "claude-sonnet-4-5")
            };
        }

        #endregion
    }

    /// <summary>
    /// Model cho Antigravity API configuration
    /// </summary>
    public record AntigravityModel(string DisplayName, string ModelId);
}
