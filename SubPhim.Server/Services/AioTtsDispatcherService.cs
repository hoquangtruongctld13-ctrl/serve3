using Google.Cloud.TextToSpeech.V1;
using System.Collections.Concurrent;
using SubPhim.Server.Data; // Thêm dòng này nếu chưa có

namespace SubPhim.Server.Services
{
    // Helper class để giới hạn tốc độ, tương đương RateLimiter của Python
    public class AioTtsRateLimiter
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly double _rate; // tokens per second
        private readonly double _capacity;
        private double _tokens;
        private DateTime _lastRefill;

        public AioTtsRateLimiter(int rpm)
        {
            _rate = rpm / 60.0;
            _capacity = rpm;
            _tokens = _capacity;
            _lastRefill = DateTime.UtcNow;
        }

        public async Task AcquireAsync(int n = 1)
        {
            await _semaphore.WaitAsync();
            try
            {
                while (_tokens < n)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = (now - _lastRefill).TotalSeconds;
                    _tokens = System.Math.Min(_capacity, _tokens + elapsed * _rate);
                    _lastRefill = now;

                    if (_tokens < n)
                    {
                        var delayNeeded = (n - _tokens) / _rate;
                        await Task.Delay(TimeSpan.FromSeconds(delayNeeded));
                    }
                }
                _tokens -= n;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    public class AioTtsDispatcherResult
    {
        public bool IsSuccess { get; set; }
        public byte[]? AudioContent { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class AioTtsDispatcherService
    {
        private readonly AioTtsSaStore _saStore;
        private readonly ILogger<AioTtsDispatcherService> _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly ConcurrentDictionary<string, List<AioTtsServiceAccount>> _saByProject = new();
        private readonly ConcurrentDictionary<string, AioTtsRateLimiter> _projectLimiters = new();
        private readonly ConcurrentDictionary<string, int> _projectRrIndex = new(); // Round-robin index

        private readonly SemaphoreSlim _globalConcurrencyLimiter = new SemaphoreSlim(50, 50);

        public AioTtsDispatcherService(AioTtsSaStore saStore, ILogger<AioTtsDispatcherService> logger, IEncryptionService encryptionService)
        {
            _saStore = saStore;
            _logger = logger;
            _encryptionService = encryptionService;
            InitializeRouter();
        }

        public void InitializeRouter()
        {
            var accounts = _saStore.GetAvailableAccounts();
            _saByProject.Clear();
            _projectLimiters.Clear();
            _projectRrIndex.Clear();

            foreach (var acc in accounts)
            {
                if (!_saByProject.ContainsKey(acc.ProjectId))
                {
                    _saByProject[acc.ProjectId] = new List<AioTtsServiceAccount>();
                    _projectLimiters[acc.ProjectId] = new AioTtsRateLimiter(200);
                    _projectRrIndex[acc.ProjectId] = 0;
                }
                _saByProject[acc.ProjectId].Add(acc);
            }
            _logger.LogInformation("AIOLauncher TTS Dispatcher: Đã khởi tạo router với {ProjectCount} projects.", _saByProject.Count);
        }

        // Helper method để tạo tên voice dựa trên model type
        private string BuildVoiceName(string language, GoogleTtsModelType modelType, string voiceId)
        {
            return modelType switch
            {
                GoogleTtsModelType.Standard => $"{language}-Standard-{voiceId}",
                GoogleTtsModelType.WaveNet => $"{language}-Wavenet-{voiceId}",
                GoogleTtsModelType.Neural2 => $"{language}-Neural2-{voiceId}",
                GoogleTtsModelType.Chirp3HD => $"{language}-Chirp3-HD-{voiceId}",
                GoogleTtsModelType.ChirpHD => $"{language}-Chirp-HD-{voiceId}",
                GoogleTtsModelType.Studio => $"{language}-Studio-{voiceId}",
                GoogleTtsModelType.Polyglot => $"{language}-Polyglot-{voiceId}",
                GoogleTtsModelType.News => $"{language}-News-{voiceId}",
                GoogleTtsModelType.Casual => $"{language}-Casual-{voiceId}",
                _ => $"{language}-Chirp3-HD-{voiceId}" // Default to Chirp3HD
            };
        }

        // SỬA LẠI HÀM NÀY - Thêm modelType parameter
        private AioTtsServiceAccount? PickServiceAccount(GoogleTtsModelType modelType, int neededChars)
        {
            // Lấy danh sách SA cho model type này
            var accountsForModel = _saStore.GetAvailableAccountsByModelType(modelType);
            if (!accountsForModel.Any())
            {
                _logger.LogWarning("Không tìm thấy SA nào cho model type {ModelType}", modelType);
                return null;
            }

            // Nhóm theo project
            var projectGroups = accountsForModel.GroupBy(a => a.ProjectId).ToList();
            var shuffledProjects = projectGroups.OrderBy(x => System.Guid.NewGuid()).ToList();

            foreach (var projectGroup in shuffledProjects)
            {
                var projectId = projectGroup.Key;
                var accountList = projectGroup.ToList();

                for (int i = 0; i < accountList.Count; i++)
                {
                    var sa = accountList[i];

                    // THAY ĐỔI CỐT LÕI: Dùng TryReserveQuota với modelType
                    if (_saStore.TryReserveQuota(sa.ClientEmail, modelType, neededChars))
                    {
                        return sa;
                    }
                }
            }
            return null;
        }
        public async Task<AioTtsDispatcherResult> SynthesizeAsync(GoogleTtsModelType modelType, string language, string voiceId, double rate, string text)
        {
            await _globalConcurrencyLimiter.WaitAsync();

            var neededChars = text?.Length ?? 0;
            if (neededChars <= 0)
            {
                _globalConcurrencyLimiter.Release();
                return new AioTtsDispatcherResult { IsSuccess = false, ErrorMessage = "Nội dung trống." };
            }

            AioTtsServiceAccount? sa = null;

            try
            {
                // 1) Chọn SA và giữ quota cho toàn bộ văn bản theo model type
                sa = PickServiceAccount(modelType, neededChars);
                if (sa == null)
                {
                    return new AioTtsDispatcherResult { IsSuccess = false, ErrorMessage = $"Server đang bận hoặc đã hết quota cho model {modelType}. Vui lòng thử lại sau." };
                }

                // 2) Tạo client một lần
                var jsonKey = _encryptionService.Decrypt(sa.EncryptedJsonKey, sa.Iv);
                var client = new Google.Cloud.TextToSpeech.V1.TextToSpeechClientBuilder
                {
                    JsonCredentials = jsonKey
                }.Build();

                // 3) Chia câu và gom batch văn bản THƯỜNG (không SSML)
                const int MaxRequestBytes = 4900;   // an toàn < 5000 byte
                const int MaxSentenceBytes = 1200;  // an toàn cho từng câu
                var sentences = SplitIntoSentencesSafe(text, MaxSentenceBytes);
                var textBatches = GroupSentencesToTextBatches(sentences, MaxRequestBytes);

                // 4) Gửi SONG SONG các batch, tôn trọng 200 RPM theo project
                var limiter = _projectLimiters[sa.ProjectId];
                int maxParallelBatches = Math.Min(8, textBatches.Count);
                var parallelGate = new SemaphoreSlim(maxParallelBatches, maxParallelBatches);

                var audioParts = new byte[textBatches.Count][];
                Exception? firstError = null;

                // Kiểm tra model có hỗ trợ SSML, speaking rate, pitch không
                bool supportsSpeakingRate = modelType != GoogleTtsModelType.Chirp3HD && modelType != GoogleTtsModelType.ChirpHD;

                var tasks = textBatches.Select((payload, index) => Task.Run(async () =>
                {
                    await parallelGate.WaitAsync();
                    try
                    {
                        await limiter.AcquireAsync(1); // tôn trọng RPM

                        var voiceName = BuildVoiceName(language, modelType, voiceId);
                        var audioConfig = new Google.Cloud.TextToSpeech.V1.AudioConfig
                        {
                            AudioEncoding = Google.Cloud.TextToSpeech.V1.AudioEncoding.Mp3
                        };

                        // Chỉ thêm SpeakingRate nếu model hỗ trợ
                        if (supportsSpeakingRate)
                        {
                            audioConfig.SpeakingRate = rate;
                        }

                        var res = await client.SynthesizeSpeechAsync(new Google.Cloud.TextToSpeech.V1.SynthesizeSpeechRequest
                        {
                            Input = new Google.Cloud.TextToSpeech.V1.SynthesisInput { Text = payload }, // KHÔNG SSML
                            Voice = new Google.Cloud.TextToSpeech.V1.VoiceSelectionParams
                            {
                                LanguageCode = language,
                                Name = voiceName
                            },
                            AudioConfig = audioConfig
                        });

                        audioParts[index] = res.AudioContent.ToByteArray();
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.CompareExchange(ref firstError, ex, null);
                    }
                    finally
                    {
                        parallelGate.Release();
                    }
                })).ToList();

                await Task.WhenAll(tasks);

                if (firstError != null)
                {
                    _logger.LogError(firstError, "TTS: Lỗi khi xử lý batch cho SA {Email}.", sa.ClientEmail);
                    _saStore.ReleaseQuota(sa.ClientEmail, neededChars);
                    return new AioTtsDispatcherResult { IsSuccess = false, ErrorMessage = "Lỗi khi giao tiếp với dịch vụ TTS nền." };
                }

                // 5) Ghép MP3 đúng chuẩn: giữ ID3v2 đầu, bỏ ID3v2 ở các chunk sau, bỏ ID3v1 ở giữa
                var merged = ConcatenateMp3Chunks(audioParts);

                _logger.LogInformation("TTS: Project {ProjectId}, Model {ModelType} đã xử lý {CharCount} ký tự qua {BatchCount} batch (song song, plain text).",
                    sa.ProjectId, modelType, neededChars, textBatches.Count);

                return new AioTtsDispatcherResult
                {
                    IsSuccess = true,
                    AudioContent = merged
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS: Lỗi không xác định cho model {ModelType}.", modelType);
                if (sa != null)
                {
                    _saStore.ReleaseQuota(sa.ClientEmail, neededChars);
                }
                return new AioTtsDispatcherResult { IsSuccess = false, ErrorMessage = $"Lỗi xử lý TTS cho model {modelType}." };
            }
            finally
            {
                _globalConcurrencyLimiter.Release();
            }
        }

        // ====== Helpers: chia câu, gom batch, ghép MP3 ======

        private static List<string> SplitIntoSentencesSafe(string input, int maxSentenceBytes)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(input ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrEmpty(normalized)) return new List<string>();

            var rough = System.Text.RegularExpressions.Regex.Split(
                normalized,
                @"(?<=[\.\!\?…。！？])\s+"
            ).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var sentences = new List<string>();
            foreach (var s in rough)
            {
                if (ByteCountUtf8(s) <= maxSentenceBytes)
                {
                    sentences.Add(EnsureSentenceEndsWithPunctuation(s));
                }
                else
                {
                    foreach (var piece in SplitLongSentenceByDelimiters(s, maxSentenceBytes))
                    {
                        var p = piece?.Trim();
                        if (!string.IsNullOrEmpty(p))
                            sentences.Add(EnsureSentenceEndsWithPunctuation(p));
                    }
                }
            }
            return sentences;
        }

        private static IEnumerable<string> SplitLongSentenceByDelimiters(string s, int maxBytes)
        {
            var firstPass = System.Text.RegularExpressions.Regex.Split(s, @"(?<=[,;:])\s+")
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            foreach (var part in firstPass)
            {
                if (ByteCountUtf8(part) <= maxBytes)
                {
                    yield return part;
                    continue;
                }

                var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var current = new System.Text.StringBuilder();
                foreach (var w in words)
                {
                    var candidate = current.Length == 0 ? w : current.ToString() + " " + w;
                    if (ByteCountUtf8(candidate) <= maxBytes)
                    {
                        current.Clear().Append(candidate);
                    }
                    else
                    {
                        if (current.Length > 0) yield return current.ToString();

                        if (ByteCountUtf8(w) > maxBytes)
                        {
                            foreach (var hard in HardCutByBytes(w, maxBytes))
                                yield return hard;
                            current.Clear();
                        }
                        else
                        {
                            current.Clear().Append(w);
                        }
                    }
                }
                if (current.Length > 0) yield return current.ToString();
            }
        }

        private static IEnumerable<string> HardCutByBytes(string s, int maxBytes)
        {
            var chunk = new System.Text.StringBuilder();
            foreach (var ch in s)
            {
                var candidate = chunk.ToString() + ch;
                if (ByteCountUtf8(candidate) <= maxBytes)
                {
                    chunk.Append(ch);
                }
                else
                {
                    if (chunk.Length > 0) yield return chunk.ToString();
                    chunk.Clear().Append(ch);
                }
            }
            if (chunk.Length > 0) yield return chunk.ToString();
        }

        private static List<string> GroupSentencesToTextBatches(List<string> sentences, int maxRequestBytes)
        {
            var batches = new List<string>();
            var current = new List<string>();

            foreach (var sent in sentences)
            {
                var candidate = BuildPlainText(current.Append(sent).ToList());
                if (ByteCountUtf8(candidate) <= maxRequestBytes)
                {
                    current.Add(sent);
                }
                else
                {
                    if (current.Count > 0)
                    {
                        batches.Add(BuildPlainText(current));
                        current.Clear();
                    }
                    current.Add(sent);
                }
            }

            if (current.Count > 0)
            {
                batches.Add(BuildPlainText(current));
            }
            return batches;
        }

        private static string BuildPlainText(List<string> sentences)
        {
            return string.Join(" ", sentences);
        }

        private static string EnsureSentenceEndsWithPunctuation(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            char last = s[s.Length - 1];
            if (!".!??！？。…".Contains(last))
            {
                return s + ".";
            }
            return s;
        }

        private static int ByteCountUtf8(string s) => System.Text.Encoding.UTF8.GetByteCount(s ?? string.Empty);

        // ====== MP3 concatenate đúng chuẩn ======

        private static byte[] ConcatenateMp3Chunks(IReadOnlyList<byte[]> parts)
        {
            if (parts == null || parts.Count == 0) return Array.Empty<byte>();

            using var ms = new MemoryStream(capacity: parts.Sum(p => p?.Length ?? 0));
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i] ?? Array.Empty<byte>();
                int start = 0;
                int end = part.Length;

                // Bỏ ID3v2 cho các chunk sau chunk đầu
                if (i > 0 && HasId3v2(part))
                {
                    int id3v2Size = GetId3v2Size(part); // đã bao gồm header 10 bytes
                    start = Math.Min(id3v2Size, part.Length);
                }

                // Bỏ ID3v1 ở giữa
                if (i < parts.Count - 1 && HasId3v1(part))
                {
                    end = Math.Max(start, end - 128);
                }

                // Ghi phần còn lại
                int len = end - start;
                if (len > 0)
                {
                    ms.Write(part, start, len);
                }
            }
            return ms.ToArray();
        }

        private static bool HasId3v2(byte[] data)
        {
            if (data.Length < 10) return false;
            return data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3';
        }

        private static int GetId3v2Size(byte[] data)
        {
            // Kích thước synchsafe của ID3v2: 4 byte ở vị trí 6..9, tổng = 10(header) + size
            if (data.Length < 10) return 0;
            int size = (data[6] & 0x7F) << 21 |
                       (data[7] & 0x7F) << 14 |
                       (data[8] & 0x7F) << 7 |
                       (data[9] & 0x7F);
            return 10 + size;
        }

        private static bool HasId3v1(byte[] data)
        {
            if (data.Length < 128) return false;
            int start = data.Length - 128;
            return data[start] == (byte)'T' && data[start + 1] == (byte)'A' && data[start + 2] == (byte)'G';
        }

    }
}