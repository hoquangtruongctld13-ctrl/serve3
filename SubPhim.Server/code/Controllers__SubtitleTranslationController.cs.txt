using Microsoft.AspNetCore.Mvc;
using SubPhim.Server.Services;
using System.Text.Json.Serialization;

namespace SubPhim.Server.Controllers
{
    /// <summary>
    /// API Controller cho dịch phụ đề phân tán
    /// Endpoint: /api/subtitle
    /// </summary>
    [ApiController]
    [Route("api/subtitle")]
    public class SubtitleTranslationController : ControllerBase
    {
        private readonly ISubtitleOrchestratorService _orchestratorService;
        private readonly ILogger<SubtitleTranslationController> _logger;

        public SubtitleTranslationController(
            ISubtitleOrchestratorService orchestratorService,
            ILogger<SubtitleTranslationController> logger)
        {
            _orchestratorService = orchestratorService;
            _logger = logger;
        }

        #region Request/Response Models
        /// <summary>
        /// Request để bắt đầu dịch phụ đề
        /// </summary>
        public class TranslateRequest
        {
            /// <summary>
            /// ID phiên dịch (unique, do client tạo)
            /// </summary>
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; }

            /// <summary>
            /// Prompt hướng dẫn dịch
            /// </summary>
            [JsonPropertyName("prompt")]
            public string Prompt { get; set; }

            /// <summary>
            /// System instruction cho AI
            /// </summary>
            [JsonPropertyName("systemInstruction")]
            public string SystemInstruction { get; set; }

            /// <summary>
            /// Danh sách dòng phụ đề cần dịch
            /// </summary>
            [JsonPropertyName("lines")]
            public List<LineInput> Lines { get; set; }

            /// <summary>
            /// Model AI sử dụng (optional, mặc định: gemini-2.5-flash)
            /// </summary>
            [JsonPropertyName("model")]
            public string? Model { get; set; }

            /// <summary>
            /// Thinking budget (optional, 0 = tắt)
            /// </summary>
            [JsonPropertyName("thinkingBudget")]
            public int? ThinkingBudget { get; set; }

            /// <summary>
            /// URL callback khi hoàn thành (optional)
            /// </summary>
            [JsonPropertyName("callbackUrl")]
            public string? CallbackUrl { get; set; }
        }

