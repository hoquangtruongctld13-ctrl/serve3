using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using subphimv1.Subphim;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace subphimv1.Services
{
    public class SrtTranslationService
    {
        public class SrtApiConfig
        {
            public string ChutesApiKey { get; set; }
            public string ChutesModel { get; set; }
            public string OpenRouterHttpReferer { get; set; } 
            public string OpenRouterXTitle { get; set; }
            public List<string> GeminiApiKeys { get; set; }
            public string GeminiModel { get; set; }
            public bool UseGeminiMultiKey { get; set; }
            public int GeminiRpm { get; set; }
            public int GeminiBatchSize { get; set; }
            public int GeminiThinkingBudget { get; set; }
            public string ChatGPTModel { get; set; }
            public int ChatGPTBatchSize { get; set; }

        }

        private readonly SrtApiConfig _config;
        public Action<string, bool> LogMessage { get; set; }

        private const string CHUTES_API_URL_SRT = "https://openrouter.ai/api/v1/chat/completions";
        private const string OPENAI_API_URL_SRT = "https://api.openai.com/v1/chat/completions";
        private const string GEMINI_API_URL_BASE_SRT = "https://generativelanguage.googleapis.com/v1beta/models/";
        private const int MAX_API_RETRIES_SRT = 3;
        private const int API_RETRY_BASE_DELAY_MS_SRT = 2000;

        private readonly SemaphoreSlim _geminiSrtRpmSemaphore;
        private readonly Timer _geminiSrtRpmResetTimer;
        private int _currentGeminiSrtApiKeyIndex = 0;
        private readonly object _geminiSrtApiKeyLock = new object();

        public SrtTranslationService(SrtApiConfig config)
        {
            _config = config;
            if (_config.GeminiApiKeys != null && _config.GeminiApiKeys.Any())
            {
                int totalRpm = _config.UseGeminiMultiKey ? _config.GeminiApiKeys.Count * _config.GeminiRpm : _config.GeminiRpm;
                totalRpm = Math.Max(1, Math.Min(60, totalRpm));
                _geminiSrtRpmSemaphore = new SemaphoreSlim(totalRpm, totalRpm);
                _geminiSrtRpmResetTimer = new Timer(state =>
                {
                    var semaphore = state as SemaphoreSlim;
                    if (semaphore == null || semaphore.CurrentCount >= totalRpm) return;
                    int toRelease = totalRpm - semaphore.CurrentCount;
                    if (toRelease > 0) semaphore.Release(toRelease);
                }, _geminiSrtRpmSemaphore, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            }
        }

        private void InternalLog(string message, bool isError = false) => LogMessage?.Invoke($"[SRT_TRANS_SVC] {message}", isError);


        public async Task<ConcurrentDictionary<int, (string text, bool success, string error)>> TranslateAllSrtLinesAsync(
            List<SrtSubtitleLine> linesToTranslate,
            SrtApiProvider provider,
            string genre,
            string targetLanguage,
            Action<double> onProgress,
            CancellationToken token)
        {
            var results = new ConcurrentDictionary<int, (string text, bool success, string error)>();
            if (!linesToTranslate.Any())
            {
                onProgress?.Invoke(100);
                return results;
            }
            int batchSize;
            switch (provider)
            {
                case SrtApiProvider.Gemini:
                    batchSize = Math.Max(1, _config.GeminiBatchSize);
                    break;
                case SrtApiProvider.ChatGPT:
                    batchSize = Math.Max(1, _config.ChatGPTBatchSize);
                    break;
                case SrtApiProvider.ChutesAI:
                default:
                    batchSize = 30;
                    break;
            }

            var batches = linesToTranslate.Select((line, index) => new { line, index })
                                          .GroupBy(x => x.index / batchSize)
                                          .Select(g => g.Select(x => x.line).ToList())
                                          .ToList();
            InternalLog($"Bắt đầu dịch {linesToTranslate.Count} dòng bằng {provider}, chia thành {batches.Count} batch (size: {batchSize}).");

            long linesProcessed = 0;
            int maxConcurrentBatches = provider == SrtApiProvider.Gemini ? Math.Min(15, (_config.GeminiApiKeys?.Count ?? 1) * 2) : 50;
            using var limiter = new SemaphoreSlim(maxConcurrentBatches);

            var tasks = new List<Task>();
            try
            {
                foreach (var batch in batches)
                {
                    if (token.IsCancellationRequested) break;
                    await limiter.WaitAsync(token);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var payloadBuilder = new StringBuilder();
                            foreach (var line in batch) payloadBuilder.AppendLine($"{line.Index}: {line.OriginalText.Replace("\r\n", " ").Replace("\n", " ")}");
                            string payload = payloadBuilder.ToString().TrimEnd();

                            string responseText = null;
                            if (!string.IsNullOrWhiteSpace(payload))
                            {
                                switch (provider)
                                {
                                    case SrtApiProvider.ChutesAI:
                                        responseText = await TranslateWithChutesAIAsync(payload, token, genre, targetLanguage);
                                        break;
                                    case SrtApiProvider.Gemini:
                                        // Dòng này cũng sẽ không còn báo lỗi "ambiguous"
                                        responseText = await TranslateWithGeminiAsync(payload, token, genre, targetLanguage);
                                        break;
                                    case SrtApiProvider.ChatGPT:
                                        responseText = await TranslateWithChatGPTAsync(payload, token, genre, targetLanguage);
                                        break;
                                    default:
                                        responseText = "Lỗi: Nhà cung cấp API không được hỗ trợ.";
                                        break;
                                }
                            }

                            if (token.IsCancellationRequested) return;

                            if (responseText != null && !responseText.StartsWith("Lỗi", StringComparison.OrdinalIgnoreCase))
                            {
                                var translatedLinesDict = new Dictionary<int, string>();
                                var regex = new Regex(@"^\s*(\d+):\s*(.*)$", RegexOptions.Multiline);
                                foreach (Match m in regex.Matches(responseText))
                                    if (int.TryParse(m.Groups[1].Value, out int idx))
                                        translatedLinesDict[idx] = m.Groups[2].Value.Trim();

                                foreach (var line in batch)
                                    if (translatedLinesDict.TryGetValue(line.Index, out string translated))
                                        results[line.Index] = (string.IsNullOrWhiteSpace(translated) ? "[API DỊCH RỖNG]" : translated, true, null);
                                    else
                                        results[line.Index] = ("[API KHÔNG TRẢ VỀ DÒNG NÀY]", false, "API did not return this line.");
                            }
                            else
                            {
                                foreach (var line in batch) results[line.Index] = (responseText ?? "[LỖI BATCH NGHIÊM TRỌNG]", false, responseText);
                            }
                        }
                        catch (OperationCanceledException) { /* Bỏ qua */ }
                        catch (Exception ex)
                        {
                            InternalLog($"Lỗi nghiêm trọng khi xử lý batch: {ex.Message}", true);
                            foreach (var line in batch) results[line.Index] = ("[LỖI EXCEPTION]", false, ex.Message);
                        }
                        finally
                        {
                            Interlocked.Add(ref linesProcessed, batch.Count);
                            onProgress?.Invoke((double)linesProcessed / linesToTranslate.Count * 100);
                            limiter.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                InternalLog("Quá trình dịch đã bị hủy bởi người dùng.");
            }

            return results;
        }
              

        private async Task<string> GetAvailableGeminiSrtApiKeyAsync()
        {
            if (_config.GeminiApiKeys == null || !_config.GeminiApiKeys.Any()) return null;
            if (!_config.UseGeminiMultiKey || _config.GeminiApiKeys.Count == 1) return _config.GeminiApiKeys[0];
            lock (_geminiSrtApiKeyLock)
            {
                string key = _config.GeminiApiKeys[_currentGeminiSrtApiKeyIndex];
                _currentGeminiSrtApiKeyIndex = (_currentGeminiSrtApiKeyIndex + 1) % _config.GeminiApiKeys.Count;
                return key;
            }
        }

        public async Task<string> TranslateWithChutesAIAsync(string inputText, CancellationToken token, string genre, string lang)
        {
            if (string.IsNullOrWhiteSpace(_config.ChutesApiKey)) return "Lỗi API: Thiếu API Key OpenRouter.";

            var requestData = new
            {
                model = _config.ChutesModel,
                messages = new[]
                {
            new { role = "system", content = GetSystemPromptForSrtTranslation(genre, lang) },
            new { role = "user", content = inputText }
        },
                stream = false,
                temperature = 0.7,
                max_tokens = 8000
            };

            // Tạo dictionary cho các header tùy chỉnh
            var extraHeaders = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(_config.OpenRouterHttpReferer))
            {
                extraHeaders["HTTP-Referer"] = _config.OpenRouterHttpReferer;
            }
            if (!string.IsNullOrWhiteSpace(_config.OpenRouterXTitle))
            {
                extraHeaders["X-Title"] = _config.OpenRouterXTitle;
            }

            var (responseText, _) = await CallApiAsync(CHUTES_API_URL_SRT, JsonConvert.SerializeObject(requestData), $"Bearer {_config.ChutesApiKey}", token, extraHeaders);
            return responseText;
        }

        public async Task<string> TranslateWithGeminiAsync(string inputText, CancellationToken token, string genre, string lang)
        {
            string apiKey = await GetAvailableGeminiSrtApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey)) return "Lỗi API: Không có key Gemini khả dụng.";
            if (_geminiSrtRpmSemaphore != null) await _geminiSrtRpmSemaphore.WaitAsync(token);

            var generationConfig = new JObject { ["temperature"] = 1, ["topP"] = 0.95, ["maxOutputTokens"] = 35000 };
            if (_config.GeminiThinkingBudget > 0) generationConfig["thinking_config"] = new JObject { ["thinking_budget"] = _config.GeminiThinkingBudget };
            var requestPayload = new { contents = new[] { new { role = "user", parts = new[] { new { text = $"Dịch các câu thoại sau sang {lang}:\n\n{inputText}" } } } }, system_instruction = new { parts = new[] { new { text = GetSystemInstructionForGeminiSrtTranslation(genre, lang) } } }, generationConfig };

            string apiUrl = $"{GEMINI_API_URL_BASE_SRT}{_config.GeminiModel}:generateContent?key={apiKey}";
            var (responseText, _) = await CallApiAsync(apiUrl, JsonConvert.SerializeObject(requestPayload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), null, token);
            return responseText;
        }
        public async Task<string> TranslateWithChatGPTAsync(string inputText, CancellationToken token, string genre, string lang)
        {
            // Dùng chung API Key với ChutesAI/OpenRouter theo yêu cầu
            if (string.IsNullOrWhiteSpace(_config.ChutesApiKey)) return "Lỗi API: Thiếu API Key cho ChatGPT.";

            var requestData = new
            {
                model = _config.ChatGPTModel, // Sử dụng model riêng của ChatGPT
                messages = new[]
                {
                    // Tái sử dụng prompt giống ChutesAI/OpenRouter
                    new { role = "system", content = GetSystemPromptForSrtTranslation(genre, lang) },
                    new { role = "user", content = inputText }
                },
                stream = false,
                temperature = 0.7,
                max_tokens = 4096
            };

            var (responseText, _) = await CallApiAsync(OPENAI_API_URL_SRT, JsonConvert.SerializeObject(requestData), $"Bearer {_config.ChutesApiKey}", token);
            return responseText;
        }
        private async Task<(string responseText, int tokensUsed)> CallApiAsync(string url, string jsonPayload, string authToken, CancellationToken token, Dictionary<string, string> extraHeaders = null)
        {
            for (int attempt = 1; attempt <= MAX_API_RETRIES_SRT; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json") };
                    if (authToken != null) request.Headers.TryAddWithoutValidation("Authorization", authToken);
                    if (extraHeaders != null)
                    {
                        foreach (var header in extraHeaders)
                        {
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }

                    using HttpResponseMessage response = await httpClient.SendAsync(request, token);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        InternalLog($"Lỗi HTTP {response.StatusCode} từ {url}. Thử lại {attempt}/{MAX_API_RETRIES_SRT}", true);
                        if (attempt < MAX_API_RETRIES_SRT) { await Task.Delay(API_RETRY_BASE_DELAY_MS_SRT * attempt, token); continue; }
                        return ($"Lỗi API: {response.StatusCode}", 0);
                    }

                    JObject parsedBody = JObject.Parse(responseBody);
                    if (parsedBody?["error"] != null) return ($"Lỗi API: {parsedBody["error"]?["message"]}", 0);
                    if (parsedBody?["promptFeedback"]?["blockReason"] != null) return ($"Lỗi: Nội dung bị chặn. Lý do: {parsedBody["promptFeedback"]["blockReason"]}", 0);

                    // *** BẮT ĐẦU LOGIC LẤY TOKEN ***
                    int tokens = 0;
                    // Dành cho API Gemini
                    if (parsedBody?["usageMetadata"]?["totalTokenCount"] != null)
                    {
                        tokens = parsedBody["usageMetadata"]["totalTokenCount"].Value<int>();
                    }
                    // Dành cho API OpenAI/ChutesAI
                    else if (parsedBody?["usage"]?["total_tokens"] != null)
                    {
                        tokens = parsedBody["usage"]["total_tokens"].Value<int>();
                    }
                    // *** KẾT THÚC LOGIC LẤY TOKEN ***

                    string responseText = parsedBody?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() ?? parsedBody?["choices"]?[0]?["message"]?["content"]?.ToString();
                    return (responseText, tokens);
                }
                catch (Exception ex)
                {
                    InternalLog($"Lỗi khi gọi API: {ex.Message}. Thử lại {attempt}/{MAX_API_RETRIES_SRT}", true);
                    if (attempt >= MAX_API_RETRIES_SRT) return ($"Lỗi Exception: {ex.Message}", 0);
                    await Task.Delay(API_RETRY_BASE_DELAY_MS_SRT * attempt, token);
                }
            }
            return ("Lỗi API: Hết số lần thử lại.", 0);
        }
        public async Task<(bool success, string translatedText, string error)> TranslateImageRegionAsync(
            byte[] imageBytes,
            string targetLanguage,
            string genre)
        {
            if (_config.GeminiApiKeys == null || !_config.GeminiApiKeys.Any())
            {
                return (false, null, "Thiếu API Key Gemini cho dịch thuật.");
            }

            string apiKey = await GetAvailableGeminiSrtApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                return (false, null, "Không có API Key Gemini khả dụng.");
            }
            string systemPrompt = GetSystemInstructionForGeminiSrtTranslation(genre, targetLanguage);
            string userPrompt = $" {systemPrompt}";

            try
            {
                if (_geminiSrtRpmSemaphore != null) await _geminiSrtRpmSemaphore.WaitAsync();

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                string apiUrl = $"{GEMINI_API_URL_BASE_SRT}{_config.GeminiModel}:generateContent?key={apiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = userPrompt },
                                new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(imageBytes) } }
                            }
                        }
                    },
                    generation_config = new
                    {
                        temperature = 0.5,
                        max_output_tokens = 4096,
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(requestBody);
                var (responseText, tokensUsed) = await CallApiAsync(apiUrl, jsonPayload, null, CancellationToken.None);

                if (responseText != null && !responseText.StartsWith("Lỗi", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, responseText.Trim(), null);
                }
                else
                {
                    return (false, null, responseText ?? "Lỗi không xác định từ API.");
                }
            }
            catch (Exception ex)
            {
                InternalLog($"Lỗi khi dịch ảnh: {ex.Message}", true);
                return (false, null, ex.Message);
            }
            finally
            {
                _geminiSrtRpmSemaphore?.Release();
            }
        }
        private string GetSystemPromptForSrtTranslation(string selectedGenre, string targetLanguage)
        {
            string baseSrtPrompt;
            if (selectedGenre == "H.Huyễn Tiên Hiệp")
            {
                baseSrtPrompt = $@"Hãy dịch các đoạn phụ đề phim sau sang {targetLanguage}, sử dụng văn phong phim tiên hiệp. QUY TẮC BẮT BUỘC: 1. Kết quả trả về CHỈ LÀ VĂN BẢN THUẦN TÚY. 2. GIỮ NGUYÊN ĐỊNH DẠNG `SốThứTự: Nội dung đã dịch` cho mỗi dòng. Ví dụ `123: Đây là bản dịch.` 3. KHÔNG thêm bất kỳ lời dẫn, giải thích, markdown, in đậm, in nghiêng nào. 4. Xác định và thống nhất tên riêng, địa danh, công pháp sang Hán Việt. 5. Dịch đúng đại từ nhân xưng cổ trang thường gặp như sau sang {targetLanguage}: 汝 (ngươi), 吾 (ta), 他 (hắn), 她 (nàng), 伊 (y), 师姐 (sư tỷ), 师尊 (sư tôn), 郎君 (chàng), 妹妹 (muội muội), 姐姐 (tỷ tỷ), 兄弟 (huynh đệ), 大哥 (đại ca). 7. Tuyệt đối KHÔNG sử dụng từ ngữ hiện đại như bạn, tôi, anh, chị,em, mày, tao, thằng, con, cha, mẹ... 8.Nếu một dòng gốc không có nội dung, hãy trả về dòng đó với nội dung dịch trống.";
            }
            else if (selectedGenre == "Ngôn Tình")
            {
                baseSrtPrompt = $@"Hãy dịch các dòng phụ đề sau sang {targetLanguage}, sử dụng văn phong lãng mạn, hiện đại hoặc cổ trang tùy theo ngữ cảnh. QUY TẮC BẮT BUỘC: 1. Kết quả trả về CHỈ LÀ VĂN BẢN THUẦN TÚY. 2. GIỮ NGUYÊN ĐỊNH DẠNG `SốThứTự: Nội dung đã dịch` cho mỗi dòng. 3. KHÔNG thêm bất kỳ lời dẫn, giải thích, markdown nào. 4. Dịch chính xác tên nhân vật theo Hán Việt. 5. Đảm bảo đại từ nhân xưng phù hợp với tình huống và mối quan hệ nhân vật. 6. Giữ nguyên số lượng dòng như đầu vào. Nếu một dòng gốc không có nội dung, hãy trả về dòng đó với nội dung dịch trống. 7. Khắc phục lỗi xưng hô sai giới tính nếu phát hiện được từ ngữ cảnh.";
            }
            else if (selectedGenre == "Khoa học lịch sử")
            {
                baseSrtPrompt = $"Hãy dịch các đoạn hội thoại sau sang {targetLanguage}, đảm bảo các thuật ngữ khoa học hay các sự kiện lịch sử chính xác. QUY TẮC BẮT BUỘC: 1. Kết quả trả về CHỈ LÀ VĂN BẢN THUẦN TÚY. 2. GIỮ NGUYÊN ĐỊNH DẠNG `SốThứTự: Nội dung đã dịch` cho mỗi dòng. 3. KHÔNG thêm bất kỳ lời dẫn, giải thích, markdown nào. 4. Giữ nguyên số lượng dòng như đầu vào. Nếu một dòng gốc không có nội dung, hãy trả về dòng đó với nội dung dịch trống.";
            }
            else // Đô Thị Hiện Đại
            {
                baseSrtPrompt = $@"Bạn là một chuyên gia dịch thuật phụ đề phim, chuyên về thể loại đô thị hiện đại. Hãy dịch các dòng phụ đề sau sang {targetLanguage}, sử dụng văn phong tự nhiên, hiện đại. QUY TẮC BẮT BUỘC: 1. Kết quả trả về CHỈ LÀ VĂN BẢN THUẦN TÚY. 2. GIỮ NGUYÊN ĐỊNH DẠNG `SốThứTự: Nội dung đã dịch` cho mỗi dòng. 3. KHÔNG thêm bất kỳ lời dẫn, giải thích, markdown nào. 4. Dịch chính xác tên nhân vật theo Hán Việt nếu có. 5. Đảm bảo ngôn ngữ đời thường, dễ hiểu. 6. Giữ nguyên số lượng dòng như đầu vào. Nếu một dòng gốc không có nội dung, hãy trả về dòng đó với nội dung dịch trống. 7. Khắc phục lỗi xưng hô sai giới tính nếu phát hiện được từ ngữ cảnh.";
            }
            return baseSrtPrompt;
        }

        public string GetSystemInstructionForGeminiSrtTranslation(string selectedGenre, string targetLanguage)
        {
            string baseFormattingAndCoreRules = $@"QUY TẮC CHUNG:
1. Dịch toàn bộ sang {targetLanguage}, KHÔNG bỏ dòng nào.
2. Kết quả là văn bản thuần túy, KHÔNG thêm lời dẫn, chú thích, markdown, in đậm/in nghiêng.
3. Mỗi dòng phải giữ nguyên số thứ tự. Ví dụ: 123: Nội dung dịch. Nếu không có gì để dịch, trả về: 123: Không có nội dung để dịch.
4. Cố gắng dịch ngắn gọn phụ đề nhưng vẫn truyền tải đầy đủ nội dung";

            string genreSpecificConstraints = "";

            if (selectedGenre == "H.Huyễn Tiên Hiệp")
            {
                genreSpecificConstraints = $@"QUY TẮC CHO PHIM THỂ LOẠI TU TIÊN / HUYỀN HUYỄN:
- Văn phong vẫn dễ hiểu, phù hợp với đối thoại trong phim tu tiên - tiên hiệp.
- Lời thoại phải mượt mà, ngắn gọn, không rườm rà.
- Tên riêng (nhân vật, địa danh, pháp bảo...) giữ nguyên Hán-Việt, KHÔNG dịch nghĩa từng chữ. Đảm bảo nhất quán.
- Dùng đúng các đại từ cổ phù hợp hoàn cảnh như ví dụ: thiên địa (天地) ngươi (汝), ta (我吾), hắn (他), nàng (她), y (他她伊), sư tỷ (师姐), sư tôn (师尊), sư đệ (师弟), sư muội (师妹), chàng (郎君他), muội muội (妹妹), tỷ tỷ (姐姐), huynh đệ (兄弟), đại ca (大哥), sư tổ (师祖), tiên tử (仙子), thánh nữ (圣女), ma nữ (魔女), tiểu yêu (小妖), đệ (弟), đệ tử (弟子) huynh (兄), tỷ (姐), thúc thúc (叔叔), tẩu tẩu (嫂嫂嫂子), bá bá (伯伯), lão gia (老爷), nha đầu (丫头), tiểu nha đầu (小丫头), đệ đệ (弟弟), thiếp (妾), tiểu thư (小姐), công tử (公子), các vị (各位), bản tọa (本座), lão phu (老夫), tại hạ (在下), đạo hữu (道友), tiền bối (前辈), vãn bối (晚辈), tiểu bối (小辈), tiểu tử (小子), tiểu nha đầu (小丫头), cô nương (姑娘), nô tỳ (奴婢), không tự tiện sửa thành đại từ khác nếu như không rõ ràng trong ngữ cảnh.
- TRÁNH TUYỆT ĐỐI các đại từ hiện đại như: tôi, mày, tao, bạn, ông, bà, cha mẹ...
- Ưu tiên dịch sát nghĩa, không được cường điệu hay “văn vẻ hóa” lời thoại, ví dụ: trời đất sẽ dịch là thiên địa, cha dịch là phụ thân, mẹ dịch là mẫu thân, cha mẹ dịch là phụ mẫu,..cố gắng duy trì văn phong cổ trang, không tự ý thêm dấu . , vào cuối câu
";
            }
            else if (selectedGenre == "Ngôn Tình")
            {
                genreSpecificConstraints = @"QUY TẮC CHO PHIM NGÔN TÌNH:
- Giữ nguyên tên riêng Hán-Việt, không dịch nghĩa từng chữ.
- Văn phong hiện đại, lời thoại tự nhiên, biểu cảm phù hợp ngữ cảnh.
- Sử dụng đại từ hiện đại như: tôi, anh, em, cậu, bạn… tùy mối quan hệ.";
            }
            else if (selectedGenre == "Khoa học lịch sử")
            {
                genreSpecificConstraints = @"QUY TẮC CHO PHIM KHOA HỌC / LỊCH SỬ:
- Dịch chính xác và nhất quán các thuật ngữ khoa học, kỹ thuật, tên riêng và các sự kiện lịch sử.
- Văn phong trang trọng, rõ ràng, phù hợp với bối cảnh học thuật hoặc lịch sử.
- Tránh sử dụng ngôn ngữ quá đời thường hoặc tiếng lóng. không tự ý thêm dấu . , vào cuối câu";
            }
            else // Đô Thị Hiện Đại
            {
                genreSpecificConstraints = @"QUY TẮC CHO PHIM ĐÔ THỊ / HIỆN ĐẠI:
- Tên riêng giữ nguyên Hán-Việt. KHÔNG dịch nghĩa từng chữ.
- Sử dụng đại từ hiện đại tự nhiên như: tôi, anh, em, chị, bạn, v.v.
- Lời thoại mạch lạc, gần gũi, dễ hiểu. không tự ý thêm dấu . , vào cuối câu";
            }

            return !string.IsNullOrWhiteSpace(genreSpecificConstraints)
                ? $"{baseFormattingAndCoreRules}\n\n{genreSpecificConstraints}"
                : baseFormattingAndCoreRules;
        }

    }
}