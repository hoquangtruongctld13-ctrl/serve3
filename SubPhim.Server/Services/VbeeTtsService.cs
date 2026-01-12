// VỊ TRÍ: Services/VbeeTtsService.cs
// Service quản lý Vbee TTS với obfuscation, quota tracking và encryption

using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubPhim.Server.Services
{
    public interface IVbeeTtsService
    {
        // Voices management
        Task<List<VbeeVoiceDto>> GetVoicesForClientAsync(int userId);
        
        // Quota management
        Task<VbeeQuotaDto> GetUserQuotaAsync(int userId);
        Task<bool> CheckUserHasQuotaAsync(int userId, long requestedCharacters);
        Task<(bool success, string message)> AdjustUserQuotaAsync(int userId, string input, bool resetMonthly);
        Task ResetUserQuotaIfNeededAsync(int userId);
        
        // Session management với obfuscation
        Task<(string sessionId, string obfuscationToken, VbeeTtsConfigDto config)?> InitializeSessionAsync(int userId, string voiceCode, long estimatedCharacters);
        Task<bool> ValidateSessionTokenAsync(string sessionId, string token);
        Task<bool> ReportCharacterUsageAsync(string sessionId, string token, long charactersUsed);
        Task<bool> CompleteSessionAsync(string sessionId, string token, long totalCharactersUsed);
        Task<bool> CancelSessionAsync(string sessionId, string token);
        Task CleanupDisconnectedSessionsAsync();
        
        // Heartbeat
        Task<bool> HeartbeatAsync(string sessionId, string token);
        
        // API Proxy - Gọi đến TTS API thực
        Task<(bool success, byte[] audioData, string error)> SynthesizeAsync(string sessionId, string token, VbeeSynthesizeRequest request);
        
        // Encryption helpers
        string EncryptForClient(string plainText);
        string DecryptFromClient(string encryptedText);
        string GenerateObfuscatedVoiceId(string realVoiceCode);
        string DecodeObfuscatedVoiceId(string obfuscatedId);
        
        // Text preprocessing (based on Python logic)
        string PreprocessText(string text);
        List<string> SplitTextToChunks(string text, int maxLength = 1000);
    }

    public class VbeeTtsService : IVbeeTtsService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VbeeTtsService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _obfuscationSalt;
        
        // Cache settings in memory
        private VbeeTtsSetting? _cachedSettings;
        private DateTime _settingsCacheTime = DateTime.MinValue;
        private readonly TimeSpan _settingsCacheDuration = TimeSpan.FromMinutes(5);

        public VbeeTtsService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<VbeeTtsService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            
            // Khởi tạo encryption key từ config hoặc tạo mặc định
            var keyString = _configuration["VbeeTts:EncryptionKey"] ?? "VbeeTts@SubPhim2025SecretKey!";
            _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
            
            var saltString = _configuration["VbeeTts:ObfuscationSalt"] ?? "VbeeObfSalt2025";
            _obfuscationSalt = Encoding.UTF8.GetBytes(saltString);
        }

        #region Voices Management

        public async Task<List<VbeeVoiceDto>> GetVoicesForClientAsync(int userId)
        {
            var voices = await _context.VbeeVoices
                .Where(v => v.IsEnabled)
                .OrderBy(v => v.DisplayOrder)
                .ThenBy(v => v.DisplayName)
                .ToListAsync();

            return voices.Select(v => new VbeeVoiceDto
            {
                VoiceId = GenerateObfuscatedVoiceId(v.VoiceCode), // Mã hóa voice ID
                DisplayName = v.DisplayName,
                Gender = v.Gender,
                LanguageCode = v.LanguageCode,
                Description = v.Description
            }).ToList();
        }

        #endregion

        #region Quota Management

        public async Task<VbeeQuotaDto> GetUserQuotaAsync(int userId)
        {
            await ResetUserQuotaIfNeededAsync(userId);
            
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return new VbeeQuotaDto { Limit = 0, Used = 0, Remaining = 0 };
            }

            return new VbeeQuotaDto
            {
                Limit = user.VbeeCharacterLimit,
                Used = user.VbeeCharactersUsed,
                Remaining = Math.Max(0, user.VbeeCharacterLimit - user.VbeeCharactersUsed),
                ResetMonthly = user.VbeeResetMonthly,
                QuotaStartedAt = user.VbeeQuotaStartedAt,
                NextResetAt = user.VbeeResetMonthly && user.VbeeQuotaStartedAt.HasValue 
                    ? user.VbeeQuotaStartedAt.Value.AddDays(30) 
                    : null
            };
        }

        public async Task<bool> CheckUserHasQuotaAsync(int userId, long requestedCharacters)
        {
            await ResetUserQuotaIfNeededAsync(userId);
            
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            var remaining = user.VbeeCharacterLimit - user.VbeeCharactersUsed;
            return remaining >= requestedCharacters;
        }

        public async Task<(bool success, string message)> AdjustUserQuotaAsync(int userId, string input, bool resetMonthly)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return (false, "Không tìm thấy người dùng");
            }

            // Parse smart input: +1000, 1000, -1000
            var trimmedInput = input.Trim();
            long adjustment = 0;
            bool isAddition = true;

            if (trimmedInput.StartsWith("+"))
            {
                isAddition = true;
                if (!long.TryParse(trimmedInput.Substring(1), out adjustment))
                {
                    return (false, "Giá trị không hợp lệ. Ví dụ: +1000 hoặc -500");
                }
            }
            else if (trimmedInput.StartsWith("-"))
            {
                isAddition = false;
                if (!long.TryParse(trimmedInput.Substring(1), out adjustment))
                {
                    return (false, "Giá trị không hợp lệ. Ví dụ: +1000 hoặc -500");
                }
            }
            else
            {
                // Số không có dấu = thêm
                isAddition = true;
                if (!long.TryParse(trimmedInput, out adjustment))
                {
                    return (false, "Giá trị không hợp lệ. Ví dụ: 1000 hoặc -500");
                }
            }

            if (isAddition)
            {
                user.VbeeCharacterLimit += adjustment;
            }
            else
            {
                user.VbeeCharacterLimit = Math.Max(0, user.VbeeCharacterLimit - adjustment);
            }

            // Cập nhật reset monthly setting
            var previousResetMonthly = user.VbeeResetMonthly;
            user.VbeeResetMonthly = resetMonthly;
            
            // Nếu bật reset monthly lần đầu, set ngày bắt đầu
            if (resetMonthly && !previousResetMonthly)
            {
                user.VbeeQuotaStartedAt = DateTime.UtcNow;
            }
            else if (!resetMonthly)
            {
                // Nếu tắt, clear ngày bắt đầu
                user.VbeeQuotaStartedAt = null;
            }

            await _context.SaveChangesAsync();

            var action = isAddition ? "thêm" : "trừ";
            return (true, $"Đã {action} {adjustment} ký tự. Giới hạn mới: {user.VbeeCharacterLimit}");
        }

        public async Task ResetUserQuotaIfNeededAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.VbeeResetMonthly || !user.VbeeQuotaStartedAt.HasValue)
            {
                return;
            }

            // Check nếu đã quá 30 ngày
            var daysSinceStart = (DateTime.UtcNow - user.VbeeQuotaStartedAt.Value).TotalDays;
            if (daysSinceStart >= 30)
            {
                user.VbeeCharactersUsed = 0;
                user.VbeeQuotaStartedAt = DateTime.UtcNow; // Reset lại chu kỳ
                user.LastVbeeResetUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Reset Vbee quota for user {UserId} after 30 days", userId);
            }
        }

        #endregion

        #region Session Management

        public async Task<(string sessionId, string obfuscationToken, VbeeTtsConfigDto config)?> InitializeSessionAsync(
            int userId, string obfuscatedVoiceCode, long estimatedCharacters)
        {
            // Decode voice ID
            var realVoiceCode = DecodeObfuscatedVoiceId(obfuscatedVoiceCode);
            if (string.IsNullOrEmpty(realVoiceCode))
            {
                _logger.LogWarning("Invalid obfuscated voice code: {VoiceCode}", obfuscatedVoiceCode);
                return null;
            }

            // Check quota
            if (!await CheckUserHasQuotaAsync(userId, estimatedCharacters))
            {
                _logger.LogWarning("User {UserId} has insufficient Vbee quota", userId);
                return null;
            }

            // Check voice exists
            var voice = await _context.VbeeVoices.FirstOrDefaultAsync(v => v.VoiceCode == realVoiceCode && v.IsEnabled);
            if (voice == null)
            {
                _logger.LogWarning("Voice not found: {VoiceCode}", realVoiceCode);
                return null;
            }

            // Create session
            var session = new VbeeTtsSession
            {
                SessionId = Guid.NewGuid().ToString(),
                UserId = userId,
                VoiceCode = realVoiceCode,
                TotalCharactersRequested = estimatedCharacters,
                Status = VbeeTtsSessionStatus.Pending,
                ObfuscationToken = GenerateSecureToken(),
                CreatedAt = DateTime.UtcNow,
                LastHeartbeatAt = DateTime.UtcNow
            };

            _context.VbeeTtsSessions.Add(session);
            await _context.SaveChangesAsync();

            // Get API config - trả về URL thực cho client gọi trực tiếp
            var settings = await GetSettingsAsync();
            var apiUrl = DecryptStoredValue(settings?.EncryptedApiUrl ?? "", settings?.ApiUrlIv ?? "");
            
            // Mã hóa URL trước khi gửi cho client (bảo vệ khỏi sniffing)
            var encryptedApiUrl = EncryptForClient(apiUrl);
            
            var config = new VbeeTtsConfigDto
            {
                // Trả API URL đã mã hóa - client sẽ giải mã
                ApiUrl = encryptedApiUrl,
                SynthesizeEndpoint = settings?.SynthesizeEndpoint ?? "/synthesize",
                VoiceCode = session.VoiceCode, // Voice ID thực để gọi Vbee API
                MaxChunkSize = settings?.MaxChunkSize ?? 100000,
                PauseSettings = new VbeePauseSettingsDto
                {
                    UsePunctuationPauses = true,
                    PausePeriod = (double)(settings?.PausePeriod ?? 0.35m),
                    PauseComma = (double)(settings?.PauseComma ?? 0.025m),
                    PauseSemicolon = (double)(settings?.PauseSemicolon ?? 0.3m),
                    PauseNewline = (double)(settings?.PauseNewline ?? 0.6m)
                }
            };

            return (session.SessionId, session.ObfuscationToken!, config);
        }

        public async Task<bool> ValidateSessionTokenAsync(string sessionId, string token)
        {
            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.ObfuscationToken == token);
            
            return session != null && 
                   session.Status != VbeeTtsSessionStatus.Cancelled && 
                   session.Status != VbeeTtsSessionStatus.Failed;
        }

        public async Task<bool> ReportCharacterUsageAsync(string sessionId, string token, long charactersUsed)
        {
            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.ObfuscationToken == token);
            
            if (session == null) return false;

            session.CharactersProcessed += charactersUsed;
            session.LastHeartbeatAt = DateTime.UtcNow;
            
            if (session.Status == VbeeTtsSessionStatus.Pending)
            {
                session.Status = VbeeTtsSessionStatus.Processing;
            }

            // Cập nhật user quota
            var user = await _context.Users.FindAsync(session.UserId);
            if (user != null)
            {
                user.VbeeCharactersUsed += charactersUsed;
                session.CharactersCharged += charactersUsed;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CompleteSessionAsync(string sessionId, string token, long totalCharactersUsed)
        {
            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.ObfuscationToken == token);
            
            if (session == null) return false;

            // Điều chỉnh quota nếu cần (nếu actual < charged, refund)
            var user = await _context.Users.FindAsync(session.UserId);
            if (user != null && session.CharactersCharged > totalCharactersUsed)
            {
                var refund = session.CharactersCharged - totalCharactersUsed;
                user.VbeeCharactersUsed = Math.Max(0, user.VbeeCharactersUsed - refund);
            }

            session.CharactersProcessed = totalCharactersUsed;
            session.Status = VbeeTtsSessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CancelSessionAsync(string sessionId, string token)
        {
            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.ObfuscationToken == token);
            
            if (session == null) return false;

            // Refund charged characters
            var user = await _context.Users.FindAsync(session.UserId);
            if (user != null && session.CharactersCharged > 0)
            {
                user.VbeeCharactersUsed = Math.Max(0, user.VbeeCharactersUsed - session.CharactersCharged);
                _logger.LogInformation("Refunded {Characters} Vbee characters for cancelled session {SessionId}", 
                    session.CharactersCharged, sessionId);
            }

            session.Status = VbeeTtsSessionStatus.Cancelled;
            session.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task CleanupDisconnectedSessionsAsync()
        {
            var disconnectThreshold = DateTime.UtcNow.AddMinutes(-5); // 5 phút không heartbeat = mất kết nối
            
            var disconnectedSessions = await _context.VbeeTtsSessions
                .Where(s => s.Status == VbeeTtsSessionStatus.Processing && 
                            s.LastHeartbeatAt < disconnectThreshold)
                .ToListAsync();

            foreach (var session in disconnectedSessions)
            {
                session.Status = VbeeTtsSessionStatus.Disconnected;
                
                // Backup data để recovery
                var backupData = new
                {
                    session.CharactersCharged,
                    session.CharactersProcessed,
                    session.TotalCharactersRequested,
                    DisconnectedAt = DateTime.UtcNow
                };
                session.EncryptedBackupData = EncryptForClient(JsonSerializer.Serialize(backupData));
                
                _logger.LogWarning("Session {SessionId} marked as disconnected", session.SessionId);
            }

            if (disconnectedSessions.Any())
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> HeartbeatAsync(string sessionId, string token)
        {
            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.ObfuscationToken == token);
            
            if (session == null) return false;

            session.LastHeartbeatAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        #endregion

        #region API Proxy

        public async Task<(bool success, byte[] audioData, string error)> SynthesizeAsync(
            string sessionId, string token, VbeeSynthesizeRequest request)
        {
            // Validate session
            if (!await ValidateSessionTokenAsync(sessionId, token))
            {
                return (false, Array.Empty<byte>(), "Invalid session or token");
            }

            var session = await _context.VbeeTtsSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
            if (session == null)
            {
                return (false, Array.Empty<byte>(), "Session not found");
            }

            var settings = await GetSettingsAsync();
            if (settings == null)
            {
                return (false, Array.Empty<byte>(), "TTS settings not configured");
            }

            // Decrypt API URL
            var apiUrl = DecryptStoredValue(settings.EncryptedApiUrl, settings.ApiUrlIv);
            if (string.IsNullOrEmpty(apiUrl))
            {
                return (false, Array.Empty<byte>(), "API URL not configured");
            }

            // Preprocess text first
            var processedText = PreprocessText(request.Text);
            
            // Split into chunks if text is too long
            var maxChunkSize = settings.MaxChunkSize > 0 ? settings.MaxChunkSize : 1000;
            var chunks = SplitTextToChunks(processedText, maxChunkSize);
            
            if (chunks.Count == 0)
            {
                return (false, Array.Empty<byte>(), "Text is empty after preprocessing");
            }

            _logger.LogInformation("Synthesizing {ChunkCount} chunks for session {SessionId}", chunks.Count, sessionId);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            var fullUrl = $"{apiUrl.TrimEnd('/')}{settings.SynthesizeEndpoint}";

            var allAudioChunks = new List<byte[]>();
            var totalCharactersProcessed = 0;

            try
            {
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var chunkText = chunks[chunkIndex];
                    if (string.IsNullOrWhiteSpace(chunkText)) continue;

                    // Build payload for this chunk
                    var payload = new
                    {
                        text = chunkText,
                        voice_id = session.VoiceCode,
                        format = request.Format ?? "mp3",
                        language = request.Language ?? "vi",
                        speed = request.Speed,
                        pause_settings = new
                        {
                            use_punctuation_pauses = request.PauseSettings?.UsePunctuationPauses ?? true,
                            pause_period = request.PauseSettings?.PausePeriod ?? (double)settings.PausePeriod,
                            pause_comma = request.PauseSettings?.PauseComma ?? (double)settings.PauseComma,
                            pause_semicolon = request.PauseSettings?.PauseSemicolon ?? (double)settings.PauseSemicolon,
                            pause_newline = request.PauseSettings?.PauseNewline ?? (double)settings.PauseNewline
                        }
                    };

                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    byte[]? chunkAudio = null;
                    string lastError = "";

                    // Retry logic for each chunk
                    for (int attempt = 0; attempt < settings.MaxRetries; attempt++)
                    {
                        try
                        {
                            var response = await client.PostAsync(fullUrl, content);
                            
                            if (response.IsSuccessStatusCode)
                            {
                                chunkAudio = await response.Content.ReadAsByteArrayAsync();
                                if (chunkAudio.Length > 0)
                                {
                                    _logger.LogDebug("Chunk {ChunkIndex}/{Total} synthesized: {Bytes} bytes", 
                                        chunkIndex + 1, chunks.Count, chunkAudio.Length);
                                    break;
                                }
                            }
                            else
                            {
                                var errorBody = await response.Content.ReadAsStringAsync();
                                lastError = $"HTTP {(int)response.StatusCode}: {errorBody}";
                                _logger.LogWarning("TTS API error (chunk {ChunkIndex}, attempt {Attempt}): {Error}", 
                                    chunkIndex + 1, attempt + 1, lastError);
                                
                                // Don't retry on 4xx except 429
                                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500 && 
                                    (int)response.StatusCode != 429)
                                {
                                    // Log the problematic chunk for debugging
                                    _logger.LogError("Chunk failed with 4xx - Text length: {Len}, First 100 chars: {Text}", 
                                        chunkText.Length, chunkText.Length > 100 ? chunkText.Substring(0, 100) + "..." : chunkText);
                                    return (false, Array.Empty<byte>(), $"API error: {response.StatusCode}");
                                }
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            lastError = $"Timeout (attempt {attempt + 1})";
                            _logger.LogWarning("TTS API timeout (chunk {ChunkIndex}, attempt {Attempt})", chunkIndex + 1, attempt + 1);
                        }
                        catch (HttpRequestException ex)
                        {
                            lastError = ex.Message;
                            _logger.LogWarning(ex, "TTS API request failed (chunk {ChunkIndex}, attempt {Attempt})", chunkIndex + 1, attempt + 1);
                        }

                        if (attempt < settings.MaxRetries - 1)
                        {
                            await Task.Delay(settings.RetryDelayMs * (attempt + 1));
                        }
                    }

                    if (chunkAudio == null || chunkAudio.Length == 0)
                    {
                        return (false, Array.Empty<byte>(), $"Chunk {chunkIndex + 1}/{chunks.Count} failed: {lastError}");
                    }

                    allAudioChunks.Add(chunkAudio);
                    totalCharactersProcessed += chunkText.Length;
                }

                // Combine all audio chunks
                byte[] finalAudio;
                if (allAudioChunks.Count == 1)
                {
                    finalAudio = allAudioChunks[0];
                }
                else
                {
                    // Simple concatenation for MP3 (works for constant bitrate)
                    // For more reliable concatenation, would need to use FFmpeg or NAudio
                    var totalLength = allAudioChunks.Sum(c => c.Length);
                    finalAudio = new byte[totalLength];
                    var offset = 0;
                    foreach (var chunk in allAudioChunks)
                    {
                        Buffer.BlockCopy(chunk, 0, finalAudio, offset, chunk.Length);
                        offset += chunk.Length;
                    }
                    _logger.LogInformation("Combined {Count} audio chunks into {Bytes} bytes", allAudioChunks.Count, finalAudio.Length);
                }

                // Report total usage
                await ReportCharacterUsageAsync(sessionId, token, totalCharactersProcessed);
                
                return (true, finalAudio, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during TTS synthesis");
                return (false, Array.Empty<byte>(), $"Internal error: {ex.Message}");
            }
        }

        #endregion

        #region Text Preprocessing (Based on Python logic)

        public string PreprocessText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // replace_symbols: ; - : — ! -> .
            var symbolReplacements = new Dictionary<char, char>
            {
                { ';', '.' }, { '-', '.' }, { ':', '.' }, { '—', '.' }, { '!', '.' }
            };
            foreach (var kvp in symbolReplacements)
            {
                text = text.Replace(kvp.Key, kvp.Value);
            }

            // remove_quotes
            text = text.Replace("'", "").Replace("\"", "");

            // xoa_xuong_dong_space
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    // Add period if line doesn't end with punctuation
                    if (!".!?;:".Contains(trimmed[^1]))
                    {
                        trimmed += ".";
                    }
                    result.Add(trimmed);
                }
            }

            return string.Join(" ", result);
        }

        public List<string> SplitTextToChunks(string text, int maxLength = 1000)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            text = text.Trim();

            // Split by sentence-ending punctuation
            var parts = Regex.Split(text, @"([.!?;:。！？；：\n]+)");
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                
                // Attach punctuation to previous part
                if (i + 1 < parts.Length && parts[i + 1].Trim().Length <= 2)
                {
                    part = part + parts[i + 1];
                    i++;
                }

                if (currentChunk.Length + part.Length <= maxLength)
                {
                    currentChunk.Append(part);
                }
                else
                {
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                    }
                    currentChunk.Clear();
                    currentChunk.Append(part);
                }
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            // Split long chunks further
            var finalChunks = new List<string>();
            foreach (var chunk in chunks)
            {
                if (chunk.Length > maxLength)
                {
                    finalChunks.AddRange(SplitLongSentence(chunk, maxLength));
                }
                else if (!string.IsNullOrWhiteSpace(chunk))
                {
                    finalChunks.Add(chunk);
                }
            }

            return finalChunks;
        }

        private List<string> SplitLongSentence(string text, int maxLength)
        {
            if (text.Length <= maxLength) return new List<string> { text };

            // Split by commas and other delimiters
            var parts = Regex.Split(text, @"([、，,\s]+)");
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();

            foreach (var part in parts)
            {
                if (currentChunk.Length + part.Length <= maxLength)
                {
                    currentChunk.Append(part);
                }
                else
                {
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                    }
                    currentChunk.Clear();
                    currentChunk.Append(part);
                }
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

        #endregion

        #region Encryption Helpers

        public string EncryptForClient(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine IV + encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return Convert.ToBase64String(result);
        }

        public string DecryptFromClient(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

            try
            {
                var fullBytes = Convert.FromBase64String(encryptedText);
                
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;

                // Extract IV (first 16 bytes)
                var iv = new byte[16];
                Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
                aes.IV = iv;

                // Extract encrypted data
                var encryptedBytes = new byte[fullBytes.Length - 16];
                Buffer.BlockCopy(fullBytes, 16, encryptedBytes, 0, encryptedBytes.Length);

                using var decryptor = aes.CreateDecryptor();
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt client data");
                return string.Empty;
            }
        }

        public string GenerateObfuscatedVoiceId(string realVoiceCode)
        {
            // Tạo obfuscated ID: HMAC(voiceCode + timestamp_rounded) + encoded_voiceCode
            var roundedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600; // Per hour
            var data = Encoding.UTF8.GetBytes($"{realVoiceCode}:{roundedTime}");
            
            using var hmac = new HMACSHA256(_obfuscationSalt);
            var hash = hmac.ComputeHash(data);
            var hashPart = Convert.ToBase64String(hash).Substring(0, 8);
            
            // Encode voice code
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(realVoiceCode))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');
            
            return $"{hashPart}.{encoded}";
        }

        public string DecodeObfuscatedVoiceId(string obfuscatedId)
        {
            if (string.IsNullOrEmpty(obfuscatedId)) return string.Empty;

            try
            {
                var parts = obfuscatedId.Split('.');
                if (parts.Length != 2) return string.Empty;

                // Decode the voice code part
                var encoded = parts[1].Replace("-", "+").Replace("_", "/");
                
                // Add padding if needed
                switch (encoded.Length % 4)
                {
                    case 2: encoded += "=="; break;
                    case 3: encoded += "="; break;
                }

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                
                // Validate hash (allow current hour and previous hour)
                for (int offset = 0; offset <= 1; offset++)
                {
                    var roundedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 - offset;
                    var data = Encoding.UTF8.GetBytes($"{decoded}:{roundedTime}");
                    
                    using var hmac = new HMACSHA256(_obfuscationSalt);
                    var hash = hmac.ComputeHash(data);
                    var expectedHash = Convert.ToBase64String(hash).Substring(0, 8);
                    
                    if (parts[0] == expectedHash)
                    {
                        return decoded;
                    }
                }

                _logger.LogWarning("Invalid obfuscated voice ID hash");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode obfuscated voice ID");
                return string.Empty;
            }
        }

        private string GenerateSecureToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private string DecryptStoredValue(string encryptedValue, string iv)
        {
            if (string.IsNullOrEmpty(encryptedValue) || string.IsNullOrEmpty(iv))
                return string.Empty;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = Convert.FromBase64String(iv);

                using var decryptor = aes.CreateDecryptor();
                var encryptedBytes = Convert.FromBase64String(encryptedValue);
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt stored value");
                return string.Empty;
            }
        }

        #endregion

        #region Settings Cache

        private async Task<VbeeTtsSetting?> GetSettingsAsync()
        {
            if (_cachedSettings != null && DateTime.UtcNow - _settingsCacheTime < _settingsCacheDuration)
            {
                return _cachedSettings;
            }

            _cachedSettings = await _context.VbeeTtsSettings.FirstOrDefaultAsync(s => s.Id == 1);
            _settingsCacheTime = DateTime.UtcNow;
            
            return _cachedSettings;
        }

        #endregion
    }

    #region DTOs

    public class VbeeVoiceDto
    {
        public string VoiceId { get; set; } = string.Empty; // Obfuscated
        public string DisplayName { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public string LanguageCode { get; set; } = "vi-VN";
        public string? Description { get; set; }
    }

    public class VbeeQuotaDto
    {
        public long Limit { get; set; }
        public long Used { get; set; }
        public long Remaining { get; set; }
        public bool ResetMonthly { get; set; }
        public DateTime? QuotaStartedAt { get; set; }
        public DateTime? NextResetAt { get; set; }
        
        public string UsedFormatted => FormatNumber(Used);
        public string LimitFormatted => FormatNumber(Limit);
        public string RemainingFormatted => FormatNumber(Remaining);
        
        private static string FormatNumber(long n)
        {
            if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
            if (n >= 1_000) return $"{n / 1_000.0:F1}K";
            return n.ToString();
        }
    }

    public class VbeeTtsConfigDto
    {
        public string ApiUrl { get; set; } = string.Empty; // URL API Vbee (đã mã hóa)
        public string SynthesizeEndpoint { get; set; } = "/synthesize"; // Endpoint để gọi
        public string VoiceCode { get; set; } = string.Empty; // Voice ID thực để gọi API
        public int MaxChunkSize { get; set; }
        public VbeePauseSettingsDto PauseSettings { get; set; } = new();
    }

    public class VbeePauseSettingsDto
    {
        public bool UsePunctuationPauses { get; set; } = true;
        public double PausePeriod { get; set; } = 0.35;
        public double PauseComma { get; set; } = 0.025;
        public double PauseSemicolon { get; set; } = 0.3;
        public double PauseNewline { get; set; } = 0.6;
    }

    public class VbeeSynthesizeRequest
    {
        public string Text { get; set; } = string.Empty;
        public string? Format { get; set; } = "mp3";
        public string? Language { get; set; } = "vi";
        public double Speed { get; set; } = 1.0;
        public VbeePauseSettingsDto? PauseSettings { get; set; }
    }

    #endregion
}
