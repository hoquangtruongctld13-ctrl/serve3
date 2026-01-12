using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SubPhim.Server.Data;
using SubPhim.Server.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// VIP Translation Service - Hoạt động giống 100% như TranslationOrchestratorService (LocalAPI)
    /// về logic gọi API, lấy proxy, đánh dấu API key bị limit RPM, và retry khi proxy lỗi.
    /// Sử dụng chung bể proxy với LocalApi/Proxy.
    /// </summary>
    public class VipTranslationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<VipTranslationService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEncryptionService _encryptionService;
        private readonly ProxyService _proxyService;
        private readonly ProxyRateLimiterService _proxyRateLimiter;
        private readonly VipApiKeyCooldownService _cooldownService;

        // Session storage
        private static readonly ConcurrentDictionary<string, VipTranslationSession> _sessions = new();
        
        // === RPM Limiter per API Key - Đảm bảo mỗi key tôn trọng RPM riêng (giống LocalAPI) ===
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _keyRpmLimiters = new();
        private static readonly ConcurrentDictionary<int, int> _keyRpmCapacities = new(); // Track capacity per key
        
        // === Round-Robin Index - Đảm bảo phân bổ đều request giữa các key (giống LocalAPI) ===
        private static int _keyRoundRobinIndex = 0;
        private static readonly object _roundRobinLock = new();
        
        // === Cached Settings - Cache MaxSrtLineLength to avoid DB query on every request ===
        private static int _cachedMaxSrtLineLength = 3000; // Default value
        private static DateTime _lastSettingsLoadTime = DateTime.MinValue;
        private static readonly TimeSpan SETTINGS_CACHE_DURATION = TimeSpan.FromMinutes(5); // Refresh every 5 minutes
        private static readonly SemaphoreSlim _settingsCacheSemaphore = new(1, 1); // For async locking
        
        // Regex pattern to parse Gemini response in format "{index}: {translated_text}"
        // (matches SrtTranslationService pattern)
        private static readonly Regex TranslationLineRegex = new(@"^\s*(\d+):\s*(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);
        
        // === Constants (giống LocalAPI) ===
        private const int RPM_WAIT_TIMEOUT_MS = 100; // Thời gian chờ khi kiểm tra RPM slot khả dụng
        private const int PROXY_RPM_WAIT_TIMEOUT_MS = 500; // Thời gian chờ khi kiểm tra proxy RPM slot
        private const int MAX_PROXY_SEARCH_ATTEMPTS = 10; // Số lần thử tìm proxy có RPM slot
        private const int FINAL_KEY_WAIT_TIMEOUT_MS = 30000; // Thời gian chờ tối đa khi tất cả keys bận (30 giây)
        // MAX_SRT_LINE_LENGTH moved to VipTranslationSettings.MaxSrtLineLength (customizable in admin)
        private const int DEFAULT_SETTINGS_ID = 1;
        private const double MISSING_INDEX_RETRY_THRESHOLD = 0.5; // Retry batch nếu >50% dòng bị thiếu index trong response
        
        // Chrome-based templates use {0}=major, {1}=build, {2}=patch
        // Firefox templates only use {0}=version (extra args are safely ignored by string.Format)
        private static readonly string[] _chromeTemplates = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36 Edg/{0}.0.{1}.{2}",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36"
        };
        
        private static readonly string[] _firefoxTemplates = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:{0}.0) Gecko/20100101 Firefox/{0}.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:{0}.0) Gecko/20100101 Firefox/{0}.0",
            "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:{0}.0) Gecko/20100101 Firefox/{0}.0"
        };

        public VipTranslationService(
            IServiceProvider serviceProvider,
            ILogger<VipTranslationService> logger,
            IHttpClientFactory httpClientFactory,
            IEncryptionService encryptionService,
            ProxyService proxyService,
            ProxyRateLimiterService proxyRateLimiter,
            VipApiKeyCooldownService cooldownService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _encryptionService = encryptionService;
            _proxyService = proxyService;
            _proxyRateLimiter = proxyRateLimiter;
            _cooldownService = cooldownService;
        }
        
        /// <summary>
        /// Tạo User-Agent ngẫu nhiên cho mỗi request để tránh bị rate limit (giống LocalAPI)
        /// </summary>
        private static string GenerateRandomUserAgent()
        {
            var random = new Random(Guid.NewGuid().GetHashCode()); // Random seed cho mỗi request
            
            // Chọn ngẫu nhiên giữa Chrome và Firefox
            bool useChrome = random.Next(2) == 0;
            
            if (useChrome)
            {
                var template = _chromeTemplates[random.Next(_chromeTemplates.Length)];
                var majorVersion = random.Next(100, 131); // Chrome versions 100-130
                var buildNumber = random.Next(1000, 9999);
                var patchNumber = random.Next(100, 999);
                return string.Format(template, majorVersion, buildNumber, patchNumber);
            }
            else
            {
                var template = _firefoxTemplates[random.Next(_firefoxTemplates.Length)];
                var majorVersion = random.Next(100, 135); // Firefox versions 100-134
                return string.Format(template, majorVersion);
            }
        }
        
        /// <summary>
        /// Helper method để chọn key theo round-robin (giống LocalAPI)
        /// </summary>
        private VipApiKey GetNextKeyRoundRobin(List<VipApiKey> eligibleKeys)
        {
            lock (_roundRobinLock)
            {
                if (_keyRoundRobinIndex >= eligibleKeys.Count)
                    _keyRoundRobinIndex = 0;
                var currentIndex = _keyRoundRobinIndex;
                _keyRoundRobinIndex++;
                return eligibleKeys[currentIndex];
            }
        }
        
        /// <summary>
        /// Đảm bảo key có RPM limiter với capacity đúng. Tạo mới nếu cần. (giống LocalAPI)
        /// </summary>
        private void EnsureKeyRpmLimiter(int keyId, int rpmCapacity)
        {
            // Kiểm tra capacity đã lưu
            if (_keyRpmCapacities.TryGetValue(keyId, out int currentCapacity) && currentCapacity == rpmCapacity)
            {
                // Capacity không thay đổi, không cần làm gì
                return;
            }
            
            // Capacity thay đổi hoặc chưa có, cần tạo/cập nhật semaphore
            lock (_roundRobinLock) // Sử dụng lock để tránh race condition
            {
                // Double-check sau khi lấy lock
                if (_keyRpmCapacities.TryGetValue(keyId, out currentCapacity) && currentCapacity == rpmCapacity)
                    return;
                
                // Dispose old semaphore nếu có
                if (_keyRpmLimiters.TryRemove(keyId, out var oldSemaphore))
                {
                    try { oldSemaphore.Dispose(); }
                    catch { /* Ignore dispose errors */ }
                }
                
                // Tạo semaphore mới
                _keyRpmLimiters[keyId] = new SemaphoreSlim(rpmCapacity, rpmCapacity);
                _keyRpmCapacities[keyId] = rpmCapacity;
            }
        }

        /// <summary>
        /// Get MaxSrtLineLength from settings with caching to avoid DB queries on every request
        /// </summary>
        private async Task<int> GetMaxSrtLineLengthAsync(AppDbContext context)
        {
            // Check if cache is still valid (without locking for better performance)
            if (DateTime.UtcNow - _lastSettingsLoadTime < SETTINGS_CACHE_DURATION)
            {
                return _cachedMaxSrtLineLength;
            }

            // Cache expired, reload from database with async locking
            await _settingsCacheSemaphore.WaitAsync();
            try
            {
                // Double-check inside lock
                if (DateTime.UtcNow - _lastSettingsLoadTime >= SETTINGS_CACHE_DURATION)
                {
                    try
                    {
                        var settings = await context.VipTranslationSettings.FindAsync(DEFAULT_SETTINGS_ID);
                        if (settings == null)
                        {
                            // Settings don't exist, try to create them
                            settings = new VipTranslationSetting { Id = DEFAULT_SETTINGS_ID };
                            context.VipTranslationSettings.Add(settings);
                            await context.SaveChangesAsync();
                        }
                        _cachedMaxSrtLineLength = settings.MaxSrtLineLength;
                        _lastSettingsLoadTime = DateTime.UtcNow;
                        _logger.LogInformation("Refreshed MaxSrtLineLength cache: {MaxLength}", _cachedMaxSrtLineLength);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load MaxSrtLineLength from DB, using cached value: {CachedValue}", _cachedMaxSrtLineLength);
                        // Keep using cached value if DB operation fails
                    }
                }
            }
            finally
            {
                _settingsCacheSemaphore.Release();
            }
            
            return _cachedMaxSrtLineLength;
        }

        public async Task<VipCreateJobResult> CreateJobAsync(int userId, string targetLanguage, List<SrtLine> lines, string systemInstruction)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Check if this is an API key request (negative userId indicates external API key)
            bool isApiKeyRequest = userId < 0;
            
            if (!isApiKeyRequest)
            {
                // Regular user validation and quota checks
                var user = await context.Users.FindAsync(userId);
                if (user == null)
                    return new VipCreateJobResult { Status = "Error", Message = "Tài khoản không tồn tại." };

                // Reset quota if needed (12:00 AM Vietnam time)
                var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
                var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastVipSrtResetUtc, vietnamTimeZone);

                if (lastResetInVietnam.Date < vietnamNow.Date)
                {
                    user.VipSrtLinesUsedToday = 0;
                    user.LastVipSrtResetUtc = DateTime.UtcNow; // Keep full DateTime
                    await context.SaveChangesAsync();
                }

                // Check quota
                int remainingLines = user.DailyVipSrtLimit - user.VipSrtLinesUsedToday;
                if (remainingLines <= 0)
                {
                    return new VipCreateJobResult 
                    { 
                        Status = "Error", 
                        Message = $"Bạn đã hết lượt dịch VIP hôm nay. Giới hạn: {user.DailyVipSrtLimit} dòng/ngày." 
                    };
                }

                if (lines.Count > remainingLines)
                {
                    return new VipCreateJobResult 
                    { 
                        Status = "Error", 
                        Message = $"Không đủ lượt dịch. Yêu cầu: {lines.Count} dòng, còn lại: {remainingLines} dòng." 
                    };
                }

                // Deduct quota
                user.VipSrtLinesUsedToday += lines.Count;
                await context.SaveChangesAsync();
            }

            // Get max line length from cached settings
            int maxLineLength = await GetMaxSrtLineLengthAsync(context);

            // Validate line length - applies to both users and API keys
            foreach (var line in lines)
            {
                if (line.OriginalText.Length > maxLineLength)
                {
                    return new VipCreateJobResult
                    {
                        Status = "Error",
                        Message = $"Dòng {line.Index} vượt quá giới hạn {maxLineLength} ký tự. Vui lòng kiểm tra lại file SRT."
                    };
                }
            }

            // Create session
            var sessionId = Guid.NewGuid().ToString();
            var session = new VipTranslationSession
            {
                SessionId = sessionId,
                UserId = userId,
                TargetLanguage = targetLanguage,
                SystemInstruction = systemInstruction,
                Status = VipJobStatus.Processing,
                TotalLines = lines.Count,
                TranslatedLines = new ConcurrentBag<TranslatedSrtLine>(),
                CreatedAt = DateTime.UtcNow,
                Cts = new CancellationTokenSource()
            };

            _sessions[sessionId] = session;

            // Start translation in background
            _ = Task.Run(async () => await ProcessTranslationAsync(sessionId, lines));

            return new VipCreateJobResult 
            { 
                Status = "Accepted", 
                SessionId = sessionId 
            };
        }

        /// <summary>
        /// Xử lý translation job (giống LocalAPI ProcessJob)
        /// </summary>
        private async Task ProcessTranslationAsync(string sessionId, List<SrtLine> lines)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            var cancellationToken = session.Cts.Token;
            _logger.LogInformation("Starting VIP translation for session {SessionId} with {LineCount} lines", sessionId, lines.Count);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var settings = await context.VipTranslationSettings.FindAsync(DEFAULT_SETTINGS_ID);
                if (settings == null)
                {
                    settings = new VipTranslationSetting { Id = DEFAULT_SETTINGS_ID };
                    context.VipTranslationSettings.Add(settings);
                    await context.SaveChangesAsync();
                }

                // Get active model
                var activeModel = await context.VipAvailableApiModels
                    .FirstOrDefaultAsync(m => m.IsActive, cancellationToken);
                
                if (activeModel == null)
                {
                    session.Status = VipJobStatus.Failed;
                    session.ErrorMessage = "Không tìm thấy model đang hoạt động.";
                    session.CompletedAt = DateTime.UtcNow;
                    return;
                }

                // === SỬA ĐỔI: Load tất cả keys enabled và filter cooldown (giống LocalAPI) ===
                var enabledKeys = await context.VipApiKeys.AsNoTracking()
                    .Where(k => k.IsEnabled)
                    .ToListAsync(cancellationToken);
                
                // Filter out keys in cooldown
                enabledKeys = enabledKeys.Where(k => !_cooldownService.IsInCooldown(k.Id)).ToList();
                
                if (!enabledKeys.Any())
                {
                    session.Status = VipJobStatus.Failed;
                    session.ErrorMessage = "Không có VIP API key nào đang hoạt động (có thể tất cả đang trong cooldown).";
                    session.CompletedAt = DateTime.UtcNow;
                    return;
                }

                // === MỚI: Lấy RPM từ Admin/VipTranslation settings (giống LocalAPI) ===
                int rpmPerKey = settings.Rpm;
                
                // Đảm bảo mỗi key có SemaphoreSlim riêng để tuân thủ RPM
                foreach (var key in enabledKeys)
                {
                    EnsureKeyRpmLimiter(key.Id, rpmPerKey);
                }
                
                _logger.LogInformation("Session {SessionId}: Using {KeyCount} VIP API keys, each with {Rpm} RPM (from Admin settings)", 
                    sessionId, enabledKeys.Count, rpmPerKey);

                // Batch processing
                int batchSize = settings.BatchSize;
                var batches = lines.Select((line, index) => new { line, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.line).ToList())
                    .ToList();

                _logger.LogInformation("Session {SessionId}: Processing {BatchCount} batches", sessionId, batches.Count);

                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    // === Kiểm tra cancellation trước mỗi batch (giống LocalAPI) ===
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Session {SessionId}: Cancellation requested, stopping at batch {BatchIndex}/{TotalBatches}",
                            sessionId, batchIndex + 1, batches.Count);
                        break;
                    }
                    
                    var batch = batches[batchIndex];
                    
                    // === Delay giữa các batch theo cài đặt (giống LocalAPI) ===
                    if (batchIndex > 0 && settings.DelayBetweenBatchesMs > 0)
                    {
                        _logger.LogDebug("Session {SessionId}: Waiting {DelayMs}ms before batch {BatchIndex}/{TotalBatches}", 
                            sessionId, settings.DelayBetweenBatchesMs, batchIndex + 1, batches.Count);
                        await Task.Delay(settings.DelayBetweenBatchesMs, cancellationToken);
                    }

                    // Translate batch với full logic giống LocalAPI
                    var translatedLines = await TranslateBatchAsync(session, batch, activeModel.ModelName, settings, enabledKeys, rpmPerKey, cancellationToken);
                    
                    // Add results to session
                    foreach (var line in translatedLines)
                    {
                        session.TranslatedLines.Add(line);
                    }
                }

                session.Status = VipJobStatus.Completed;
                session.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("🎉 Session {SessionId} COMPLETED!", sessionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Session {SessionId} đã bị hủy (timeout hoặc user request).", sessionId);
                session.Status = VipJobStatus.Failed;
                session.ErrorMessage = "Job đã bị hủy.";
                session.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing VIP translation session {SessionId}", sessionId);
                session.Status = VipJobStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.CompletedAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Translate một batch - hoạt động giống 100% TranslationOrchestratorService.TranslateBatchAsync
        /// </summary>
        private async Task<List<TranslatedSrtLine>> TranslateBatchAsync(VipTranslationSession session, List<SrtLine> batch, 
            string modelName, VipTranslationSetting settings, List<VipApiKey> availableKeys, int rpmPerKey, CancellationToken token)
        {
            // Build input text in line-by-line format: "{index}: {text}" 
            var inputBuilder = new StringBuilder();
            foreach (var line in batch)
            {
                var cleanText = line.OriginalText.Replace("\r\n", " ").Replace("\n", " ");
                inputBuilder.AppendLine($"{line.Index}: {cleanText}");
            }
            string inputText = inputBuilder.ToString().TrimEnd();

            var generationConfig = new JObject
            {
                ["temperature"] = 1,
                ["topP"] = 0.95,
                ["maxOutputTokens"] = settings.MaxOutputTokens
            };

            if (settings.EnableThinkingBudget && settings.ThinkingBudget > 0)
            {
                generationConfig["thinking_config"] = new JObject { ["thinking_budget"] = settings.ThinkingBudget };
            }

            var requestPayloadObject = new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = inputText } } } },
                system_instruction = new { parts = new[] { new { text = session.SystemInstruction } } },
                generationConfig
            };

            string jsonPayload = JsonConvert.SerializeObject(requestPayloadObject, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            // === MỚI: Sử dụng round-robin và per-key RPM limiter (giống LocalAPI) ===
            HashSet<int> triedKeyIds = new HashSet<int>();
            int? successfulKeyId = null;
            
            for (int attempt = 1; attempt <= settings.MaxRetries; attempt++)
            {
                VipApiKey? selectedKey = null;
                
                try
                {
                    // === Chọn key bằng round-robin và chờ per-key RPM limiter (giống LocalAPI) ===
                    selectedKey = await GetAvailableKeyWithRpmLimitAsync(availableKeys, triedKeyIds, rpmPerKey, token);
                    
                    if (selectedKey == null)
                    {
                        _logger.LogWarning("Batch: Không còn VIP key nào khả dụng sau {Attempts} lần thử với {TriedKeys} keys",
                            attempt, triedKeyIds.Count);
                        break; // Không còn key nào để thử
                    }

                    triedKeyIds.Add(selectedKey.Id);
                    
                    var apiKey = _encryptionService.Decrypt(selectedKey.EncryptedApiKey, selectedKey.Iv);
                    string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

                    _logger.LogInformation("Batch attempt {Attempt}/{MaxRetries}: Using VIP Key ID {KeyId} (round-robin)", 
                        attempt, settings.MaxRetries, selectedKey.Id);

                    var (responseText, tokensUsed, errorType, errorDetail, httpStatusCode) = 
                        await CallApiWithRetryAsync(apiUrl, jsonPayload, settings, selectedKey.Id, token);

                    // === XỬ LÝ LỖI 429 (giống LocalAPI) ===
                    if (httpStatusCode == 429)
                    {
                        _logger.LogWarning("VIP Key ID {KeyId} gặp lỗi 429 Rate Limit. Đặt vào cooldown và chờ {Delay}ms trước khi thử key khác.", 
                            selectedKey.Id, settings.RetryDelayMs);
                        
                        await _cooldownService.SetCooldownAsync(selectedKey.Id, $"HTTP 429 on attempt {attempt}");
                        
                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs, token);
                            continue; // Thử lại với key khác
                        }
                    }
                    
                    // === XỬ LÝ CÁC LỖI NGHIÊM TRỌNG KHÁC (giống LocalAPI) ===
                    if (httpStatusCode == 401 || httpStatusCode == 403 || 
                        errorType == "INVALID_ARGUMENT" || errorDetail?.Contains("API key") == true)
                    {
                        _logger.LogError("VIP Key ID {KeyId} gặp lỗi nghiêm trọng ({ErrorType}). Vô hiệu hóa vĩnh viễn và thử key khác NGAY.", 
                            selectedKey.Id, errorType);
                        
                        await _cooldownService.DisableKeyPermanentlyAsync(selectedKey.Id, 
                            $"{errorType}: {errorDetail}");
                        
                        if (attempt < settings.MaxRetries)
                        {
                            // Không delay cho lỗi nghiêm trọng - thử ngay với key khác
                            continue;
                        }
                    }

                    // === THÀNH CÔNG ===
                    if (responseText != null && !responseText.StartsWith("Lỗi", StringComparison.OrdinalIgnoreCase))
                    {
                        successfulKeyId = selectedKey.Id;
                        
                        var results = new List<TranslatedSrtLine>();
                        var translatedLinesDict = new Dictionary<int, string>();
                        
                        foreach (Match m in TranslationLineRegex.Matches(responseText))
                        {
                            if (int.TryParse(m.Groups[1].Value, out int idx))
                                translatedLinesDict[idx] = m.Groups[2].Value.Trim();
                        }

                        // === MỚI: Kiểm tra nếu response thiếu quá nhiều index, retry với key khác ===
                        int batchCount = batch.Count;
                        int missingCount = 0;
                        int retryThreshold = (int)(batchCount * MISSING_INDEX_RETRY_THRESHOLD);
                        
                        // Đếm số dòng thiếu, dừng sớm nếu đã vượt threshold
                        foreach (var line in batch)
                        {
                            if (!translatedLinesDict.ContainsKey(line.Index))
                            {
                                missingCount++;
                                if (missingCount > retryThreshold && attempt < settings.MaxRetries)
                                {
                                    break; // Đã vượt threshold, không cần đếm tiếp
                                }
                            }
                        }
                        
                        if (missingCount > retryThreshold && attempt < settings.MaxRetries)
                        {
                            _logger.LogWarning("VIP Batch có {MissingCount}/{TotalCount} dòng thiếu index (>{Threshold:P0}). Retry với key khác...",
                                missingCount, batchCount, MISSING_INDEX_RETRY_THRESHOLD);
                            
                            int delayMs = settings.RetryDelayMs * attempt;
                            await Task.Delay(delayMs, token);
                            continue; // Retry với key khác
                        }
                        // === KẾT THÚC MỚI ===

                        foreach (var line in batch)
                        {
                            if (translatedLinesDict.TryGetValue(line.Index, out string? translated))
                                results.Add(new TranslatedSrtLine
                                {
                                    Index = line.Index,
                                    TranslatedText = string.IsNullOrWhiteSpace(translated) ? "[API DỊCH RỖNG]" : translated,
                                    Success = true
                                });
                            else
                                results.Add(new TranslatedSrtLine
                                {
                                    Index = line.Index,
                                    TranslatedText = "[API KHÔNG TRẢ VỀ DÒNG NÀY]",
                                    Success = false
                                });
                        }
                        
                        // Update API key usage
                        await UpdateKeyUsageAsync(successfulKeyId.Value, tokensUsed);
                        
                        // Reset cooldown nếu batch thành công (giống LocalAPI)
                        await _cooldownService.ResetCooldownAsync(successfulKeyId.Value);
                        
                        return results;
                    }
                    
                    // === LỖI KHÁC (không phải 429, không nghiêm trọng) ===
                    if (attempt < settings.MaxRetries)
                    {
                        int delayMs = settings.RetryDelayMs * attempt;
                        
                        _logger.LogWarning("Batch attempt {Attempt} failed with VIP Key ID {KeyId}. Error: {Error}. Retrying after {Delay}ms...",
                            attempt, selectedKey.Id, errorType, delayMs);
                        
                        await Task.Delay(delayMs, token);
                        continue;
                    }

                }
                catch (OperationCanceledException)
                {
                    if (selectedKey != null)
                    {
                        _logger.LogInformation("Batch processing cancelled for session {SessionId} at attempt {Attempt} with VIP Key ID {KeyId}", 
                            session.SessionId, attempt, selectedKey.Id);
                    }
                    else
                    {
                        _logger.LogInformation("Batch processing cancelled for session {SessionId} at attempt {Attempt} (no key was selected)", 
                            session.SessionId, attempt);
                    }
                    break; // Exit retry loop on cancellation
                }
                catch (Exception ex)
                {
                    if (selectedKey != null)
                    {
                        _logger.LogError(ex, "Exception during batch translation attempt {Attempt} with VIP Key ID {KeyId}", 
                            attempt, selectedKey.Id);
                    }
                    else
                    {
                        _logger.LogError(ex, "Exception during batch translation attempt {Attempt} (no key was selected). Available keys: {KeyCount}, Tried keys: {TriedCount}", 
                            attempt, availableKeys.Count, triedKeyIds.Count);
                    }
                    
                    if (attempt >= settings.MaxRetries) break;
                    await Task.Delay(settings.RetryDelayMs * attempt, token);
                }
            }
            
            // === TẤT CẢ ATTEMPTS ĐỀU THẤT BẠI ===
            _logger.LogError("Batch translation failed after {MaxRetries} attempts with {KeyCount} different VIP keys",
                settings.MaxRetries, triedKeyIds.Count);
            
            return batch.Select(l => new TranslatedSrtLine
            {
                Index = l.Index,
                TranslatedText = "[LỖI: Không thể dịch sau nhiều lần thử]",
                Success = false
            }).ToList();
        }

        /// <summary>
        /// Chọn key bằng round-robin và đợi per-key RPM limiter (giống LocalAPI)
        /// </summary>
        private async Task<VipApiKey?> GetAvailableKeyWithRpmLimitAsync(
            List<VipApiKey> availableKeys, HashSet<int> excludeKeyIds, int rpmPerKey, CancellationToken token)
        {
            // Lọc keys chưa thử và không trong cooldown
            var eligibleKeys = availableKeys
                .Where(k => !excludeKeyIds.Contains(k.Id) && !_cooldownService.IsInCooldown(k.Id))
                .ToList();
            
            if (!eligibleKeys.Any()) 
            {
                var totalKeys = availableKeys.Count;
                var excludedKeys = excludeKeyIds.Count;
                var cooldownKeys = availableKeys.Count(k => _cooldownService.IsInCooldown(k.Id));
                
                _logger.LogWarning(
                    "No eligible VIP keys available. Total: {Total}, Excluded: {Excluded}, In Cooldown: {Cooldown}",
                    totalKeys, excludedKeys, cooldownKeys);
                    
                return null;
            }
            
            // === ROUND-ROBIN SELECTION: Đảm bảo phân bổ đều ===
            VipApiKey selectedKey = GetNextKeyRoundRobin(eligibleKeys);
            
            // === PER-KEY RPM LIMITER: Đảm bảo mỗi key tuân thủ RPM riêng ===
            var semaphore = _keyRpmLimiters.GetOrAdd(selectedKey.Id, _ => new SemaphoreSlim(rpmPerKey, rpmPerKey));
            
            // Thử lấy slot từ semaphore (không chờ vô hạn)
            if (await semaphore.WaitAsync(RPM_WAIT_TIMEOUT_MS, token))
            {
                // Tự động release sau 1 phút (60 giây = 1 phút trong context RPM)
                ScheduleSemaphoreRelease(semaphore, TimeSpan.FromMinutes(1));
                
                _logger.LogDebug("VIP Key ID {KeyId} selected via round-robin. RPM slots remaining: {Remaining}/{Total}", 
                    selectedKey.Id, semaphore.CurrentCount, rpmPerKey);
                
                return selectedKey;
            }
            
            // Nếu key đã đạt RPM limit, thử key tiếp theo
            _logger.LogWarning("VIP Key ID {KeyId} đã đạt giới hạn {Rpm} RPM, thử key khác", selectedKey.Id, rpmPerKey);
            
            // Thử các key còn lại
            foreach (var key in eligibleKeys.Where(k => k.Id != selectedKey.Id))
            {
                var keySemaphore = _keyRpmLimiters.GetOrAdd(key.Id, _ => new SemaphoreSlim(rpmPerKey, rpmPerKey));
                if (await keySemaphore.WaitAsync(RPM_WAIT_TIMEOUT_MS, token))
                {
                    ScheduleSemaphoreRelease(keySemaphore, TimeSpan.FromMinutes(1));
                    
                    _logger.LogDebug("Alternative VIP Key ID {KeyId} selected. RPM slots remaining: {Remaining}/{Total}", 
                        key.Id, keySemaphore.CurrentCount, rpmPerKey);
                    
                    return key;
                }
            }
            
            // Nếu tất cả key đều đạt RPM limit, chờ key đầu tiên với timeout
            _logger.LogInformation("Tất cả VIP keys đều đạt giới hạn RPM, đợi key ID {KeyId} với timeout {TimeoutMs}ms...", 
                selectedKey.Id, FINAL_KEY_WAIT_TIMEOUT_MS);
            
            // Sử dụng timeout để tránh chờ vô hạn
            if (await semaphore.WaitAsync(FINAL_KEY_WAIT_TIMEOUT_MS, token))
            {
                ScheduleSemaphoreRelease(semaphore, TimeSpan.FromMinutes(1));
                return selectedKey;
            }
            
            // Timeout - không có key nào khả dụng
            _logger.LogWarning("Timeout khi đợi VIP key khả dụng sau {TimeoutMs}ms. Tất cả {Count} keys đều bận.", 
                FINAL_KEY_WAIT_TIMEOUT_MS, eligibleKeys.Count);
            return null;
        }
        
        /// <summary>
        /// Lên lịch release semaphore sau một khoảng thời gian (giống LocalAPI)
        /// </summary>
        private static void ScheduleSemaphoreRelease(SemaphoreSlim semaphore, TimeSpan delay)
        {
            // Sử dụng object holder để tránh race condition với timer assignment
            var timerHolder = new TimerHolder();
            timerHolder.Timer = new Timer(_ =>
            {
                try 
                { 
                    semaphore.Release(); 
                }
                catch (SemaphoreFullException) 
                { 
                    // Semaphore đã đầy, ignore
                }
                catch (ObjectDisposedException) 
                { 
                    // Semaphore đã bị disposed, ignore 
                }
                finally
                {
                    // Dispose timer sau khi callback hoàn thành
                    try { timerHolder.Timer?.Dispose(); }
                    catch { /* Ignore dispose errors */ }
                }
            }, null, delay, Timeout.InfiniteTimeSpan);
        }
        
        // Helper class để giữ timer reference an toàn
        private class TimerHolder
        {
            public Timer? Timer { get; set; }
        }
        
        /// <summary>
        /// Gọi API với retry và proxy handling (giống LocalAPI CallApiWithRetryAsync)
        /// </summary>
        private async Task<(string? responseText, int tokensUsed, string? errorType, string? errorDetail, int httpStatusCode)> CallApiWithRetryAsync(
            string url, string jsonPayload, VipTranslationSetting settings, int apiKeyId, CancellationToken token)
        {
            // Generate random User-Agent once per request to avoid fingerprinting (giống LocalAPI)
            string userAgent = GenerateRandomUserAgent();
            
            // Track failed proxy IDs to exclude them from subsequent attempts within this request
            var failedProxyIds = new HashSet<int>();
            
            // Track current proxy slot for RPM limiting
            string? currentProxySlotId = null;
            Proxy? currentProxy = null;
            
            // Create unique request ID for tracking
            string requestId = $"vipkey{apiKeyId}_{Guid.NewGuid():N}";
            
            for (int attempt = 1; attempt <= settings.MaxRetries; attempt++)
            {
                if (token.IsCancellationRequested)
                    return ("Lỗi: Tác vụ đã bị hủy.", 0, "CANCELLED", "Task was cancelled", 0);

                // === PROXY SELECTION WITH RPM LIMITING (giống LocalAPI) ===
                // Release previous proxy slot if switching proxy
                if (currentProxySlotId != null)
                {
                    _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                    currentProxySlotId = null;
                }
                
                // Get a proxy with available RPM slots
                currentProxy = await GetProxyWithAvailableRpmSlotAsync(failedProxyIds, requestId, token);
                
                // Acquire RPM slot for this proxy (if proxy is available)
                if (currentProxy != null)
                {
                    currentProxySlotId = await _proxyRateLimiter.TryAcquireSlotWithTimeoutAsync(
                        currentProxy.Id, requestId, PROXY_RPM_WAIT_TIMEOUT_MS, token);
                    
                    if (currentProxySlotId == null)
                    {
                        _logger.LogWarning("Proxy {ProxyId} ({Host}:{Port}) đã đạt giới hạn RPM, thử proxy khác",
                            currentProxy.Id, currentProxy.Host, currentProxy.Port);
                        failedProxyIds.Add(currentProxy.Id); // Tạm exclude proxy này
                        
                        // Try to get another proxy
                        currentProxy = await GetProxyWithAvailableRpmSlotAsync(failedProxyIds, requestId, token);
                        if (currentProxy != null)
                        {
                            currentProxySlotId = await _proxyRateLimiter.TryAcquireSlotWithTimeoutAsync(
                                currentProxy.Id, requestId, PROXY_RPM_WAIT_TIMEOUT_MS, token);
                        }
                    }
                }

                try
                {
                    // Create HttpClient with the current proxy (or direct if no proxy)
                    using var httpClient = _proxyService.CreateHttpClientWithProxy(currentProxy);
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };
                    
                    // Add random User-Agent header to avoid rate limiting
                    request.Headers.Add("User-Agent", userAgent);

                    if (currentProxy != null)
                    {
                        var (rpmPerProxy, availSlots, _) = _proxyRateLimiter.GetProxyStatus(currentProxy.Id);
                        _logger.LogDebug("Attempt {Attempt}/{MaxRetries}: Sending VIP request via Proxy {ProxyId} ({Type}://{Host}:{Port}) (Key ID: {KeyId}) RPM slots: {Available}/{Max}", 
                            attempt, settings.MaxRetries, currentProxy.Id, currentProxy.Type, currentProxy.Host, currentProxy.Port, apiKeyId, availSlots, rpmPerProxy);
                    }
                    else
                    {
                        _logger.LogDebug("Attempt {Attempt}/{MaxRetries}: Sending VIP request directly (no proxy) (Key ID: {KeyId})", 
                            attempt, settings.MaxRetries, apiKeyId);
                    }
                    
                    using HttpResponseMessage response = await httpClient.SendAsync(request, token);
                    string responseBody = await response.Content.ReadAsStringAsync(token);

                    // === REQUEST ĐÃ KẾT NỐI THÀNH CÔNG ĐẾN API GEMINI ===
                    // Đánh dấu slot đã được sử dụng (sẽ tự auto-release sau 1 phút)
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.MarkSlotAsUsed(currentProxySlotId);
                        currentProxySlotId = null; // Prevent early release
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        int statusCode = (int)response.StatusCode;
                        string errorType = $"HTTP_{statusCode}";
                        string errorMsg = $"HTTP Error {statusCode}";

                        // === THÊM MỚI: Kiểm tra lỗi FAILED_PRECONDITION "location is not supported" từ Gemini API ===
                        // Lỗi này cho biết proxy IP location không được hỗ trợ bởi Gemini API
                        // Cần disable proxy ngay lập tức để tránh tái sử dụng
                        if (statusCode == 400 && currentProxy != null)
                        {
                            try
                            {
                                var errorBody = JObject.Parse(responseBody);
                                var errorStatus = errorBody?["error"]?["status"]?.ToString();
                                var errorMessage = errorBody?["error"]?["message"]?.ToString() ?? "";
                                
                                if (errorStatus == "FAILED_PRECONDITION" && 
                                    errorMessage.Contains("location is not supported", StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogError("🚫 Proxy {ProxyId} ({Host}:{Port}) bị khoá do lỗi FAILED_PRECONDITION: {ErrorMessage}. Disable proxy ngay lập tức.",
                                        currentProxy.Id, currentProxy.Host, currentProxy.Port, errorMessage);
                                    
                                    await _proxyService.DisableProxyImmediatelyAsync(currentProxy.Id, "location is not supported");
                                    failedProxyIds.Add(currentProxy.Id);
                                    
                                    // Thử ngay với proxy khác (không delay)
                                    if (attempt < settings.MaxRetries)
                                    {
                                        continue;
                                    }
                                    
                                    return ($"Lỗi API: {errorMessage}", 0, "FAILED_PRECONDITION", errorMessage, statusCode);
                                }
                            }
                            catch (JsonReaderException)
                            {
                                // Không parse được JSON, tiếp tục xử lý như HTTP error bình thường
                            }
                        }
                        // === KẾT THÚC THÊM MỚI ===

                        _logger.LogWarning("HTTP Error {StatusCode}. Retrying in {Delay}ms... (Attempt {Attempt}/{MaxRetries})",
                            statusCode, settings.RetryDelayMs * attempt, attempt, settings.MaxRetries);

                        // Ghi nhận proxy failure nếu lỗi không phải 429 (429 là do API, không phải proxy)
                        if (currentProxy != null && statusCode != 429)
                        {
                            await _proxyService.RecordProxyFailureAsync(currentProxy.Id, $"HTTP {statusCode}");
                        }

                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }

                        // Hết số lần retry, trả về lỗi
                        return ($"Lỗi API: {response.StatusCode}", 0, errorType, errorMsg, statusCode);
                    }

                    // === Request thành công, ghi nhận proxy success ===
                    if (currentProxy != null)
                    {
                        await _proxyService.RecordProxySuccessAsync(currentProxy.Id);
                    }

                    // === Parse JSON response với error handling ===
                    JObject parsedBody;
                    try
                    {
                        parsedBody = JObject.Parse(responseBody);
                    }
                    catch (JsonReaderException jsonEx)
                    {
                        // Response không phải JSON (có thể là HTML error page từ proxy hoặc server)
                        var previewBody = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                        _logger.LogWarning("Response is not valid JSON (possibly HTML error page). Preview: {Preview}. Retrying... (Attempt {Attempt}/{MaxRetries})",
                            previewBody, attempt, settings.MaxRetries);
                        
                        // Nếu response bắt đầu bằng HTML tag, có thể proxy trả về error page
                        if (responseBody.TrimStart().StartsWith("<", StringComparison.Ordinal))
                        {
                            if (currentProxy != null)
                            {
                                await _proxyService.RecordProxyFailureAsync(currentProxy.Id, "Proxy returned HTML instead of JSON", isIntermittent: true);
                                failedProxyIds.Add(currentProxy.Id);
                            }
                        }
                        
                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }
                        
                        return ("Lỗi: Response không phải JSON hợp lệ", 0, "INVALID_JSON", $"JSON parse error: {jsonEx.Message}", 200);
                    }

                    // Kiểm tra lỗi trong response body
                    if (parsedBody?["error"] != null)
                    {
                        string errorMsg = parsedBody["error"]?["message"]?.ToString() ?? "Unknown error";
                        _logger.LogWarning("API returned error: {ErrorMsg}. Retrying... (Attempt {Attempt}/{MaxRetries})",
                            errorMsg, attempt, settings.MaxRetries);

                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }

                        return ($"Lỗi API: {errorMsg}", 0, "API_ERROR", errorMsg, 200);
                    }

                    // === Kiểm tra blockReason (vi phạm chính sách an toàn) ===
                    if (parsedBody?["promptFeedback"]?["blockReason"] != null)
                    {
                        string blockReason = parsedBody["promptFeedback"]["blockReason"]?.ToString() ?? "Unknown";
                        string errorMsg = $"Nội dung bị chặn. Lý do: {blockReason}";

                        _logger.LogWarning("Content blocked by safety filters: {BlockReason}. This will NOT be retried.", blockReason);

                        // Vi phạm chính sách không retry
                        return ($"Lỗi: {errorMsg}", 0, "BLOCKED_CONTENT", errorMsg, 200);
                    }

                    // === Kiểm tra finishReason ===
                    var finishReason = parsedBody?["candidates"]?[0]?["finishReason"]?.ToString();
                    if (!string.IsNullOrEmpty(finishReason) && finishReason != "STOP")
                    {
                        string errorMsg = $"FinishReason không hợp lệ: {finishReason}";

                        _logger.LogWarning("Invalid finishReason: {FinishReason}. Possible safety violation. Retrying... (Attempt {Attempt}/{MaxRetries})",
                            finishReason, attempt, settings.MaxRetries);

                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }

                        return ($"Lỗi: {errorMsg}", 0, "FINISH_REASON", errorMsg, 200);
                    }

                    int tokens = parsedBody?["usageMetadata"]?["totalTokenCount"]?.Value<int>() ?? 0;
                    string? responseText = parsedBody?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                    if (responseText == null)
                    {
                        _logger.LogWarning("API returned OK but content is empty. Retrying... (Attempt {Attempt}/{MaxRetries})",
                            attempt, settings.MaxRetries);

                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }

                        return ("Lỗi: API trả về phản hồi rỗng.", 0, "EMPTY_RESPONSE", "API returned empty response", 200);
                    }

                    // Success
                    return (responseText, tokens, null, null, 200);
                }
                catch (HttpRequestException ex) when (IsProxyTunnelError(ex))
                {
                    // === PROXY TUNNEL ERROR: Immediately switch to different proxy or direct connection ===
                    // Lỗi kết nối proxy - KHÔNG tính vào RPM (release slot early)
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                        currentProxySlotId = null;
                    }
                    
                    if (currentProxy != null)
                    {
                        failedProxyIds.Add(currentProxy.Id);
                        await _proxyService.RecordProxyFailureAsync(currentProxy.Id, $"Proxy tunnel failed: {ex.Message}");
                        _logger.LogWarning("Proxy {ProxyId} ({Host}:{Port}) tunnel connection failed: {Error}. Excluding and trying another proxy immediately.", 
                            currentProxy.Id, currentProxy.Host, currentProxy.Port, ex.Message);
                    }
                    
                    // Don't count proxy failures as API retry attempts - retry immediately with new proxy
                    if (attempt < settings.MaxRetries)
                    {
                        await Task.Delay(500, token); // Short delay before retry with new proxy
                        continue;
                    }
                    
                    return ($"Lỗi Proxy: {ex.Message}", 0, "PROXY_TUNNEL_ERROR", ex.Message, 0);
                }
                catch (Exception ex)
                {
                    // === Lỗi kết nối - KHÔNG tính vào RPM (release slot early) ===
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                        currentProxySlotId = null;
                    }
                    
                    // Check if this is a CRITICAL proxy error (connection timeout, host unreachable, etc.)
                    if (currentProxy != null && ProxyService.IsCriticalProxyError(ex))
                    {
                        var errorDescription = ProxyService.GetProxyErrorDescription(ex);
                        _logger.LogError("🚫 CRITICAL PROXY ERROR for Proxy {ProxyId} ({Host}:{Port}): {Error}. Disabling proxy PERMANENTLY.", 
                            currentProxy.Id, currentProxy.Host, currentProxy.Port, errorDescription);
                        
                        await _proxyService.DisableProxyImmediatelyAsync(currentProxy.Id, errorDescription);
                        failedProxyIds.Add(currentProxy.Id);
                        
                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(500, token); // Short delay before retry
                            continue;
                        }
                    }
                    // Record non-critical proxy failure and switch to a new proxy
                    else if (currentProxy != null && (ex is HttpRequestException || ex is TaskCanceledException))
                    {
                        failedProxyIds.Add(currentProxy.Id);
                        
                        bool isTimeoutError = ProxyService.IsTimeoutOrCancellationError(ex);
                        var errorMessage = ProxyService.GetProxyErrorDescription(ex);
                        
                        await _proxyService.RecordProxyFailureAsync(currentProxy.Id, errorMessage, 
                            isIntermittent: false, isTimeoutError: isTimeoutError);
                        
                        if (isTimeoutError)
                        {
                            _logger.LogDebug("Proxy {ProxyId} ({Host}:{Port}) timeout (transient): {Error}. Switching to another proxy.", 
                                currentProxy.Id, currentProxy.Host, currentProxy.Port, errorMessage);
                        }
                        else
                        {
                            _logger.LogWarning("Proxy {ProxyId} ({Host}:{Port}) connection failed: {Error}. Switching to a new proxy.", 
                                currentProxy.Id, currentProxy.Host, currentProxy.Port, errorMessage);
                        }
                    }
                    
                    _logger.LogError(ex, "Exception during VIP API call. Retrying in {Delay}ms... (Attempt {Attempt}/{MaxRetries})",
                        settings.RetryDelayMs * attempt, attempt, settings.MaxRetries);

                    if (attempt >= settings.MaxRetries)
                        return ($"Lỗi Exception: {ex.Message}", 0, "EXCEPTION", ex.Message, 0);

                    await Task.Delay(settings.RetryDelayMs * attempt, token);
                }
            }

            // Cleanup: release slot if still held
            if (currentProxySlotId != null)
            {
                _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
            }

            return ("Lỗi API: Hết số lần thử lại.", 0, "MAX_RETRIES", "Exceeded maximum retry attempts", 0);
        }
        
        /// <summary>
        /// Lấy proxy có RPM slot khả dụng, loại trừ các proxy đã failed. (giống LocalAPI)
        /// </summary>
        private async Task<Proxy?> GetProxyWithAvailableRpmSlotAsync(HashSet<int> excludeProxyIds, string requestId, CancellationToken token)
        {
            // Get list of available proxies
            var proxy = await _proxyService.GetNextProxyAsync(excludeProxyIds);
            if (proxy == null)
            {
                return null;
            }
            
            // Check if this proxy has available RPM slots
            if (_proxyRateLimiter.HasAvailableSlot(proxy.Id))
            {
                return proxy;
            }
            
            // Current proxy is at RPM limit, try to find another one
            var triedProxyIds = new HashSet<int>(excludeProxyIds) { proxy.Id };
            
            for (int i = 0; i < MAX_PROXY_SEARCH_ATTEMPTS; i++)
            {
                var nextProxy = await _proxyService.GetNextProxyAsync(triedProxyIds);
                if (nextProxy == null)
                {
                    // No more proxies available - return the original one (will wait for slot)
                    _logger.LogInformation("All proxies at RPM limit or excluded. Using proxy {ProxyId} and waiting for slot.", proxy.Id);
                    return proxy;
                }
                
                if (_proxyRateLimiter.HasAvailableSlot(nextProxy.Id))
                {
                    return nextProxy;
                }
                
                triedProxyIds.Add(nextProxy.Id);
            }
            
            // All proxies at RPM limit, return the first one
            return proxy;
        }
        
        /// <summary>
        /// Check if the exception is a proxy tunnel error (giống LocalAPI)
        /// </summary>
        private static bool IsProxyTunnelError(HttpRequestException ex)
        {
            var message = ex.Message ?? string.Empty;
            return message.Contains("proxy tunnel", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("proxy", StringComparison.OrdinalIgnoreCase) && 
                   (message.Contains("400") || message.Contains("407") || message.Contains("403"));
        }
        
        /// <summary>
        /// Update API key usage after successful translation
        /// </summary>
        private async Task UpdateKeyUsageAsync(int keyId, int tokensUsed)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var apiKey = await context.VipApiKeys.FindAsync(keyId);
                if (apiKey == null)
                {
                    _logger.LogWarning("Không thể cập nhật sử dụng: Không tìm thấy VIP API Key ID {ApiKeyId}", keyId);
                    return;
                }
                var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
                var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(apiKey.LastRequestCountResetUtc, vietnamTimeZone);
                if (lastResetInVietnam.Date < vietnamNow.Date)
                {
                    _logger.LogInformation("Resetting daily request count for VIP API Key ID {ApiKeyId}", keyId);
                    apiKey.RequestsToday = 0;
                    apiKey.LastRequestCountResetUtc = DateTime.UtcNow.Date;
                }
                apiKey.RequestsToday++;
                if (tokensUsed > 0)
                {
                    apiKey.TotalTokensUsed += tokensUsed;
                }
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật sử dụng cho VIP API Key ID {ApiKeyId}", keyId);
            }
        }

        public async Task<List<TranslatedSrtLine>?> GetResultsAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return null;

            // Update last polled time for auto-cleanup tracking
            session.LastPolledAt = DateTime.UtcNow;
            
            return session.TranslatedLines.OrderBy(l => l.Index).ToList();
        }

        public async Task<(bool IsCompleted, string? ErrorMessage)> GetStatusAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return (true, "Session không tồn tại hoặc đã hết hạn.");

            // Update last polled time for auto-cleanup tracking
            session.LastPolledAt = DateTime.UtcNow;
            
            bool isCompleted = session.Status == VipJobStatus.Completed || session.Status == VipJobStatus.Failed;
            return (isCompleted, session.ErrorMessage);
        }

        public async Task<bool> CancelJobAsync(string sessionId, int userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;

            if (session.UserId != userId)
                return false;

            session.Cts.Cancel();
            session.Status = VipJobStatus.Failed;
            session.ErrorMessage = "Job đã bị hủy bởi người dùng.";

            // Refund unused lines (only for regular users, not API keys)
            bool isApiKeyRequest = userId < 0;
            if (!isApiKeyRequest)
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await context.Users.FindAsync(userId);
                
                if (user != null)
                {
                    int translatedCount = session.TranslatedLines.Count(l => l.Success);
                    int refundLines = session.TotalLines - translatedCount;
                    
                    if (refundLines > 0)
                    {
                        user.VipSrtLinesUsedToday -= refundLines;
                        await context.SaveChangesAsync();
                        _logger.LogInformation("Refunded {Count} lines to user {UserId}", refundLines, userId);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Cleanup stale API key sessions that have completed but not been polled for 5 minutes.
        /// For API key requests (userId < 0), sessions are auto-deleted if:
        /// - The job is completed or failed, AND
        /// - Either never polled after completion, OR not polled for 5 minutes since completion
        /// </summary>
        /// <returns>Number of sessions cleaned up</returns>
        public int CleanupStaleApiKeySessions()
        {
            var now = DateTime.UtcNow;
            var staleThreshold = TimeSpan.FromMinutes(5);
            int cleanedCount = 0;
            
            // Snapshot of session keys for thread-safe iteration
            // ConcurrentDictionary methods (TryGetValue, TryRemove) are atomic
            var sessionIds = _sessions.Keys.ToList();
            
            foreach (var sessionId in sessionIds)
            {
                if (!_sessions.TryGetValue(sessionId, out var session))
                    continue;
                    
                // Only process API key sessions (negative userId)
                if (session.UserId >= 0)
                    continue;
                    
                // Only process completed or failed sessions
                if (session.Status != VipJobStatus.Completed && session.Status != VipJobStatus.Failed)
                    continue;
                
                // Session must have CompletedAt set for proper cleanup timing
                // If CompletedAt is null for a completed/failed session, it's a data inconsistency - skip and log
                if (session.CompletedAt == null)
                {
                    _logger.LogWarning(
                        "API key session {SessionId} is {Status} but has no CompletedAt timestamp, skipping cleanup",
                        sessionId, session.Status);
                    continue;
                }
                    
                // Check if session is stale (not polled for 5 minutes after completion)
                // Use LastPolledAt if available, otherwise use CompletedAt
                var lastActivity = session.LastPolledAt ?? session.CompletedAt.Value;
                
                if (now - lastActivity >= staleThreshold)
                {
                    // Remove the session
                    if (_sessions.TryRemove(sessionId, out var removed))
                    {
                        cleanedCount++;
                        _logger.LogInformation(
                            "Cleaned up stale API key session {SessionId} (ApiKeyId={ApiKeyId}, CompletedAt={CompletedAt}, LastPolledAt={LastPolledAt})",
                            sessionId, -removed.UserId, removed.CompletedAt, removed.LastPolledAt);
                            
                        // Dispose the CancellationTokenSource
                        try 
                        { 
                            removed.Cts?.Dispose(); 
                        } 
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disposing CancellationTokenSource for session {SessionId}", sessionId);
                        }
                    }
                }
            }
            
            return cleanedCount;
        }
    }

    public class VipTranslationSession
    {
        public string SessionId { get; set; }
        public int UserId { get; set; }
        public string TargetLanguage { get; set; }
        public string SystemInstruction { get; set; }
        public VipJobStatus Status { get; set; }
        public int TotalLines { get; set; }
        public ConcurrentBag<TranslatedSrtLine> TranslatedLines { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public CancellationTokenSource Cts { get; set; }
        
        /// <summary>
        /// Timestamp of the last time this session was polled for results.
        /// Used for auto-cleanup of API key sessions that are not polled after completion.
        /// </summary>
        public DateTime? LastPolledAt { get; set; }
    }

    public enum VipJobStatus
    {
        Processing,
        Completed,
        Failed
    }

    public class VipCreateJobResult
    {
        public string Status { get; set; } // "Accepted", "Error"
        public string? Message { get; set; }
        public string? SessionId { get; set; }
    }
}
