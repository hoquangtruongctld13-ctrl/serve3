using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SubPhim.Server.Data;
using SubPhim.Server.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SubPhim.Server.Services
{
    public class TranslationOrchestratorService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TranslationOrchestratorService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApiKeyCooldownService _cooldownService;
        private readonly JobCancellationService _cancellationService;
        private readonly GlobalRequestRateLimiterService _globalRateLimiter;
        private readonly ProxyService _proxyService;
        private readonly ProxyRateLimiterService _proxyRateLimiter;

        // === RPM Limiter per API Key ===
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _keyRpmLimiters = new();
        private static readonly ConcurrentDictionary<int, int> _keyRpmCapacities = new();

        // === Round-Robin Index per Pool ===
        private static int _paidKeyRoundRobinIndex = 0;
        private static int _freeKeyRoundRobinIndex = 0;
        private static readonly object _roundRobinLock = new();

        // === Constants ===
        private const int RPM_WAIT_TIMEOUT_MS = 100;
        private const int PROXY_RPM_WAIT_TIMEOUT_MS = 500;
        private const int FINAL_KEY_WAIT_TIMEOUT_MS = 30000;
        private const int RETRY_RESULT_TIMEOUT_SECONDS = 30;
        private const int DEFAULT_LOCAL_API_SETTING_ID = 1;
        private const int MIN_BATCH_SIZE = 1;
        private const double MISSING_INDEX_RETRY_THRESHOLD = 0.3; // Giảm xuống 30% để retry sớm hơn
        private const int PROXY_SEARCH_DELAY_MS = 200; // Giảm delay để tìm proxy nhanh hơn

        // === MỚI: Constants cho retry và validation ===
        private const int MAX_BATCH_RETRY_ATTEMPTS = 3; // Số lần retry batch sau khi tất cả keys fail
        private const int BATCH_RETRY_DELAY_MS = 2000; // Delay giữa các lần retry batch
        private const int KEY_ACQUISITION_TIMEOUT_MS = 60000; // Timeout khi đợi key (60 giây)
        private const int ALL_KEYS_BUSY_RETRY_DELAY_MS = 5000; // Delay khi tất cả keys bận

        // User-Agent templates
        private static readonly string[] _chromeTemplates = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{0}.0.{1}.{2} Safari/537.36"
        };

        private static readonly string[] _firefoxTemplates = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:{0}.0) Gecko/20100101 Firefox/{0}.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:{0}.0) Gecko/20100101 Firefox/{0}.0"
        };

        private static string GenerateRandomUserAgent()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());
            bool useChrome = random.Next(2) == 0;

            if (useChrome)
            {
                var template = _chromeTemplates[random.Next(_chromeTemplates.Length)];
                return string.Format(template, random.Next(100, 131), random.Next(1000, 9999), random.Next(100, 999));
            }
            else
            {
                var template = _firefoxTemplates[random.Next(_firefoxTemplates.Length)];
                return string.Format(template, random.Next(100, 135));
            }
        }

        public record CreateJobResult(string Status, string Message, string SessionId = null, int RemainingLines = 0);

        public TranslationOrchestratorService(
            IServiceProvider serviceProvider,
            ILogger<TranslationOrchestratorService> logger,
            IHttpClientFactory httpClientFactory,
            ApiKeyCooldownService cooldownService,
            JobCancellationService cancellationService,
            GlobalRequestRateLimiterService globalRateLimiter,
            ProxyService proxyService,
            ProxyRateLimiterService proxyRateLimiter)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _cooldownService = cooldownService;
            _cancellationService = cancellationService;
            _globalRateLimiter = globalRateLimiter;
            _proxyService = proxyService;
            _proxyRateLimiter = proxyRateLimiter;
        }

        // =====================================================================
        // PHẦN 1: JOB PROCESSING - CẢI TIẾN HOÀN TOÀN
        // =====================================================================

        private async Task ProcessJob(string sessionId, int userId, SubscriptionTier userTier)
        {
            _logger.LogInformation("🚀 Starting IMPROVED processing for job {SessionId} using {Tier} tier", sessionId, userTier);

            var cancellationToken = _cancellationService.RegisterJob(sessionId, userId, timeoutMinutes: 15);
            ApiPoolType poolToUse = (userTier == SubscriptionTier.Free) ? ApiPoolType.Free : ApiPoolType.Paid;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

                var job = await context.TranslationJobs.FindAsync(new object[] { sessionId }, cancellationToken);
                if (job == null)
                {
                    _logger.LogError("Job {SessionId} not found!", sessionId);
                    return;
                }

                job.Status = JobStatus.Processing;
                await context.SaveChangesAsync(cancellationToken);

                var settings = await GetLocalApiSettingsAsync(context, cancellationToken);
                var activeModel = await context.AvailableApiModels.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.IsActive && m.PoolType == poolToUse, cancellationToken);

                if (activeModel == null)
                    throw new Exception($"Không có model nào đang hoạt động cho nhóm '{poolToUse}'.");

                // Load và filter keys
                var enabledKeys = await context.ManagedApiKeys.AsNoTracking()
                    .Where(k => k.IsEnabled && k.PoolType == poolToUse)
                    .ToListAsync(cancellationToken);

                enabledKeys = enabledKeys.Where(k => !_cooldownService.IsInCooldown(k.Id)).ToList();

                if (!enabledKeys.Any())
                    throw new Exception($"Không có API key nào khả dụng cho nhóm '{poolToUse}'.");

                int rpmPerKey = settings.Rpm;
                foreach (var key in enabledKeys)
                {
                    EnsureKeyRpmLimiter(key.Id, rpmPerKey);
                }

                var allLines = await context.OriginalSrtLines.AsNoTracking()
                    .Where(l => l.SessionId == sessionId)
                    .OrderBy(l => l.LineIndex)
                    .ToListAsync(cancellationToken);

                int batchSize = GetValidBatchSize(settings);
                var batches = allLines
                    .Select((line, index) => new { line, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.line).ToList())
                    .ToList();

                _logger.LogInformation("Job {SessionId}: Processing {BatchCount} batches with {KeyCount} keys, RPM={Rpm}",
                    sessionId, batches.Count, enabledKeys.Count, rpmPerKey);

                // =================================================================
                // CẢI TIẾN QUAN TRỌNG: XỬ LÝ BATCH TUẦN TỰ VỚI CONCURRENT LIMIT
                // =================================================================

                int maxConcurrentBatches = Math.Min(enabledKeys.Count * 2, 10); // Giới hạn concurrent
                var semaphore = new SemaphoreSlim(maxConcurrentBatches);
                var batchResults = new ConcurrentDictionary<int, BatchProcessResult>();
                var processingTasks = new List<Task>();

                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Job {SessionId}: Cancellation requested at batch {BatchIndex}", sessionId, batchIndex);
                        break;
                    }

                    var batch = batches[batchIndex];
                    int currentBatchIndex = batchIndex;

                    // === QUAN TRỌNG: Delay thực sự giữa các batch ===
                    if (batchIndex > 0 && settings.DelayBetweenBatchesMs > 0)
                    {
                        await Task.Delay(settings.DelayBetweenBatchesMs, cancellationToken);
                    }

                    // Đợi semaphore để giới hạn concurrent
                    await semaphore.WaitAsync(cancellationToken);

                    processingTasks.Add(Task.Run(async () =>
                    {
                        string? rateLimitSlotId = null;
                        var result = new BatchProcessResult { BatchIndex = currentBatchIndex, Success = false };

                        try
                        {
                            rateLimitSlotId = await _globalRateLimiter.AcquireSlotAsync(
                                $"{sessionId}_batch{currentBatchIndex}", cancellationToken);

                            // === CẢI TIẾN: Retry batch với timeout ===
                            var batchResult = await ProcessBatchWithRetryAsync(
                                batch, job, settings, activeModel.ModelName,
                                poolToUse, encryptionService, enabledKeys, rpmPerKey,
                                currentBatchIndex, cancellationToken);

                            result.Success = batchResult.success;
                            result.Results = batchResult.results;
                            result.UsedKeyId = batchResult.usedKeyId;
                            result.TokensUsed = batchResult.tokensUsed;

                            if (batchResult.results != null && batchResult.results.Any())
                            {
                                await SaveResultsToDb(sessionId, batchResult.results);
                            }

                            if (batchResult.usedKeyId.HasValue)
                            {
                                await UpdateUsageInDb(batchResult.usedKeyId.Value, batchResult.tokensUsed);
                                await _cooldownService.ResetCooldownAsync(batchResult.usedKeyId.Value);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // === SỬA LỖI QUAN TRỌNG: LƯU RESULTS KHI CANCELLED ===
                            _logger.LogWarning("Batch {BatchIndex} cancelled for job {SessionId}", currentBatchIndex, sessionId);

                            var cancelledResults = batch.Select(l => new TranslatedSrtLineDb
                            {
                                SessionId = sessionId,
                                LineIndex = l.LineIndex,
                                TranslatedText = "[CANCELLED - Đợi quá lâu]",
                                Success = false,
                                ErrorType = "CANCELLED",
                                ErrorDetail = "Operation was cancelled due to timeout"
                            }).ToList();

                            await SaveResultsToDb(sessionId, cancelledResults);
                            result.Results = cancelledResults;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Exception processing batch {BatchIndex} for job {SessionId}", currentBatchIndex, sessionId);

                            var errorResults = batch.Select(l => new TranslatedSrtLineDb
                            {
                                SessionId = sessionId,
                                LineIndex = l.LineIndex,
                                TranslatedText = $"[LỖI: {ex.Message.Substring(0, Math.Min(100, ex.Message.Length))}]",
                                Success = false,
                                ErrorType = "EXCEPTION",
                                ErrorDetail = ex.Message
                            }).ToList();

                            await SaveResultsToDb(sessionId, errorResults);
                            result.Results = errorResults;
                        }
                        finally
                        {
                            if (rateLimitSlotId != null)
                            {
                                _globalRateLimiter.ReleaseSlot(rateLimitSlotId);
                            }
                            semaphore.Release();
                            batchResults[currentBatchIndex] = result;
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(processingTasks);

                // =================================================================
                // CẢI TIẾN: VALIDATION VÀ RETRY CÁC BATCH THẤT BẠI
                // =================================================================

                await ValidateAndRetryFailedBatchesAsync(
                    sessionId, job, batches, batchResults, settings, activeModel.ModelName,
                    poolToUse, encryptionService, enabledKeys, rpmPerKey, cancellationToken);

                // Kiểm tra và hoàn trả lượt dịch
                try { await CheckAndRefundFailedLinesAsync(sessionId); }
                catch (Exception ex) { _logger.LogError(ex, "CheckAndRefund failed"); }

                await UpdateJobStatus(sessionId, JobStatus.Completed);
                _logger.LogInformation("🎉 Job {SessionId} COMPLETED!", sessionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {SessionId} cancelled (timeout or user request)", sessionId);
                await CheckAndRefundFailedLinesAsync(sessionId);
                await UpdateJobStatus(sessionId, JobStatus.Failed, "Job đã bị hủy.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error processing job {SessionId}", sessionId);
                await UpdateJobStatus(sessionId, JobStatus.Failed, ex.Message);
            }
            finally
            {
                _cancellationService.UnregisterJob(sessionId, userId);
            }
        }

        // =====================================================================
        // CẢI TIẾN MỚI: BATCH PROCESSING VỚI RETRY THÔNG MINH
        // =====================================================================

        private class BatchProcessResult
        {
            public int BatchIndex { get; set; }
            public bool Success { get; set; }
            public List<TranslatedSrtLineDb>? Results { get; set; }
            public int? UsedKeyId { get; set; }
            public int TokensUsed { get; set; }
        }

        private async Task<(bool success, List<TranslatedSrtLineDb> results, int tokensUsed, int? usedKeyId)> ProcessBatchWithRetryAsync(
            List<OriginalSrtLineDb> batch,
            TranslationJobDb job,
            LocalApiSetting settings,
            string modelName,
            ApiPoolType poolType,
            IEncryptionService encryptionService,
            List<ManagedApiKey> availableKeys,
            int rpmPerKey,
            int batchIndex,
            CancellationToken token)
        {
            int batchRetryCount = 0;

            while (batchRetryCount < MAX_BATCH_RETRY_ATTEMPTS && !token.IsCancellationRequested)
            {
                try
                {
                    var (results, tokensUsed, usedKeyId) = await TranslateBatchAsync(
                        batch, job, settings, modelName, job.SystemInstruction,
                        poolType, encryptionService, availableKeys, rpmPerKey, token);

                    // Kiểm tra kết quả
                    int successCount = results.Count(r => r.Success);
                    int totalCount = results.Count;
                    double successRate = (double)successCount / totalCount;

                    if (successRate >= 0.7) // Ít nhất 70% thành công
                    {
                        _logger.LogInformation("Batch {BatchIndex} completed: {Success}/{Total} lines ({Rate:P0})",
                            batchIndex, successCount, totalCount, successRate);
                        return (true, results, tokensUsed, usedKeyId);
                    }

                    // Quá nhiều lỗi, retry batch
                    _logger.LogWarning("Batch {BatchIndex} has low success rate ({Rate:P0}). Retry {Retry}/{Max}",
                        batchIndex, successRate, batchRetryCount + 1, MAX_BATCH_RETRY_ATTEMPTS);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogWarning(ex, "Batch {BatchIndex} failed with exception. Retry {Retry}/{Max}",
                        batchIndex, batchRetryCount + 1, MAX_BATCH_RETRY_ATTEMPTS);
                }

                batchRetryCount++;
                if (batchRetryCount < MAX_BATCH_RETRY_ATTEMPTS)
                {
                    await Task.Delay(BATCH_RETRY_DELAY_MS * batchRetryCount, token);
                }
            }

            // Tất cả retry thất bại - trả về error results
            _logger.LogError("Batch {BatchIndex} FAILED after {Max} retry attempts", batchIndex, MAX_BATCH_RETRY_ATTEMPTS);

            var failedResults = batch.Select(l => new TranslatedSrtLineDb
            {
                SessionId = job.SessionId,
                LineIndex = l.LineIndex,
                TranslatedText = "[LỖI: Không thể dịch sau nhiều lần thử]",
                Success = false,
                ErrorType = "MAX_BATCH_RETRIES_EXCEEDED",
                ErrorDetail = $"Failed after {MAX_BATCH_RETRY_ATTEMPTS} batch retry attempts"
            }).ToList();

            return (false, failedResults, 0, null);
        }

        // =====================================================================
        // CẢI TIẾN MỚI: VALIDATION VÀ RETRY CÁC BATCH THẤT BẠI SAU KHI HOÀN THÀNH
        // =====================================================================

        private async Task ValidateAndRetryFailedBatchesAsync(
            string sessionId,
            TranslationJobDb job,
            List<List<OriginalSrtLineDb>> batches,
            ConcurrentDictionary<int, BatchProcessResult> batchResults,
            LocalApiSetting settings,
            string modelName,
            ApiPoolType poolType,
            IEncryptionService encryptionService,
            List<ManagedApiKey> availableKeys,
            int rpmPerKey,
            CancellationToken token)
        {
            _logger.LogInformation("Validating results for job {SessionId}...", sessionId);

            // Tìm các batch thất bại hoặc thiếu
            var failedBatchIndices = new List<int>();

            for (int i = 0; i < batches.Count; i++)
            {
                if (!batchResults.TryGetValue(i, out var result) || !result.Success)
                {
                    failedBatchIndices.Add(i);
                }
            }

            if (!failedBatchIndices.Any())
            {
                _logger.LogInformation("All {Count} batches completed successfully for job {SessionId}", batches.Count, sessionId);
                return;
            }

            _logger.LogWarning("Found {FailedCount}/{TotalCount} failed batches for job {SessionId}. Retrying...",
                failedBatchIndices.Count, batches.Count, sessionId);

            // Retry các batch thất bại
            foreach (var batchIndex in failedBatchIndices)
            {
                if (token.IsCancellationRequested) break;

                var batch = batches[batchIndex];

                try
                {
                    _logger.LogInformation("Retrying failed batch {BatchIndex} for job {SessionId}", batchIndex, sessionId);

                    // Xóa kết quả cũ
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var oldResults = await context.TranslatedSrtLines
                        .Where(r => r.SessionId == sessionId && batch.Select(b => b.LineIndex).Contains(r.LineIndex))
                        .ToListAsync(token);

                    if (oldResults.Any())
                    {
                        context.TranslatedSrtLines.RemoveRange(oldResults);
                        await context.SaveChangesAsync(token);
                    }

                    // Retry với timeout riêng
                    using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    retryCts.CancelAfter(TimeSpan.FromMinutes(2));

                    var (success, results, tokensUsed, usedKeyId) = await ProcessBatchWithRetryAsync(
                        batch, job, settings, modelName, poolType, encryptionService,
                        availableKeys, rpmPerKey, batchIndex, retryCts.Token);

                    if (results != null && results.Any())
                    {
                        await SaveResultsToDb(sessionId, results);

                        if (usedKeyId.HasValue && tokensUsed > 0)
                        {
                            await UpdateUsageInDb(usedKeyId.Value, tokensUsed);
                        }
                    }

                    _logger.LogInformation("Retry batch {BatchIndex}: {Status}", batchIndex, success ? "SUCCESS" : "STILL FAILED");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrying batch {BatchIndex} for job {SessionId}", batchIndex, sessionId);
                }

                // Delay giữa các retry
                await Task.Delay(1000, token);
            }
        }

        // =====================================================================
        // CẢI TIẾN: TRANSLATE BATCH VỚI LOGIC KEY ACQUISITION TỐT HƠN
        // =====================================================================

        private async Task<(List<TranslatedSrtLineDb> results, int tokensUsed, int? usedKeyId)> TranslateBatchAsync(
            List<OriginalSrtLineDb> batch, TranslationJobDb job, LocalApiSetting settings,
            string modelName, string systemInstruction, ApiPoolType poolType,
            IEncryptionService encryptionService, List<ManagedApiKey> availableKeys, int rpmPerKey, CancellationToken token)
        {
            var payloadBuilder = new StringBuilder();
            foreach (var line in batch)
            {
                payloadBuilder.AppendLine($"{line.LineIndex}: {line.OriginalText.Replace("\r\n", " ").Replace("\n", " ")}");
            }
            string payload = payloadBuilder.ToString().TrimEnd();

            var generationConfig = new JObject
            {
                ["temperature"] = (decimal)settings.Temperature,
                ["topP"] = 0.95,
                ["maxOutputTokens"] = settings.MaxOutputTokens
            };

            if (settings.EnableThinkingBudget && settings.ThinkingBudget > 0)
            {
                generationConfig["thinking_config"] = new JObject { ["thinking_budget"] = settings.ThinkingBudget };
            }

            var requestPayloadObject = new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = $"Dịch các câu thoại sau sang {job.TargetLanguage}:\n\n{payload}" } } } },
                system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                generationConfig
            };

            string jsonPayload = JsonConvert.SerializeObject(requestPayloadObject, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            HashSet<int> triedKeyIds = new HashSet<int>();
            int? successfulKeyId = null;

            for (int attempt = 1; attempt <= settings.MaxRetries; attempt++)
            {
                ManagedApiKey? selectedKey = null;

                try
                {
                    // === CẢI TIẾN: Chờ key với timeout và retry ===
                    selectedKey = await GetAvailableKeyWithTimeoutAsync(
                        availableKeys, poolType, triedKeyIds, rpmPerKey,
                        KEY_ACQUISITION_TIMEOUT_MS, token);

                    if (selectedKey == null)
                    {
                        _logger.LogWarning("Attempt {Attempt}: No key available after timeout. Waiting {Delay}ms before retry...",
                            attempt, ALL_KEYS_BUSY_RETRY_DELAY_MS);

                        // Clear triedKeyIds để thử lại tất cả keys
                        triedKeyIds.Clear();
                        await Task.Delay(ALL_KEYS_BUSY_RETRY_DELAY_MS, token);
                        continue;
                    }

                    triedKeyIds.Add(selectedKey.Id);

                    var apiKey = encryptionService.Decrypt(selectedKey.EncryptedApiKey, selectedKey.Iv);
                    string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

                    _logger.LogDebug("Batch attempt {Attempt}/{MaxRetries}: Using Key ID {KeyId}",
                        attempt, settings.MaxRetries, selectedKey.Id);

                    var (responseText, tokensUsed, errorType, errorDetail, httpStatusCode) =
                        await CallApiWithRetryAsync(apiUrl, jsonPayload, settings, selectedKey.Id, token);

                    // Xử lý lỗi 429
                    if (httpStatusCode == 429)
                    {
                        _logger.LogWarning("Key ID {KeyId} hit rate limit. Setting cooldown...", selectedKey.Id);
                        await _cooldownService.SetCooldownAsync(selectedKey.Id, $"HTTP 429 on attempt {attempt}");

                        if (attempt < settings.MaxRetries)
                        {
                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }
                    }

                    // Xử lý lỗi nghiêm trọng
                    if (httpStatusCode == 401 || httpStatusCode == 403 ||
                        errorType == "INVALID_ARGUMENT" || errorDetail?.Contains("API key") == true)
                    {
                        _logger.LogError("Key ID {KeyId} has critical error. Disabling permanently.", selectedKey.Id);
                        await _cooldownService.DisableKeyPermanentlyAsync(selectedKey.Id, $"{errorType}: {errorDetail}");

                        if (attempt < settings.MaxRetries) continue;
                    }

                    // === THÀNH CÔNG ===
                    if (responseText != null && !responseText.StartsWith("Lỗi", StringComparison.OrdinalIgnoreCase))
                    {
                        successfulKeyId = selectedKey.Id;

                        var results = new List<TranslatedSrtLineDb>();
                        var translatedLinesDict = new Dictionary<int, string>();
                        var regex = new Regex(@"^\s*(\d+):\s*(.*)$", RegexOptions.Multiline);

                        foreach (Match m in regex.Matches(responseText))
                        {
                            if (int.TryParse(m.Groups[1].Value, out int idx))
                                translatedLinesDict[idx] = m.Groups[2].Value.Trim();
                        }

                        // === CẢI TIẾN: Kiểm tra missing với threshold thấp hơn ===
                        int batchCount = batch.Count;
                        int missingCount = batch.Count(line => !translatedLinesDict.ContainsKey(line.LineIndex));
                        double missingRate = (double)missingCount / batchCount;

                        if (missingRate > MISSING_INDEX_RETRY_THRESHOLD && attempt < settings.MaxRetries)
                        {
                            _logger.LogWarning("Batch has {MissingCount}/{TotalCount} missing lines ({Rate:P0}). Retrying...",
                                missingCount, batchCount, missingRate);

                            await Task.Delay(settings.RetryDelayMs * attempt, token);
                            continue;
                        }

                        // Build results
                        foreach (var line in batch)
                        {
                            if (translatedLinesDict.TryGetValue(line.LineIndex, out string? translated))
                            {
                                results.Add(new TranslatedSrtLineDb
                                {
                                    SessionId = job.SessionId,
                                    LineIndex = line.LineIndex,
                                    TranslatedText = string.IsNullOrWhiteSpace(translated) ? "[API DỊCH RỖNG]" : translated,
                                    Success = true
                                });
                            }
                            else
                            {
                                results.Add(new TranslatedSrtLineDb
                                {
                                    SessionId = job.SessionId,
                                    LineIndex = line.LineIndex,
                                    TranslatedText = "[API KHÔNG TRẢ VỀ DÒNG NÀY]",
                                    Success = false,
                                    ErrorType = "MISSING_LINE",
                                    ErrorDetail = "API response missing this line"
                                });
                            }
                        }

                        return (results, tokensUsed, successfulKeyId);
                    }

                    // Lỗi khác - retry
                    if (attempt < settings.MaxRetries)
                    {
                        int delayMs = settings.RetryDelayMs * attempt;
                        _logger.LogWarning("Attempt {Attempt} failed: {Error}. Retrying after {Delay}ms...",
                            attempt, errorType ?? responseText, delayMs);
                        await Task.Delay(delayMs, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception during attempt {Attempt} with Key ID {KeyId}",
                        attempt, selectedKey?.Id);

                    if (attempt >= settings.MaxRetries) break;
                    await Task.Delay(settings.RetryDelayMs * attempt, token);
                }
            }

            // Tất cả attempts thất bại
            _logger.LogError("Batch translation FAILED after {MaxRetries} attempts", settings.MaxRetries);

            var failedResults = batch.Select(l => new TranslatedSrtLineDb
            {
                SessionId = job.SessionId,
                LineIndex = l.LineIndex,
                TranslatedText = "[LỖI: Không thể dịch sau nhiều lần thử]",
                Success = false,
                ErrorType = "MAX_RETRIES_EXCEEDED",
                ErrorDetail = $"Failed after {settings.MaxRetries} API attempts"
            }).ToList();

            return (failedResults, 0, null);
        }

        // =====================================================================
        // CẢI TIẾN MỚI: KEY ACQUISITION VỚI TIMEOUT VÀ WAIT
        // =====================================================================

        private async Task<ManagedApiKey?> GetAvailableKeyWithTimeoutAsync(
            List<ManagedApiKey> availableKeys,
            ApiPoolType poolType,
            HashSet<int> excludeKeyIds,
            int rpmPerKey,
            int timeoutMs,
            CancellationToken token)
        {
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (DateTime.UtcNow - startTime < timeout && !token.IsCancellationRequested)
            {
                // Lọc keys chưa thử và không trong cooldown
                var eligibleKeys = availableKeys
                    .Where(k => !excludeKeyIds.Contains(k.Id) && !_cooldownService.IsInCooldown(k.Id))
                    .ToList();

                if (!eligibleKeys.Any())
                {
                    // Tất cả keys đã thử hoặc trong cooldown - đợi và thử lại
                    await Task.Delay(1000, token);

                    // Refresh cooldown status
                    eligibleKeys = availableKeys
                        .Where(k => !_cooldownService.IsInCooldown(k.Id))
                        .ToList();

                    if (!eligibleKeys.Any())
                    {
                        _logger.LogWarning("All {Count} keys in cooldown. Waiting...", availableKeys.Count);
                        continue;
                    }

                    // Clear exclude list để thử lại
                    excludeKeyIds.Clear();
                }

                // Round-robin selection
                var selectedKey = GetNextKeyRoundRobin(eligibleKeys, poolType);

                // Kiểm tra RPM limiter
                var semaphore = _keyRpmLimiters.GetOrAdd(selectedKey.Id, _ => new SemaphoreSlim(rpmPerKey, rpmPerKey));

                if (await semaphore.WaitAsync(RPM_WAIT_TIMEOUT_MS, token))
                {
                    ScheduleSemaphoreRelease(semaphore, TimeSpan.FromMinutes(1));
                    _logger.LogDebug("Key ID {KeyId} acquired. RPM slots: {Remaining}/{Total}",
                        selectedKey.Id, semaphore.CurrentCount, rpmPerKey);
                    return selectedKey;
                }

                // Key này đã đạt RPM limit - thử key khác
                _logger.LogDebug("Key ID {KeyId} at RPM limit, trying another key", selectedKey.Id);
                excludeKeyIds.Add(selectedKey.Id);

                // Thử các key còn lại
                foreach (var key in eligibleKeys.Where(k => k.Id != selectedKey.Id && !excludeKeyIds.Contains(k.Id)))
                {
                    var keySemaphore = _keyRpmLimiters.GetOrAdd(key.Id, _ => new SemaphoreSlim(rpmPerKey, rpmPerKey));

                    if (await keySemaphore.WaitAsync(50, token)) // Nhanh hơn
                    {
                        ScheduleSemaphoreRelease(keySemaphore, TimeSpan.FromMinutes(1));
                        return key;
                    }

                    excludeKeyIds.Add(key.Id);
                }

                // Tất cả keys đều bận - đợi một chút
                await Task.Delay(500, token);
            }

            return null;
        }

        // =====================================================================
        // CẢI TIẾN: API CALL VỚI PROXY RETRY THÔNG MINH HƠN
        // =====================================================================

        private async Task<(string responseText, int tokensUsed, string? errorType, string? errorDetail, int httpStatusCode)> CallApiWithRetryAsync(
            string url, string jsonPayload, LocalApiSetting settings, int apiKeyId, CancellationToken token)
        {
            var failedProxyIds = new HashSet<int>();
            string userAgent = GenerateRandomUserAgent();
            string? currentProxySlotId = null;
            Proxy? currentProxy = null;
            string requestId = $"key{apiKeyId}_{Guid.NewGuid():N}";
            int apiRetryCount = 0;
            int maxProxyAttempts = 50; // Giới hạn số lần thử proxy
            int proxyAttempts = 0;

            while (!token.IsCancellationRequested && proxyAttempts < maxProxyAttempts)
            {
                proxyAttempts++;

                // Release previous proxy slot
                if (currentProxySlotId != null)
                {
                    _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                    currentProxySlotId = null;
                }

                // Get proxy
                currentProxy = await GetProxyWithAvailableRpmSlotAsync(failedProxyIds, requestId, token);

                if (currentProxy == null && failedProxyIds.Count > 0)
                {
                    _logger.LogWarning("No proxy available after {Count} failures. Trying direct connection.", failedProxyIds.Count);
                }

                // Acquire proxy slot
                if (currentProxy != null)
                {
                    currentProxySlotId = await _proxyRateLimiter.TryAcquireSlotWithTimeoutAsync(
                        currentProxy.Id, requestId, PROXY_RPM_WAIT_TIMEOUT_MS, token);

                    if (currentProxySlotId == null)
                    {
                        failedProxyIds.Add(currentProxy.Id);
                        await Task.Delay(100, token);
                        continue;
                    }
                }

                try
                {
                    using var httpClient = _proxyService.CreateHttpClientWithProxy(currentProxy);
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("User-Agent", userAgent);

                    using HttpResponseMessage response = await httpClient.SendAsync(request, token);
                    string responseBody = await response.Content.ReadAsStringAsync(token);

                    // Mark slot as used
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.MarkSlotAsUsed(currentProxySlotId);
                        currentProxySlotId = null;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        int statusCode = (int)response.StatusCode;

                        // === Xử lý FAILED_PRECONDITION (location not supported) ===
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
                                    _logger.LogError("🚫 Proxy {ProxyId} blocked: {Error}. Disabling...",
                                        currentProxy.Id, errorMessage);

                                    await _proxyService.DisableProxyImmediatelyAsync(currentProxy.Id, "location not supported");
                                    failedProxyIds.Add(currentProxy.Id);
                                    continue; // Thử proxy khác ngay lập tức
                                }
                            }
                            catch (JsonReaderException) { }
                        }

                        // API error - count as retry
                        apiRetryCount++;

                        if (currentProxy != null && statusCode != 429)
                        {
                            await _proxyService.RecordProxyFailureAsync(currentProxy.Id, $"HTTP {statusCode}");
                        }

                        if (apiRetryCount >= settings.MaxRetries)
                        {
                            return ($"Lỗi API: HTTP {statusCode}", 0, $"HTTP_{statusCode}", responseBody, statusCode);
                        }

                        await Task.Delay(settings.RetryDelayMs * apiRetryCount, token);
                        continue;
                    }

                    // Success - record proxy success
                    if (currentProxy != null)
                    {
                        await _proxyService.RecordProxySuccessAsync(currentProxy.Id);
                    }

                    // Parse response
                    JObject parsedBody;
                    try
                    {
                        parsedBody = JObject.Parse(responseBody);
                    }
                    catch (JsonReaderException)
                    {
                        if (responseBody.TrimStart().StartsWith("<"))
                        {
                            // HTML response - proxy error
                            if (currentProxy != null)
                            {
                                await _proxyService.RecordProxyFailureAsync(currentProxy.Id, "HTML response");
                                failedProxyIds.Add(currentProxy.Id);
                            }
                            continue;
                        }

                        apiRetryCount++;
                        if (apiRetryCount >= settings.MaxRetries)
                        {
                            return ("Lỗi: Response không phải JSON", 0, "INVALID_JSON", "Parse error", 200);
                        }
                        continue;
                    }

                    // Check for API errors in response
                    if (parsedBody?["error"] != null)
                    {
                        string errorMsg = parsedBody["error"]?["message"]?.ToString() ?? "Unknown";
                        apiRetryCount++;

                        if (apiRetryCount >= settings.MaxRetries)
                        {
                            return ($"Lỗi API: {errorMsg}", 0, "API_ERROR", errorMsg, 200);
                        }

                        await Task.Delay(settings.RetryDelayMs * apiRetryCount, token);
                        continue;
                    }

                    // Check blocked content
                    if (parsedBody?["promptFeedback"]?["blockReason"] != null)
                    {
                        string blockReason = parsedBody["promptFeedback"]["blockReason"].ToString();
                        return ($"Nội dung bị chặn: {blockReason}", 0, "BLOCKED_CONTENT", blockReason, 200);
                    }

                    // Check finish reason
                    var finishReason = parsedBody?["candidates"]?[0]?["finishReason"]?.ToString();
                    if (!string.IsNullOrEmpty(finishReason) && finishReason != "STOP")
                    {
                        apiRetryCount++;
                        if (apiRetryCount >= settings.MaxRetries)
                        {
                            return ($"FinishReason: {finishReason}", 0, "FINISH_REASON", finishReason, 200);
                        }
                        continue;
                    }

                    int tokens = parsedBody?["usageMetadata"]?["totalTokenCount"]?.Value<int>() ?? 0;
                    string? responseText = parsedBody?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                    if (responseText == null)
                    {
                        apiRetryCount++;
                        if (apiRetryCount >= settings.MaxRetries)
                        {
                            return ("Lỗi: Response rỗng", 0, "EMPTY_RESPONSE", "Empty", 200);
                        }
                        continue;
                    }

                    return (responseText, tokens, null, null, 200);
                }
                catch (HttpRequestException ex)
                {
                    // Release slot early
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                        currentProxySlotId = null;
                    }

                    if (currentProxy != null)
                    {
                        var errorDesc = ProxyService.GetProxyErrorDescription(ex);

                        if (ProxyService.IsCriticalProxyError(ex))
                        {
                            _logger.LogWarning("Critical proxy error for {ProxyId}: {Error}. Disabling...",
                                currentProxy.Id, errorDesc);
                            await _proxyService.DisableProxyImmediatelyAsync(currentProxy.Id, errorDesc);
                        }
                        else
                        {
                            await _proxyService.RecordProxyFailureAsync(currentProxy.Id, errorDesc);
                        }

                        failedProxyIds.Add(currentProxy.Id);
                    }

                    await Task.Delay(PROXY_SEARCH_DELAY_MS, token);
                    continue;
                }
                catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
                {
                    // Timeout - try another proxy
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                        currentProxySlotId = null;
                    }

                    if (currentProxy != null)
                    {
                        await _proxyService.RecordProxyFailureAsync(currentProxy.Id, "Timeout", isTimeoutError: true);
                        failedProxyIds.Add(currentProxy.Id);
                    }

                    await Task.Delay(PROXY_SEARCH_DELAY_MS, token);
                    continue;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    if (currentProxySlotId != null)
                    {
                        _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
                        currentProxySlotId = null;
                    }

                    if (currentProxy != null)
                    {
                        await _proxyService.RecordProxyFailureAsync(currentProxy.Id, ex.Message);
                        failedProxyIds.Add(currentProxy.Id);
                    }

                    // Check if no more proxies
                    var nextProxy = await _proxyService.GetNextProxyAsync(failedProxyIds);
                    if (nextProxy == null)
                    {
                        return ($"Lỗi: Hết proxy ({failedProxyIds.Count} lỗi)", 0, "ALL_PROXIES_FAILED", ex.Message, 0);
                    }

                    await Task.Delay(PROXY_SEARCH_DELAY_MS, token);
                    continue;
                }
            }

            // Cleanup
            if (currentProxySlotId != null)
            {
                _proxyRateLimiter.ReleaseSlotEarly(currentProxySlotId);
            }

            if (token.IsCancellationRequested)
            {
                return ("Lỗi: Request cancelled", 0, "CANCELLED", "Operation cancelled", 0);
            }

            return ($"Lỗi: Quá số lần thử proxy ({maxProxyAttempts})", 0, "MAX_PROXY_ATTEMPTS", "Exceeded max attempts", 0);
        }

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        private async Task<Proxy?> GetProxyWithAvailableRpmSlotAsync(HashSet<int> excludeProxyIds, string requestId, CancellationToken token)
        {
            var proxy = await _proxyService.GetNextProxyAsync(excludeProxyIds);
            if (proxy == null) return null;

            if (_proxyRateLimiter.HasAvailableSlot(proxy.Id)) return proxy;

            var triedProxyIds = new HashSet<int>(excludeProxyIds) { proxy.Id };

            while (!token.IsCancellationRequested)
            {
                var nextProxy = await _proxyService.GetNextProxyAsync(triedProxyIds);
                if (nextProxy == null) return proxy;

                if (_proxyRateLimiter.HasAvailableSlot(nextProxy.Id)) return nextProxy;

                triedProxyIds.Add(nextProxy.Id);
            }

            return proxy;
        }

        private ManagedApiKey GetNextKeyRoundRobin(List<ManagedApiKey> eligibleKeys, ApiPoolType poolType)
        {
            lock (_roundRobinLock)
            {
                int currentIndex;
                if (poolType == ApiPoolType.Paid)
                {
                    if (_paidKeyRoundRobinIndex >= eligibleKeys.Count) _paidKeyRoundRobinIndex = 0;
                    currentIndex = _paidKeyRoundRobinIndex++;
                }
                else
                {
                    if (_freeKeyRoundRobinIndex >= eligibleKeys.Count) _freeKeyRoundRobinIndex = 0;
                    currentIndex = _freeKeyRoundRobinIndex++;
                }
                return eligibleKeys[currentIndex];
            }
        }

        private void EnsureKeyRpmLimiter(int keyId, int rpmCapacity)
        {
            if (_keyRpmCapacities.TryGetValue(keyId, out int current) && current == rpmCapacity) return;

            lock (_roundRobinLock)
            {
                if (_keyRpmCapacities.TryGetValue(keyId, out current) && current == rpmCapacity) return;

                if (_keyRpmLimiters.TryRemove(keyId, out var old))
                {
                    try { old.Dispose(); } catch { }
                }

                _keyRpmLimiters[keyId] = new SemaphoreSlim(rpmCapacity, rpmCapacity);
                _keyRpmCapacities[keyId] = rpmCapacity;
            }
        }

        private void ScheduleSemaphoreRelease(SemaphoreSlim semaphore, TimeSpan delay)
        {
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                try { semaphore.Release(); }
                catch (SemaphoreFullException) { }
                catch (ObjectDisposedException) { }
            });
        }

        private int GetValidBatchSize(LocalApiSetting settings)
        {
            return settings?.BatchSize > 0 ? settings.BatchSize : MIN_BATCH_SIZE;
        }

        private async Task<LocalApiSetting> GetLocalApiSettingsAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            return await context.LocalApiSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == DEFAULT_LOCAL_API_SETTING_ID, cancellationToken)
                ?? new LocalApiSetting();
        }

        private async Task UpdateJobStatus(string sessionId, JobStatus newStatus, string? errorMessage = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var job = await context.TranslationJobs.FindAsync(sessionId);

                if (job == null || job.Status == JobStatus.Completed || job.Status == JobStatus.Failed) return;

                job.Status = newStatus;
                if (errorMessage != null) job.ErrorMessage = errorMessage;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update job status: {SessionId}", sessionId);
            }
        }

        private async Task SaveResultsToDb(string sessionId, List<TranslatedSrtLineDb> results)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.TranslatedSrtLines.AddRangeAsync(results);
            await context.SaveChangesAsync();
        }

        private async Task UpdateUsageInDb(int apiKeyId, int tokensUsed)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var apiKey = await context.ManagedApiKeys.FindAsync(apiKeyId);
                if (apiKey == null) return;

                var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
                var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(apiKey.LastRequestCountResetUtc, vietnamTimeZone);

                if (lastResetInVietnam.Date < vietnamNow.Date)
                {
                    apiKey.RequestsToday = 0;
                    apiKey.LastRequestCountResetUtc = DateTime.UtcNow.Date;
                }

                apiKey.RequestsToday++;
                if (tokensUsed > 0) apiKey.TotalTokensUsed += tokensUsed;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update usage for key {KeyId}", apiKeyId);
            }
        }

        private async Task CheckAndRefundFailedLinesAsync(string sessionId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await context.TranslationJobs
                    .Include(j => j.TranslatedLines)
                    .Include(j => j.OriginalLines)
                    .FirstOrDefaultAsync(j => j.SessionId == sessionId);

                if (job == null) return;

                var failedLines = job.TranslatedLines.Where(l => !l.Success).ToList();
                int failedCount = failedLines.Count;
                int totalCount = job.OriginalLines.Count;

                if (failedCount > 0)
                {
                    _logger.LogWarning("Job {SessionId}: {FailedCount}/{TotalCount} lines failed",
                        sessionId, failedCount, totalCount);

                    var user = await context.Users.FindAsync(job.UserId);
                    if (user != null)
                    {
                        int refundAmount = Math.Min(failedCount, user.LocalSrtLinesUsedToday);
                        if (refundAmount > 0)
                        {
                            user.LocalSrtLinesUsedToday -= refundAmount;
                            await context.SaveChangesAsync();
                            _logger.LogInformation("Refunded {RefundAmount} lines for user {UserId}", refundAmount, user.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during refund for job {SessionId}", sessionId);
            }
        }

        // =====================================================================
        // PUBLIC METHODS (CreateJobAsync, GetJobResultsAsync, etc.)
        // Giữ nguyên từ code gốc với các cải tiến nhỏ
        // =====================================================================

        public record CreateJobPublicResult(string Status, string Message, string SessionId = null, int RemainingLines = 0);

        public async Task<CreateJobPublicResult> CreateJobAsync(int userId, string genre, string targetLanguage, List<SrtLine> allLines, string systemInstruction, bool acceptPartial)
        {
            _logger.LogInformation("Job creation request for User ID {UserId}. Lines: {LineCount}", userId, allLines.Count);

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await context.Users.FindAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");

            var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
            var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastLocalSrtResetUtc, vietnamTimeZone);

            if (lastResetInVietnam.Date < vietnamNow.Date)
            {
                user.LocalSrtLinesUsedToday = 0;
                user.LastLocalSrtResetUtc = DateTime.UtcNow.Date;
                await context.SaveChangesAsync();
            }

            int remainingLines = user.DailyLocalSrtLimit - user.LocalSrtLinesUsedToday;
            int requestedLines = allLines.Count;

            if (requestedLines <= remainingLines)
            {
                user.LocalSrtLinesUsedToday += requestedLines;
                var sessionId = await CreateJobInDb(user, genre, targetLanguage, systemInstruction, allLines, context);
                _ = ProcessJob(sessionId, user.Id, user.Tier);
                return new CreateJobPublicResult("Accepted", "OK", sessionId);
            }

            if (remainingLines > 0)
            {
                if (acceptPartial)
                {
                    var partialLines = allLines.Take(remainingLines).ToList();
                    user.LocalSrtLinesUsedToday += partialLines.Count;
                    var sessionId = await CreateJobInDb(user, genre, targetLanguage, systemInstruction, partialLines, context);
                    _ = ProcessJob(sessionId, user.Id, user.Tier);
                    return new CreateJobPublicResult("Accepted", "OK", sessionId);
                }
                else
                {
                    string message = $"Yêu cầu: {requestedLines} dòng, còn lại: {remainingLines} dòng.\nDịch {remainingLines} dòng đầu?";
                    return new CreateJobPublicResult("PartialContent", message, RemainingLines: remainingLines);
                }
            }

            return new CreateJobPublicResult("Error", $"Đã hết {user.DailyLocalSrtLimit} lượt dịch trong ngày.");
        }

        private async Task<string> CreateJobInDb(User user, string genre, string targetLanguage, string systemInstruction, List<SrtLine> linesToProcess, AppDbContext context)
        {
            var sessionId = Guid.NewGuid().ToString();
            var newJob = new TranslationJobDb
            {
                SessionId = sessionId,
                UserId = user.Id,
                Genre = genre,
                TargetLanguage = targetLanguage,
                SystemInstruction = systemInstruction,
                Status = JobStatus.Pending,
                OriginalLines = linesToProcess.Select(l => new OriginalSrtLineDb { LineIndex = l.Index, OriginalText = l.OriginalText }).ToList()
            };
            context.TranslationJobs.Add(newJob);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created Job {SessionId} with {LineCount} lines", sessionId, linesToProcess.Count);
            return sessionId;
        }

        public async Task<(bool isCompleted, string errorMessage)> GetJobStatusAsync(string sessionId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await context.TranslationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.SessionId == sessionId);
            if (job == null) return (true, "Session không tồn tại.");
            bool isFinished = job.Status == JobStatus.Completed || job.Status == JobStatus.Failed;
            return (isFinished, job.ErrorMessage);
        }

        public async Task<List<TranslatedSrtLine>> GetJobResultsAsync(string sessionId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var transaction = await context.Database.BeginTransactionAsync();
            var resultsDb = await context.TranslatedSrtLines.Where(l => l.SessionId == sessionId).ToListAsync();

            if (resultsDb.Any())
            {
                context.TranslatedSrtLines.RemoveRange(resultsDb);
                await context.SaveChangesAsync();
            }
            await transaction.CommitAsync();

            var translatedLines = resultsDb
                .Select(l => new TranslatedSrtLine
                {
                    Index = l.LineIndex,
                    TranslatedText = l.TranslatedText,
                    Success = l.Success
                })
                .ToList();

            // Auto-retry failed lines before returning
            var failedIndexes = resultsDb.Where(l => !l.Success).Select(l => l.LineIndex).Distinct().ToList();

            if (failedIndexes.Any())
            {
                _logger.LogInformation("Auto-retrying {Count} failed lines for session {SessionId}", failedIndexes.Count, sessionId);
                // TODO: Implement RetranslateLinesAsync if needed
            }

            return translatedLines;
        }
    }
}