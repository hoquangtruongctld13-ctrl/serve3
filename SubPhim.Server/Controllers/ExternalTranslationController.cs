using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Models;
using SubPhim.Server.Services;
using System.Security.Claims;

namespace SubPhim.Server.Controllers
{
    /// <summary>
    /// External API Controller for VIP Translation
    /// Provides API key authenticated access to translation services
    /// </summary>
    [ApiController]
    [Route("api/v1/external")]
    [Authorize(Policy = "ExternalApiPolicy")]
    public class ExternalTranslationController : ControllerBase
    {
        private readonly VipTranslationService _vipTranslationService;
        private readonly IExternalApiCreditService _creditService;
        private readonly IExternalApiKeyService _keyService;
        private readonly AppDbContext _context;
        private readonly ILogger<ExternalTranslationController> _logger;

        public ExternalTranslationController(
            VipTranslationService vipTranslationService,
            IExternalApiCreditService creditService,
            IExternalApiKeyService keyService,
            AppDbContext context,
            ILogger<ExternalTranslationController> logger)
        {
            _vipTranslationService = vipTranslationService;
            _creditService = creditService;
            _keyService = keyService;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Start a new translation job
        /// </summary>
        [HttpPost("translation/start")]
        public async Task<IActionResult> StartTranslation([FromBody] StartTranslationRequest request)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                // Validate request
                if (request.Lines == null || request.Lines.Count == 0)
                {
                    return BadRequest(new 
                    { 
                        status = "InvalidRequest", 
                        message = "Lines array is required and must not be empty" 
                    });
                }

                // Estimate credits required
                var totalInputChars = request.Lines.Sum(l => l.Text?.Length ?? 0);
                var estimatedCredits = await _creditService.EstimateCredits(totalInputChars);
                
                // Check if sufficient credits
                if (!await _creditService.HasSufficientCredits(apiKeyId, estimatedCredits))
                {
                    var balance = await _creditService.GetBalance(apiKeyId);
                    return StatusCode(402, new
                    {
                        status = "InsufficientCredits",
                        currentBalance = balance,
                        estimatedRequired = estimatedCredits,
                        message = "Không đủ credit. Vui lòng nạp thêm."
                    });
                }

                // Create usage log
                var sessionId = Guid.NewGuid().ToString("N");
                var usageLog = new ExternalApiUsageLog
                {
                    ApiKeyId = apiKeyId,
                    SessionId = sessionId,
                    Endpoint = "/api/v1/external/translation/start",
                    TargetLanguage = request.TargetLanguage,
                    InputLines = request.Lines.Count,
                    Status = UsageStatus.Pending,
                    ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = HttpContext.Request.Headers["User-Agent"].ToString()
                };
                
                _context.ExternalApiUsageLogs.Add(usageLog);
                await _context.SaveChangesAsync();

                // Start translation job using VipTranslationService
                // Note: We create a fake userId = -apiKeyId to distinguish from regular users
                var fakeUserId = -apiKeyId;
                
                var result = await _vipTranslationService.CreateJobAsync(
                    fakeUserId,
                    request.TargetLanguage,
                    request.Lines.Select(l => new SrtLine 
                    { 
                        Index = l.Index, 
                        OriginalText = l.Text 
                    }).ToList(),
                    request.SystemInstruction ?? "Dịch tự nhiên, phù hợp ngữ cảnh"
                );

                if (result.Status == "Error")
                {
                    // Update usage log with error
                    usageLog.Status = UsageStatus.Failed;
                    usageLog.ErrorMessage = result.Message;
                    await _context.SaveChangesAsync();
                    
                    return BadRequest(new
                    {
                        status = "Error",
                        message = result.Message
                    });
                }

                // Update usage log with the VIP session ID
                usageLog.SessionId = result.SessionId ?? sessionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "External API Key {ApiKeyId} started translation job {SessionId}",
                    apiKeyId, result.SessionId);

