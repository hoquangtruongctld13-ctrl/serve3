using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.Security.Claims;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/aiolauncher-tts")]
    [Authorize]
    public class AioLauncherTtsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AioTtsDispatcherService _dispatcher;
        private readonly ILogger<AioLauncherTtsController> _logger;
        private readonly IWebHostEnvironment _env;

        public AioLauncherTtsController(AppDbContext context, AioTtsDispatcherService dispatcher, ILogger<AioLauncherTtsController> logger, IWebHostEnvironment env)
        {
            _context = context;
            _dispatcher = dispatcher;
            _logger = logger;
            _env = env;
        }

        public record TtsApiRequest(
            string Language,
            string VoiceId,
            double Rate,
            string Text,
            GoogleTtsModelType ModelType = GoogleTtsModelType.Chirp3HD // Mặc định Chirp3HD để tương thích ngược
        );

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] TtsApiRequest request)
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId))
            {
                return Unauthorized();
            }

            var characterCount = request.Text?.Length ?? 0;
            if (characterCount == 0)
            {
                return BadRequest(new { message = "Nội dung văn bản không được để trống." });
            }

            // BỎ GIỚI HẠN 4900 BYTE Ở ĐÂY ĐỂ SERVER TỰ CHIA BATCH TRONG Dispatcher
            // const int MaxByteLimit = 4900;
            // var byteCount = System.Text.Encoding.UTF8.GetByteCount(request.Text);
            // if (byteCount > MaxByteLimit)
            // {
            //     return StatusCode(413, new { message = $"Nội dung văn bản quá lớn ({byteCount} bytes). Vui lòng chia nhỏ yêu cầu dưới {MaxByteLimit} bytes." });
            // }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "Tài khoản không tồn tại." });
            }

            long remainingChars = user.TtsCharacterLimit - user.TtsCharactersUsed;
            if (characterCount > remainingChars)
            {
                return StatusCode(429, new { message = $"Không đủ ký tự TTS. Yêu cầu: {characterCount}, còn lại: {remainingChars}." });
            }

            // Trừ quota theo tổng ký tự đầu vào. Dispatcher sẽ reserve lại theo SA tương ứng.
            user.TtsCharactersUsed += characterCount;
            await _context.SaveChangesAsync();

            var result = await _dispatcher.SynthesizeAsync(request.ModelType, request.Language, request.VoiceId, request.Rate, request.Text);

            if (result.IsSuccess)
            {
                return File(result.AudioContent, "audio/mpeg");
            }
            else
            {
                // Hoàn lại quota cho user nếu thất bại
                user.TtsCharactersUsed -= characterCount;
                if (user.TtsCharactersUsed < 0) user.TtsCharactersUsed = 0;
                await _context.SaveChangesAsync();

                _logger.LogWarning("Tạo AIOLauncher TTS cho user {UserId} thất bại: {Error}", userId, result.ErrorMessage);
                return StatusCode(503, new { message = result.ErrorMessage });
            }
        }

        [HttpPost("batch/upload"), DisableRequestSizeLimit] // Cho phép upload file lớn
        public async Task<IActionResult> UploadSrtBatch([FromForm] IFormFile srtFile, [FromForm] string language, [FromForm] string voiceId, [FromForm] double rate, [FromForm] string audioFormat, [FromForm] GoogleTtsModelType modelType = GoogleTtsModelType.Chirp3HD)
        {
            const long maxFileSize = 50 * 1024 * 1024;

            if (srtFile == null || srtFile.Length == 0)
                return BadRequest(new { message = "Chưa cung cấp file SRT." });
            if (srtFile.Length > maxFileSize)
            {
                return StatusCode(413, new { message = $"Kích thước file quá lớn. Vui lòng upload file SRT dưới {maxFileSize / 1024 / 1024}MB." });
            }

            if (!int.TryParse(User.FindFirstValue("id"), out int userId)) return Unauthorized();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound(new { message = "Tài khoản không tồn tại." });

            var tempUploadDir = Path.Combine(_env.ContentRootPath, "TempUploads");
            Directory.CreateDirectory(tempUploadDir);
            var originalFilePath = Path.Combine(tempUploadDir, $"{Guid.NewGuid()}_{srtFile.FileName}");

            await using (var stream = new FileStream(originalFilePath, FileMode.Create))
            {
                await srtFile.CopyToAsync(stream);
            }

            var newJob = new AioTtsBatchJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = AioTtsJobStatus.Pending,
                Language = language,
                VoiceId = voiceId,
                Rate = rate,
                AudioFormat = audioFormat,
                ModelType = modelType,
                OriginalSrtFilePath = originalFilePath
            };

            _context.AioTtsBatchJobs.Add(newJob);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã tạo TTS Batch Job {JobId} cho User {UserId}.", newJob.Id, userId);

            return Ok(new { jobId = newJob.Id });
        }

        [HttpGet("batch/status/{jobId}")]
        public async Task<IActionResult> GetBatchStatus(Guid jobId)
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId)) return Unauthorized();

            var job = await _context.AioTtsBatchJobs.AsNoTracking()
                                    .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

            if (job == null) return NotFound(new { message = "Không tìm thấy tác vụ hoặc bạn không có quyền truy cập." });

            return Ok(new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                totalLines = job.TotalLines,
                processedLines = job.ProcessedLines,
                errorMessage = job.ErrorMessage,
                createdAt = job.CreatedAt,
                completedAt = job.CompletedAt
            });
        }

        [HttpGet("list-voices")]
        public async Task<IActionResult> ListVoices([FromQuery] string? languageCode = null, [FromQuery] GoogleTtsModelType? modelType = null)
        {
            try
            {
                // Lấy một SA bất kỳ để gọi API (không tốn quota khi list voices)
                var allSas = await _context.AioTtsServiceAccounts
                    .Where(sa => sa.IsEnabled)
                    .ToListAsync();

                if (!allSas.Any())
                {
                    return StatusCode(503, new { message = "Không có Service Account nào khả dụng." });
                }

                var sa = allSas.First();

                // Giải mã key
                var encryptionService = HttpContext.RequestServices.GetRequiredService<IEncryptionService>();
                var decryptedKey = encryptionService.Decrypt(sa.EncryptedJsonKey, sa.Iv);

                var client = new Google.Cloud.TextToSpeech.V1.TextToSpeechClientBuilder
                {
                    JsonCredentials = decryptedKey
                }.Build();

                // Gọi ListVoices API
                var request = new Google.Cloud.TextToSpeech.V1.ListVoicesRequest();
                if (!string.IsNullOrEmpty(languageCode))
                {
                    request.LanguageCode = languageCode;
                }

                var response = await client.ListVoicesAsync(request);

                // Filter theo model type nếu được chỉ định
                var voices = response.Voices.AsEnumerable();
                if (modelType.HasValue)
                {
                    var modelIdentifier = GetModelIdentifierFromType(modelType.Value);
                    voices = voices.Where(v => v.Name.Contains($"-{modelIdentifier}-", StringComparison.OrdinalIgnoreCase));
                }

                // Format response
                var result = voices.Select(v => new
                {
                    name = v.Name,
                    languageCodes = v.LanguageCodes.ToArray(),
                    ssmlGender = v.SsmlGender.ToString(),
                    naturalSampleRateHertz = v.NaturalSampleRateHertz,
                    // Phân tích model type từ tên voice
                    modelType = DetectModelTypeFromVoiceName(v.Name),
                    // Trích xuất voice ID (ký tự cuối hoặc tên)
                    voiceId = ExtractVoiceId(v.Name)
                }).ToList();

                return Ok(new
                {
                    voices = result,
                    totalCount = result.Count,
                    filteredBy = new
                    {
                        languageCode = languageCode ?? "all",
                        modelType = modelType?.ToString() ?? "all"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi list voices từ Google TTS API");
                return StatusCode(500, new { message = "Lỗi khi lấy danh sách voices: " + ex.Message });
            }
        }

        private string GetModelIdentifierFromType(GoogleTtsModelType modelType)
        {
            return modelType switch
            {
                GoogleTtsModelType.Standard => "Standard",
                GoogleTtsModelType.WaveNet => "Wavenet",
                GoogleTtsModelType.Neural2 => "Neural2",
                GoogleTtsModelType.Chirp3HD => "Chirp3-HD",
                GoogleTtsModelType.ChirpHD => "Chirp-HD",
                GoogleTtsModelType.Studio => "Studio",
                GoogleTtsModelType.Polyglot => "Polyglot",
                GoogleTtsModelType.News => "News",
                GoogleTtsModelType.Casual => "Casual",
                _ => "Standard"
            };
        }

        private string DetectModelTypeFromVoiceName(string voiceName)
        {
            if (voiceName.Contains("Chirp3-HD", StringComparison.OrdinalIgnoreCase))
                return "Chirp3HD";
            if (voiceName.Contains("Chirp-HD", StringComparison.OrdinalIgnoreCase))
                return "ChirpHD";
            if (voiceName.Contains("Neural2", StringComparison.OrdinalIgnoreCase))
                return "Neural2";
            if (voiceName.Contains("Wavenet", StringComparison.OrdinalIgnoreCase))
                return "WaveNet";
            if (voiceName.Contains("Studio", StringComparison.OrdinalIgnoreCase))
                return "Studio";
            if (voiceName.Contains("Polyglot", StringComparison.OrdinalIgnoreCase))
                return "Polyglot";
            if (voiceName.Contains("News", StringComparison.OrdinalIgnoreCase))
                return "News";
            if (voiceName.Contains("Casual", StringComparison.OrdinalIgnoreCase))
                return "Casual";
            if (voiceName.Contains("Standard", StringComparison.OrdinalIgnoreCase))
                return "Standard";
            return "Unknown";
        }

        private string ExtractVoiceId(string voiceName)
        {
            // Ví dụ: en-US-Standard-A -> A
            // en-US-Chirp3-HD-Achernar -> Achernar
            var parts = voiceName.Split('-');
            return parts.Length > 0 ? parts[^1] : voiceName;
        }

        [HttpGet("batch/download/{jobId}")]
        public async Task<IActionResult> DownloadBatchResult(Guid jobId)
        {
            if (!int.TryParse(User.FindFirstValue("id"), out int userId)) return Unauthorized();

            var job = await _context.AioTtsBatchJobs.AsNoTracking()
                                .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

            if (job == null) return NotFound(new { message = "Không tìm thấy tác vụ hoặc bạn không có quyền truy cập." });

            if (job.Status != AioTtsJobStatus.Completed || string.IsNullOrEmpty(job.ResultZipFilePath) || !System.IO.File.Exists(job.ResultZipFilePath))
            {
                return BadRequest(new { message = "Tác vụ chưa hoàn thành hoặc file kết quả không tồn tại." });
            }

            var filePath = job.ResultZipFilePath;
            var fileName = $"AioTtsResult_{job.Id}.zip";
            var fileInfo = new FileInfo(filePath);

            // Cho phép resume và stream trực tiếp từ đĩa, tránh dồn RAM
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            Response.Headers["Content-Length"] = fileInfo.Length.ToString();

            // Đăng ký xóa file sau khi response hoàn tất (thành công hoặc không)
            HttpContext.Response.OnCompleted(() =>
            {
                try { System.IO.File.Delete(filePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Không thể xóa file zip tạm thời: {FilePath}", filePath); }
                return Task.CompletedTask;
            });

            return PhysicalFile(filePath, "application/zip", enableRangeProcessing: true);
        }

    }
}