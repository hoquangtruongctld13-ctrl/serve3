using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    public class AioTtsBatchProcessorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AioTtsBatchProcessorService> _logger;
        private static bool _isProcessing = false; // Cờ để đảm bảo chỉ 1 job chạy tại 1 thời điểm

        public AioTtsBatchProcessorService(IServiceProvider serviceProvider, ILogger<AioTtsBatchProcessorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AIOLauncher TTS Batch Processor Service is starting.");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Chờ khởi động

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isProcessing)
                {
                    await ProcessNextJobAsync(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
        {
            _isProcessing = true;
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AioTtsDispatcherService>();

            var pendingJob = await context.AioTtsBatchJobs.AsNoTracking()
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync(j => j.Status == AioTtsJobStatus.Pending, stoppingToken);

            if (pendingJob == null)
            {
                _isProcessing = false;
                return;
            }

            var job = await context.AioTtsBatchJobs.Include(j => j.User).FirstOrDefaultAsync(j => j.Id == pendingJob.Id, stoppingToken);
            if (job == null || job.Status != AioTtsJobStatus.Pending)
            {
                _isProcessing = false;
                return;
            }

            _logger.LogInformation("Found new TTS batch job {JobId} for user {UserId}. Starting processing.", job.Id, job.UserId);
            var user = job.User;
            string tempJobDir = Path.Combine(Path.GetTempPath(), "AioTtsJobs", job.Id.ToString());
            // Giữ lại đường dẫn file SRT gốc để dùng trong khối catch nếu cần
            string originalSrtPath = job.OriginalSrtFilePath;

            try
            {
                job.Status = AioTtsJobStatus.Processing;
                await context.SaveChangesAsync(stoppingToken);

                Directory.CreateDirectory(tempJobDir);

                var srtItems = ParseSrtFile(originalSrtPath);
                job.TotalLines = srtItems.Count;

                long totalCharsForJob = srtItems.Sum(item => (long)item.Text.Length);
                long remainingChars = user.TtsCharacterLimit - user.TtsCharactersUsed;
                if (totalCharsForJob > remainingChars)
                {
                    throw new InvalidOperationException($"Không đủ ký tự TTS. Yêu cầu: {totalCharsForJob}, còn lại: {remainingChars}.");
                }

                user.TtsCharactersUsed += totalCharsForJob;
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Job {JobId}: Total {LineCount} lines, {CharCount} characters. User quota updated.", job.Id, job.TotalLines, totalCharsForJob);

                var synthesisTasks = srtItems.Select(item =>
                {
                    var ext = GetExtensionFromFormat(job.AudioFormat);
                    var fileName = $"{item.Index:D4}_{SafeTimestamp(item.Start)}_{SafeTimestamp(item.End)}{ext}";

                    return dispatcher.SynthesizeAsync(job.ModelType, job.Language, job.VoiceId, job.Rate, item.Text)
                                     .ContinueWith(t => new { Result = t.Result, FileName = fileName, Index = item.Index }, stoppingToken);
                }).ToList();

                var synthesisResults = await Task.WhenAll(synthesisTasks);

                foreach (var result in synthesisResults)
                {
                    if (result.Result.IsSuccess)
                    {
                        var filePath = Path.Combine(tempJobDir, result.FileName);
                        await File.WriteAllBytesAsync(filePath, result.Result.AudioContent, stoppingToken);
                        job.ProcessedLines++;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to synthesize line {Index} for job {JobId}: {Error}", result.Index, job.Id, result.Result.ErrorMessage);
                    }
                }

                await context.SaveChangesAsync(stoppingToken);

                if (job.ProcessedLines == 0 && job.TotalLines > 0)
                {
                    throw new Exception("Không tạo được bất kỳ file audio nào. Vui lòng kiểm tra lại quota hoặc cài đặt.");
                }

                var zipFileName = $"{job.Id}.zip";
                var zipFilePath = Path.Combine(Path.GetTempPath(), "AioTtsJobs", zipFileName);
                ZipFile.CreateFromDirectory(tempJobDir, zipFilePath);

                job.ResultZipFilePath = zipFilePath;
                job.Status = AioTtsJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("Successfully completed TTS batch job {JobId}. Result is at {ZipPath}", job.Id, zipFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process TTS batch job {JobId}.", job.Id);
                job.Status = AioTtsJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;

                // --- SỬA LỖI Ở ĐÂY ---
                // Hoàn lại quota nếu quá trình xử lý gặp lỗi nghiêm trọng
                if (user != null && File.Exists(originalSrtPath)) // Kiểm tra file còn tồn tại không
                {
                    // Gọi phiên bản đồng bộ ParseSrtFile
                    var srtItemsForRefund = ParseSrtFile(originalSrtPath);
                    long totalCharsForJob = srtItemsForRefund.Sum(i => (long)i.Text.Length);

                    if (totalCharsForJob > 0)
                    {
                        user.TtsCharactersUsed -= totalCharsForJob;
                        if (user.TtsCharactersUsed < 0) user.TtsCharactersUsed = 0;
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempJobDir)) Directory.Delete(tempJobDir, true);
                if (File.Exists(originalSrtPath)) File.Delete(originalSrtPath); // Dùng biến đã lưu

                await context.SaveChangesAsync(stoppingToken);
                _isProcessing = false;
            }
        }

        private List<(int Index, string Start, string End, string Text)> ParseSrtFile(string filePath)
        {
            var results = new List<(int Index, string Start, string End, string Text)>();
            var fileContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);

            // Tách file SRT thành các khối dựa trên 2 dòng trống
            var blockSeparator = new Regex(@"(\r\n\r\n|\n\n)", RegexOptions.Compiled);
            var blocks = blockSeparator.Split(fileContent).Where(b => !string.IsNullOrWhiteSpace(b));

            // Biểu thức chính quy để trích xuất thời gian
            var timeRegex = new Regex(@"(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})", RegexOptions.Compiled);

            foreach (var block in blocks)
            {
                var lines = block.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                // Một khối SRT hợp lệ phải có ít nhất 3 dòng: index, thời gian, và nội dung
                if (lines.Length < 3) continue;

                // Dòng đầu tiên là chỉ số (index)
                if (int.TryParse(lines[0], out int index))
                {
                    // Dòng thứ hai là thời gian
                    var timeMatch = timeRegex.Match(lines[1]);
                    if (timeMatch.Success)
                    {
                        var startTime = timeMatch.Groups[1].Value;
                        var endTime = timeMatch.Groups[2].Value;

                        // Các dòng còn lại là nội dung
                        var text = string.Join(" ", lines.Skip(2)).Trim();

                        results.Add((index, startTime, endTime, text));
                    }
                }
            }

            return results;
        }
        private string SafeTimestamp(string timestamp)
        {
            return timestamp.Replace(":", "").Replace(",", "");
        }

        private string GetExtensionFromFormat(string format)
        {
            return format.ToUpper() switch
            {
                "MP3" => ".mp3",
                "WAV" => ".wav",
                "OGG_OPUS" => ".ogg",
                _ => ".mp3"
            };
        }
    }
}