                return Ok(new
                {
                    status = "Accepted",
                    sessionId = result.SessionId,
                    estimatedCredits,
                    message = "Job started successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting external translation");
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get translation results for a session
        /// </summary>
        [HttpGet("translation/result/{sessionId}")]
        public async Task<IActionResult> GetResult(string sessionId)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                // Verify this session belongs to this API key
                var usageLog = await _context.ExternalApiUsageLogs
                    .FirstOrDefaultAsync(l => l.SessionId == sessionId && l.ApiKeyId == apiKeyId);

                if (usageLog == null)
                {
                    return NotFound(new
                    {
                        status = "NotFound",
                        message = "Session not found or does not belong to this API key"
                    });
                }

                // Get results from VipTranslationService
                var newLines = await _vipTranslationService.GetResultsAsync(sessionId);
                var (isCompleted, errorMessage) = await _vipTranslationService.GetStatusAsync(sessionId);

                if (isCompleted)
                {
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        // Job failed
                        usageLog.Status = UsageStatus.Failed;
                        usageLog.ErrorMessage = errorMessage;
                        await _context.SaveChangesAsync();
                        
                        return Ok(new
                        {
                            status = "Failed",
                            error = new
                            {
                                code = "TRANSLATION_ERROR",
                                message = errorMessage,
                                creditsRefunded = usageLog.CreditsCharged
                            }
                        });
                    }
                    else
                    {
                        // Job completed successfully - charge credits if not already charged
                        if (usageLog.Status == UsageStatus.Pending && newLines != null)
                        {
                            var totalOutputChars = newLines.Sum(l => l.TranslatedText?.Length ?? 0);
                            await _creditService.ChargeCredits(apiKeyId, sessionId, totalOutputChars);
                            
                            // Reload usage log to get updated values
                            await _context.Entry(usageLog).ReloadAsync();
                        }

                        return Ok(new
                        {
                            status = "Completed",
                            result = new
                            {
                                lines = newLines?.Select(l => new 
                                { 
                                    index = l.Index, 
                                    translatedText = l.TranslatedText 
                                }).ToList(),
                                totalCharacters = usageLog.OutputCharacters,
                                creditsCharged = usageLog.CreditsCharged,
                                geminiErrors = usageLog.GeminiErrors != null 
                                    ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(usageLog.GeminiErrors)
                                    : new List<string>()
                            }
                        });
                    }
                }
                else
                {
                    // Job still processing
                    var completedCount = newLines?.Count ?? 0;
                    var processedLines = newLines?.Select(l => new 
                    { 
                        index = l.Index, 
                        translatedText = l.TranslatedText 
                    }).ToList();
                    
                    return Ok(new
                    {
                        status = "Processing",
                        progress = new
                        {
                            completedLines = completedCount,
                            totalLines = usageLog.InputLines,
                            percentage = usageLog.InputLines > 0 
                                ? (int)((completedCount / (double)usageLog.InputLines) * 100) 
                                : 0
                        },
                        newLines = processedLines
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting translation result for session {SessionId}", sessionId);
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Cancel a translation job
        /// </summary>
        [HttpPost("translation/cancel/{sessionId}")]
        public async Task<IActionResult> CancelTranslation(string sessionId)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                // Verify this session belongs to this API key
                var usageLog = await _context.ExternalApiUsageLogs
                    .FirstOrDefaultAsync(l => l.SessionId == sessionId && l.ApiKeyId == apiKeyId);

                if (usageLog == null)
                {
                    return NotFound(new
                    {
                        status = "NotFound",
                        message = "Session not found or does not belong to this API key"
                    });
                }

                // Cancel the job
                var fakeUserId = -apiKeyId;
                var success = await _vipTranslationService.CancelJobAsync(sessionId, fakeUserId);
                
                if (success)
                {
                    // Refund credits if any were charged
                    await _creditService.RefundCredits(apiKeyId, sessionId, "Job cancelled by user");
                    
                    usageLog.Status = UsageStatus.Cancelled;
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    status = success ? "Cancelled" : "Error",
                    creditsRefunded = usageLog.CreditsCharged,
                    message = success ? "Job đã hủy. Credit chưa sử dụng đã được hoàn trả." : "Unable to cancel job"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling translation for session {SessionId}", sessionId);
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get API key account information
        /// </summary>
        [HttpGet("account/info")]
        public async Task<IActionResult> GetAccountInfo()
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                var apiKey = await _keyService.GetApiKeyByIdAsync(apiKeyId);
                if (apiKey == null)
                    return NotFound(new { status = "NotFound", message = "API key not found" });

                var (creditsPerChar, vndPerCredit) = await _creditService.GetPricingSettings();
                
                // Get current RPM usage from cache
                var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
                var windowKey = $"rpm_{apiKeyId}_{currentMinute}";
                var currentRpmUsage = 0; // Would need to inject IMemoryCache to get actual value

                return Ok(new
                {
                    keyId = $"{apiKey.KeyPrefix}...{apiKey.KeySuffix}",
                    displayName = apiKey.DisplayName,
                    creditBalance = apiKey.CreditBalance,
                    rpmLimit = apiKey.RpmLimit,
                    currentRpmUsage,
                    pricing = new
                    {
                        creditsPerCharacter = creditsPerChar,
                        vndPerCredit
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account info");
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get usage history
        /// </summary>
        [HttpGet("account/usage")]
        public async Task<IActionResult> GetUsageHistory(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                pageSize = Math.Min(pageSize, 100); // Max 100 items per page
                
                var query = _context.ExternalApiUsageLogs
                    .Where(l => l.ApiKeyId == apiKeyId);

                if (from.HasValue)
                    query = query.Where(l => l.StartedAt >= from.Value);
                
                if (to.HasValue)
                    query = query.Where(l => l.StartedAt <= to.Value);

                var totalItems = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                var logs = await query
                    .OrderByDescending(l => l.StartedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Calculate summary
                var summaryQuery = _context.ExternalApiUsageLogs
                    .Where(l => l.ApiKeyId == apiKeyId && l.Status == UsageStatus.Completed);
                
                if (from.HasValue)
                    summaryQuery = summaryQuery.Where(l => l.StartedAt >= from.Value);
                if (to.HasValue)
                    summaryQuery = summaryQuery.Where(l => l.StartedAt <= to.Value);

                var summary = await summaryQuery
                    .GroupBy(l => 1)
                    .Select(g => new
                    {
                        totalJobs = g.Count(),
                        totalCreditsUsed = g.Sum(l => l.CreditsCharged),
                        totalCharactersTranslated = g.Sum(l => l.OutputCharacters)
                    })
                    .FirstOrDefaultAsync();

                var (_, vndPerCredit) = await _creditService.GetPricingSettings();
                
                return Ok(new
                {
                    summary = new
                    {
                        totalJobs = summary?.totalJobs ?? 0,
                        totalCreditsUsed = summary?.totalCreditsUsed ?? 0,
                        totalCharactersTranslated = summary?.totalCharactersTranslated ?? 0,
                        estimatedCostVnd = (summary?.totalCreditsUsed ?? 0) * vndPerCredit
                    },
                    items = logs.Select(l => new
                    {
                        sessionId = l.SessionId,
                        startedAt = l.StartedAt,
                        completedAt = l.CompletedAt,
                        status = l.Status.ToString(),
                        inputLines = l.InputLines,
                        outputCharacters = l.OutputCharacters,
                        creditsCharged = l.CreditsCharged,
                        targetLanguage = l.TargetLanguage,
                        durationMs = l.DurationMs,
                        geminiErrors = l.GeminiErrors != null 
                            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(l.GeminiErrors)
                            : new List<string>()
                    }).ToList(),
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalPages,
                        totalItems
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting usage history");
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get credit transaction history
        /// </summary>
        [HttpGet("account/transactions")]
        public async Task<IActionResult> GetTransactions(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                pageSize = Math.Min(pageSize, 100);
                
                var totalItems = await _context.ExternalApiCreditTransactions
                    .Where(t => t.ApiKeyId == apiKeyId)
                    .CountAsync();

                var transactions = await _context.ExternalApiCreditTransactions
                    .Where(t => t.ApiKeyId == apiKeyId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var currentBalance = await _creditService.GetBalance(apiKeyId);

                return Ok(new
                {
                    currentBalance,
                    items = transactions.Select(t => new
                    {
                        id = t.Id,
                        type = t.Type.ToString(),
                        amount = t.Amount,
                        balanceAfter = t.BalanceAfter,
                        description = t.Description,
                        createdAt = t.CreatedAt,
                        createdBy = t.CreatedBy
                    }).ToList(),
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                        totalItems
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transactions");
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Estimate cost for translation
        /// </summary>
        [HttpPost("estimate")]
        public async Task<IActionResult> EstimateCost([FromBody] EstimateRequest request)
        {
            try
            {
                if (!int.TryParse(User.FindFirstValue("api_key_id"), out int apiKeyId))
                    return Unauthorized(new { status = "Unauthorized", message = "Invalid API key" });

                var estimatedCredits = await _creditService.EstimateCredits(request.CharacterCount);
                var (_, vndPerCredit) = await _creditService.GetPricingSettings();
                var currentBalance = await _creditService.GetBalance(apiKeyId);

                return Ok(new
                {
                    characterCount = request.CharacterCount,
                    estimatedCredits,
                    estimatedCostVnd = estimatedCredits * vndPerCredit,
                    currentBalance,
                    hasEnoughCredits = currentBalance >= estimatedCredits
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error estimating cost");
                return StatusCode(500, new
                {
                    status = "Error",
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        // Request DTOs
        public class StartTranslationRequest
        {
            public required string TargetLanguage { get; set; }
            public required List<TranslationLineInput> Lines { get; set; }
            public string? SystemInstruction { get; set; }
        }

        public class TranslationLineInput
        {
            public int Index { get; set; }
            public required string Text { get; set; }
        }

        public class EstimateRequest
        {
            public int CharacterCount { get; set; }
        }
    }
}