        public class LineInput
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        public class TranslateResponse
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("totalLines")]
            public int TotalLines { get; set; }

            [JsonPropertyName("batchCount")]
            public int BatchCount { get; set; }

            [JsonPropertyName("serversAssigned")]
            public int ServersAssigned { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }
        }

        public class StatusResponse
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("progress")]
            public double Progress { get; set; }

            [JsonPropertyName("totalLines")]
            public int TotalLines { get; set; }

            [JsonPropertyName("completedLines")]
            public int CompletedLines { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("taskStats")]
            public Dictionary<string, int> TaskStats { get; set; }
        }

        public class ResultsResponse
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("totalLines")]
            public int TotalLines { get; set; }

            [JsonPropertyName("completedLines")]
            public int CompletedLines { get; set; }

            [JsonPropertyName("results")]
            public List<TranslatedLine> Results { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; }

            [JsonPropertyName("completedAt")]
            public DateTime? CompletedAt { get; set; }
        }

        public class TranslatedLine
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("original")]
            public string Original { get; set; }

            [JsonPropertyName("translated")]
            public string Translated { get; set; }
        }

        public class CallbackRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("totalLines")]
            public int TotalLines { get; set; }

            [JsonPropertyName("completedLines")]
            public int CompletedLines { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("apiKeyUsage")]
            public List<ApiKeyUsage>? ApiKeyUsage { get; set; }

            [JsonPropertyName("results")]
            public List<TranslatedLine>? Results { get; set; }
        }

        public class ApiKeyUsage
        {
            [JsonPropertyName("apiKey")]
            public string ApiKey { get; set; }

            [JsonPropertyName("maskedKey")]
            public string MaskedKey { get; set; }

            [JsonPropertyName("requestCount")]
            public int RequestCount { get; set; }

            [JsonPropertyName("successCount")]
            public int SuccessCount { get; set; }

            [JsonPropertyName("failureCount")]
            public int FailureCount { get; set; }
        }

        public class ErrorResponse
        {
            [JsonPropertyName("error")]
            public string Error { get; set; }

            [JsonPropertyName("detail")]
            public string? Detail { get; set; }
        }
        #endregion

        /// <summary>
        /// Submit job dịch phụ đề
        /// </summary>
        /// <remarks>
        /// Gửi danh sách dòng phụ đề để dịch. Server sẽ tự động phân phối đến các server dịch.
        ///
        /// Ví dụ request:
        /// ```json
        /// {
        ///   "sessionId": "job-20231211-143000-abc123",
        ///   "prompt": "Dịch phụ đề sau sang tiếng Việt.\nFormat: index|text dịch",
        ///   "systemInstruction": "Bạn là dịch giả phụ đề phim chuyên nghiệp.",
        ///   "lines": [
        ///     {"index": 1, "text": "Hello world"},
        ///     {"index": 2, "text": "How are you?"}
        ///   ],
        ///   "model": "gemini-2.5-flash",
        ///   "callbackUrl": "https://your-server.com/callback"
        /// }
        /// ```
        /// </remarks>
        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromBody] TranslateRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.SessionId))
                {
                    return BadRequest(new ErrorResponse { Error = "sessionId là bắt buộc" });
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return BadRequest(new ErrorResponse { Error = "prompt là bắt buộc" });
                }

                if (string.IsNullOrWhiteSpace(request.SystemInstruction))
                {
                    return BadRequest(new ErrorResponse { Error = "systemInstruction là bắt buộc" });
                }

                if (request.Lines == null || !request.Lines.Any())
                {
                    return BadRequest(new ErrorResponse { Error = "lines không được để trống" });
                }

                _logger.LogInformation("Received translation request: {SessionId}, {Lines} lines",
                    request.SessionId, request.Lines.Count);

                // Convert to service model
                var serviceRequest = new SubtitleTranslationRequest
                {
                    SessionId = request.SessionId,
                    Prompt = request.Prompt,
                    SystemInstruction = request.SystemInstruction,
                    Lines = request.Lines.Select(l => new SubtitleLine { Index = l.Index, Text = l.Text }).ToList(),
                    Model = request.Model,
                    ThinkingBudget = request.ThinkingBudget,
                    CallbackUrl = request.CallbackUrl
                };

                var result = await _orchestratorService.SubmitJobAsync(serviceRequest);

                return Ok(new TranslateResponse
                {
                    SessionId = result.SessionId,
                    Status = result.Status,
                    TotalLines = result.TotalLines,
                    BatchCount = result.BatchCount,
                    ServersAssigned = result.ServersAssigned,
                    Message = result.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing translation request");
                return StatusCode(500, new ErrorResponse { Error = "Lỗi server", Detail = ex.Message });
            }
        }

        /// <summary>
        /// Lấy trạng thái job dịch
        /// </summary>
        /// <param name="sessionId">ID phiên dịch</param>
        [HttpGet("status/{sessionId}")]
        public async Task<IActionResult> GetStatus(string sessionId)
        {
            try
            {
                var status = await _orchestratorService.GetJobStatusAsync(sessionId);

                return Ok(new StatusResponse
                {
                    SessionId = status.SessionId,
                    Status = status.Status,
                    Progress = status.Progress,
                    TotalLines = status.TotalLines,
                    CompletedLines = status.CompletedLines,
                    Error = status.Error,
                    TaskStats = status.TaskStats
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new ErrorResponse { Error = $"Job {sessionId} không tồn tại" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status for {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse { Error = "Lỗi server", Detail = ex.Message });
            }
        }

        /// <summary>
        /// Lấy kết quả dịch đầy đủ
        /// </summary>
        /// <param name="sessionId">ID phiên dịch</param>
        [HttpGet("results/{sessionId}")]
        public async Task<IActionResult> GetResults(string sessionId)
        {
            try
            {
                var results = await _orchestratorService.GetJobResultsAsync(sessionId);

                return Ok(new ResultsResponse
                {
                    SessionId = results.SessionId,
                    Status = results.Status,
                    TotalLines = results.TotalLines,
                    CompletedLines = results.CompletedLines,
                    Results = results.Results.Select(r => new TranslatedLine
                    {
                        Index = r.Index,
                        Original = r.Original,
                        Translated = r.Translated
                    }).ToList(),
                    Error = results.Error,
                    CreatedAt = results.CreatedAt,
                    CompletedAt = results.CompletedAt
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new ErrorResponse { Error = $"Job {sessionId} không tồn tại" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting results for {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse { Error = "Lỗi server", Detail = ex.Message });
            }
        }

        /// <summary>
        /// Callback endpoint để server dịch gửi kết quả về
        /// </summary>
        [HttpPost("callback/{sessionId}/{batchIndex}")]
        public async Task<IActionResult> Callback(string sessionId, int batchIndex, [FromBody] CallbackRequest request)
        {
            try
            {
                _logger.LogInformation("Received callback for {SessionId} batch {BatchIndex}", sessionId, batchIndex);

                var callbackData = new ServerCallbackData
                {
                    SessionId = request.SessionId,
                    Status = request.Status,
                    TotalLines = request.TotalLines,
                    CompletedLines = request.CompletedLines,
                    Error = request.Error,
                    ApiKeyUsage = request.ApiKeyUsage?.Select(u => new ApiKeyUsageInfo
                    {
                        ApiKey = u.ApiKey,
                        MaskedKey = u.MaskedKey,
                        RequestCount = u.RequestCount,
                        SuccessCount = u.SuccessCount,
                        FailureCount = u.FailureCount
                    }).ToList(),
                    Results = request.Results?.Select(r => new TranslatedLineResult
                    {
                        Index = r.Index,
                        Original = r.Original,
                        Translated = r.Translated
                    }).ToList()
                };

                await _orchestratorService.ProcessCallbackAsync(callbackData);

                return Ok(new { received = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing callback for {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse { Error = ex.Message });
            }
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                service = "Subtitle Translation API (Distributed)",
                status = "running",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
