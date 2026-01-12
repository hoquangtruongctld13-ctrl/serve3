// VỊ TRÍ: Pages/Admin/VbeeTtsManagement.cshtml.cs
// Code-behind cho trang quản lý Vbee TTS

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Security.Cryptography;
using System.Text;

namespace SubPhim.Server.Pages.Admin
{
    public class VbeeTtsManagementModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VbeeTtsManagementModel> _logger;
        private readonly byte[] _encryptionKey;

        public VbeeTtsManagementModel(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<VbeeTtsManagementModel> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;

            var keyString = _configuration["VbeeTts:EncryptionKey"] ?? "VbeeTts@SubPhim2025SecretKey!";
            _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
        }

        public List<VbeeVoice> Voices { get; set; } = new();
        public VbeeTtsSettingViewModel? Settings { get; set; }
        public List<VbeeTtsSession> ActiveSessions { get; set; } = new();
        
        [TempData]
        public string? SuccessMessage { get; set; }
        
        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            Voices = await _context.VbeeVoices
                .OrderBy(v => v.DisplayOrder)
                .ThenBy(v => v.DisplayName)
                .ToListAsync();

            var settings = await _context.VbeeTtsSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings != null)
            {
                Settings = new VbeeTtsSettingViewModel
                {
                    SynthesizeEndpoint = settings.SynthesizeEndpoint,
                    RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
                    MaxRetries = settings.MaxRetries,
                    RetryDelayMs = settings.RetryDelayMs,
                    MaxChunkSize = settings.MaxChunkSize,
                    PausePeriod = settings.PausePeriod,
                    PauseComma = settings.PauseComma,
                    PauseSemicolon = settings.PauseSemicolon,
                    PauseNewline = settings.PauseNewline,
                    DecryptedApiUrl = DecryptValue(settings.EncryptedApiUrl, settings.ApiUrlIv)
                };
            }

            ActiveSessions = await _context.VbeeTtsSessions
                .Include(s => s.User)
                .Where(s => s.Status == VbeeTtsSessionStatus.Pending || 
                            s.Status == VbeeTtsSessionStatus.Processing ||
                            s.Status == VbeeTtsSessionStatus.Disconnected)
                .OrderByDescending(s => s.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddVoiceAsync(
            string voiceCode,
            string displayName,
            string gender,
            string languageCode)
        {
            if (string.IsNullOrWhiteSpace(voiceCode) || string.IsNullOrWhiteSpace(displayName))
            {
                ErrorMessage = "Voice code và tên hiển thị không được để trống";
                return RedirectToPage();
            }

            // Check duplicate
            if (await _context.VbeeVoices.AnyAsync(v => v.VoiceCode == voiceCode))
            {
                ErrorMessage = $"Voice code '{voiceCode}' đã tồn tại";
                return RedirectToPage();
            }

            var voice = new VbeeVoice
            {
                VoiceCode = voiceCode.Trim(),
                DisplayName = displayName.Trim(),
                Gender = gender ?? "Female",
                LanguageCode = languageCode?.Trim() ?? "vi-VN",
                IsEnabled = true,
                DisplayOrder = await _context.VbeeVoices.CountAsync(),
                CreatedAt = DateTime.UtcNow
            };

            _context.VbeeVoices.Add(voice);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Đã thêm voice '{displayName}' thành công";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleVoiceAsync(int voiceId)
        {
            var voice = await _context.VbeeVoices.FindAsync(voiceId);
            if (voice == null)
            {
                ErrorMessage = "Voice không tồn tại";
                return RedirectToPage();
            }

            voice.IsEnabled = !voice.IsEnabled;
            await _context.SaveChangesAsync();

            SuccessMessage = voice.IsEnabled 
                ? $"Đã bật voice '{voice.DisplayName}'" 
                : $"Đã tắt voice '{voice.DisplayName}'";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteVoiceAsync(int voiceId)
        {
            var voice = await _context.VbeeVoices.FindAsync(voiceId);
            if (voice == null)
            {
                ErrorMessage = "Voice không tồn tại";
                return RedirectToPage();
            }

            _context.VbeeVoices.Remove(voice);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Đã xóa voice '{voice.DisplayName}'";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateVoiceAsync(
            int voiceId,
            string voiceCode,
            string displayName,
            string gender,
            string languageCode)
        {
            var voice = await _context.VbeeVoices.FindAsync(voiceId);
            if (voice == null)
            {
                ErrorMessage = "Voice không tồn tại";
                return RedirectToPage();
            }

            // Check duplicate code (excluding current voice)
            if (await _context.VbeeVoices.AnyAsync(v => v.VoiceCode == voiceCode && v.Id != voiceId))
            {
                ErrorMessage = $"Voice code '{voiceCode}' đã được sử dụng";
                return RedirectToPage();
            }

            voice.VoiceCode = voiceCode.Trim();
            voice.DisplayName = displayName.Trim();
            voice.Gender = gender ?? "Female";
            voice.LanguageCode = languageCode?.Trim() ?? "vi-VN";

            await _context.SaveChangesAsync();

            SuccessMessage = $"Đã cập nhật voice '{displayName}'";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostImportVoicesAsync(string voicesJson)
        {
            if (string.IsNullOrWhiteSpace(voicesJson))
            {
                ErrorMessage = "Dữ liệu import không được để trống";
                return RedirectToPage();
            }

            var addedCount = 0;
            var skippedCount = 0;
            var errorLines = new List<string>();

            try
            {
                // Try JSON format first
                if (voicesJson.Trim().StartsWith("["))
                {
                    var voiceList = System.Text.Json.JsonSerializer.Deserialize<List<VoiceImportDto>>(voicesJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (voiceList != null)
                    {
                        foreach (var v in voiceList)
                        {
                            if (string.IsNullOrWhiteSpace(v.Code) || string.IsNullOrWhiteSpace(v.Name))
                                continue;

                            if (await _context.VbeeVoices.AnyAsync(x => x.VoiceCode == v.Code))
                            {
                                skippedCount++;
                                continue;
                            }

                            _context.VbeeVoices.Add(new VbeeVoice
                            {
                                VoiceCode = v.Code.Trim(),
                                DisplayName = v.Name.Trim(),
                                Gender = v.Gender ?? "Female",
                                LanguageCode = v.Language ?? "vi-VN",
                                IsEnabled = true,
                                DisplayOrder = await _context.VbeeVoices.CountAsync(),
                                CreatedAt = DateTime.UtcNow
                            });
                            addedCount++;
                        }
                    }
                }
                else
                {
                    // CSV format: code, name, gender, language
                    var lines = voicesJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                        if (parts.Length < 2)
                        {
                            errorLines.Add(line);
                            continue;
                        }

                        var code = parts[0];
                        var name = parts[1];
                        var gender = parts.Length > 2 ? parts[2] : "Female";
                        var language = parts.Length > 3 ? parts[3] : "vi-VN";

                        if (await _context.VbeeVoices.AnyAsync(x => x.VoiceCode == code))
                        {
                            skippedCount++;
                            continue;
                        }

                        _context.VbeeVoices.Add(new VbeeVoice
                        {
                            VoiceCode = code,
                            DisplayName = name,
                            Gender = gender,
                            LanguageCode = language,
                            IsEnabled = true,
                            DisplayOrder = await _context.VbeeVoices.CountAsync(),
                            CreatedAt = DateTime.UtcNow
                        });
                        addedCount++;
                    }
                }

                await _context.SaveChangesAsync();

                var msg = $"Đã import {addedCount} voices";
                if (skippedCount > 0) msg += $", bỏ qua {skippedCount} voices trùng";
                if (errorLines.Any()) msg += $", {errorLines.Count} dòng lỗi";
                SuccessMessage = msg;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Lỗi import: {ex.Message}";
            }

            return RedirectToPage();
        }

        private class VoiceImportDto
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Gender { get; set; }
            public string Language { get; set; }
        }

        public async Task<IActionResult> OnPostSaveSettingsAsync(
            string apiUrl,
            string synthesizeEndpoint,
            int requestTimeoutSeconds,
            int maxRetries,
            int retryDelayMs,
            int maxChunkSize,
            decimal pausePeriod,
            decimal pauseComma,
            decimal pauseSemicolon,
            decimal pauseNewline)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                ErrorMessage = "API URL không được để trống";
                return RedirectToPage();
            }

            var settings = await _context.VbeeTtsSettings.FirstOrDefaultAsync(s => s.Id == 1);
            var (encryptedUrl, iv) = EncryptValue(apiUrl.Trim());

            if (settings == null)
            {
                settings = new VbeeTtsSetting
                {
                    Id = 1,
                    EncryptedApiUrl = encryptedUrl,
                    ApiUrlIv = iv,
                    SynthesizeEndpoint = synthesizeEndpoint?.Trim() ?? "/synthesize",
                    RequestTimeoutSeconds = Math.Clamp(requestTimeoutSeconds, 10, 300),
                    MaxRetries = Math.Clamp(maxRetries, 1, 10),
                    RetryDelayMs = Math.Clamp(retryDelayMs, 500, 10000),
                    MaxChunkSize = Math.Clamp(maxChunkSize, 100, 100000),
                    PausePeriod = Math.Max(0, pausePeriod),
                    PauseComma = Math.Max(0, pauseComma),
                    PauseSemicolon = Math.Max(0, pauseSemicolon),
                    PauseNewline = Math.Max(0, pauseNewline),
                    UpdatedAt = DateTime.UtcNow
                };
                _context.VbeeTtsSettings.Add(settings);
            }
            else
            {
                settings.EncryptedApiUrl = encryptedUrl;
                settings.ApiUrlIv = iv;
                settings.SynthesizeEndpoint = synthesizeEndpoint?.Trim() ?? "/synthesize";
                settings.RequestTimeoutSeconds = Math.Clamp(requestTimeoutSeconds, 10, 300);
                settings.MaxRetries = Math.Clamp(maxRetries, 1, 10);
                settings.RetryDelayMs = Math.Clamp(retryDelayMs, 500, 10000);
                settings.MaxChunkSize = Math.Clamp(maxChunkSize, 100, 100000);
                settings.PausePeriod = Math.Max(0, pausePeriod);
                settings.PauseComma = Math.Max(0, pauseComma);
                settings.PauseSemicolon = Math.Max(0, pauseSemicolon);
                settings.PauseNewline = Math.Max(0, pauseNewline);
                settings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            SuccessMessage = "Đã lưu cài đặt thành công";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCleanupSessionsAsync()
        {
            var threshold = DateTime.UtcNow.AddHours(-1);
            var oldSessions = await _context.VbeeTtsSessions
                .Where(s => s.CreatedAt < threshold && 
                            (s.Status == VbeeTtsSessionStatus.Disconnected ||
                             s.Status == VbeeTtsSessionStatus.Pending))
                .ToListAsync();

            if (oldSessions.Any())
            {
                _context.VbeeTtsSessions.RemoveRange(oldSessions);
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã dọn dẹp {oldSessions.Count} sessions cũ";
            }
            else
            {
                SuccessMessage = "Không có sessions cũ cần dọn dẹp";
            }

            return RedirectToPage();
        }

        #region Encryption Helpers

        private (string encrypted, string iv) EncryptValue(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return (Convert.ToBase64String(encryptedBytes), Convert.ToBase64String(aes.IV));
        }

        private string DecryptValue(string encryptedValue, string iv)
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
                _logger.LogError(ex, "Failed to decrypt value");
                return "[Decryption Error]";
            }
        }

        #endregion
    }

    public class VbeeTtsSettingViewModel
    {
        public string? DecryptedApiUrl { get; set; }
        public string SynthesizeEndpoint { get; set; } = "/synthesize";
        public int RequestTimeoutSeconds { get; set; } = 60;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 2000;
        public int MaxChunkSize { get; set; } = 1000;
        public decimal PausePeriod { get; set; } = 0.35m;
        public decimal PauseComma { get; set; } = 0.025m;
        public decimal PauseSemicolon { get; set; } = 0.3m;
        public decimal PauseNewline { get; set; } = 0.6m;
    }
}
