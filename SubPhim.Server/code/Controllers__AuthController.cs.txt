using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options; 
using Microsoft.IdentityModel.Tokens;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using SubPhim.Server.Settings;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    [HttpPost("check-feature-access")]
    [Authorize] // Chỉ người dùng đã đăng nhập mới được gọi
    public async Task<IActionResult> CheckFeatureAccess([FromBody] FeatureAccessRequest request)
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        // Luôn lấy thông tin mới nhất từ DB để đảm bảo quyền là chính xác
        var userInDb = await _context.Users.FindAsync(userId);
        if (userInDb == null)
        {
            return NotFound("Tài khoản không tồn tại.");
        }

        // Chuyển đổi tên tính năng từ chuỗi sang enum
        if (!Enum.TryParse<GrantedFeatures>(request.FeatureName, true, out var featureToCheck))
        {
            return BadRequest($"Tên tính năng không hợp lệ: {request.FeatureName}");
        }

        // Kiểm tra quyền
        bool hasAccess = userInDb.GrantedFeatures.HasFlag(featureToCheck);

        if (hasAccess)
        {
            return Ok(new { HasAccess = true, Message = "OK" });
        }
        else
        {
            return Ok(new { HasAccess = false, Message = $"Bạn không có quyền truy cập tính năng '{request.FeatureName}'. Vui lòng liên hệ admin." });
        }
    }
    public record FeatureAccessRequest(string FeatureName);
    public record DeviceDto(string Hwid, string LastLoginIp, DateTime LastLogin);
    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<UsageLimitsSettings> _usageLimitsMonitor;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ITierSettingsService _tierSettingsService;
    private readonly IEmailService _emailService;
    public AuthController(
     AppDbContext context,
     ILogger<AuthController> logger,
     IMemoryCache cache,
     IConfiguration configuration,
     ITierSettingsService tierSettingsService,
     IEmailService emailService) 
    {
        _context = context;
        _logger = logger;
        _cache = cache;
        _configuration = configuration;
        _tierSettingsService = tierSettingsService;
        _emailService = emailService; 
    }
    public record UsageStatusDto(
       bool CanProcessNewVideo,
       int RemainingVideosToday,
       int MaxVideoDurationMinutes,
       DateTime LimitResetTimeUtc,
       string Message
   );
    public record ViolationReport(string Reason, string Hwid);
    public record RegisterRequest(string Username, string Password, string Email, string Hwid);
    public record LoginRequest(string Username, string Password, string Hwid);
    public record UserDto(
    int Id, string Username, string Uid, string Email, string SubscriptionTier, DateTime SubscriptionExpiry,
    string Token, GrantedFeatures GrantedFeatures,
    AllowedApis AllowedApiAccess,
    int RemainingRequests, int VideosProcessedToday,
    int DailyVideoLimit, List<DeviceDto> Devices,
     int SrtLinesUsedToday,
int DailySrtLineLimit, long TtsCharactersUsed,
long TtsCharacterLimit,
    long AioCharactersUsedToday,
    long AioCharacterLimit,
         int LocalSrtLinesUsedToday,
    int DailyLocalSrtLineLimit,
    int VipSrtLinesUsedToday,
    int DailyVipSrtLimit
);

    public record ForgotPasswordRequest(string Email);

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.Email))
        {
            return BadRequest("Email là bắt buộc.");
        }
        const int MaxForgotPasswordRequestsPerDay = 2;
        var cacheKey = $"forgot_password_limit_{request.Email.ToLower()}";
        if (_cache.TryGetValue(cacheKey, out int requestCount) && requestCount >= MaxForgotPasswordRequestsPerDay)
        {
            _logger.LogWarning("Forgot password limit ({Limit}) reached for email: {Email}", MaxForgotPasswordRequestsPerDay, request.Email);
            return Ok(new { Message = "Nếu email của bạn chính xác là email đã đăng ký tài khoản, hãy kiểm tra hộp thư để lấy mật khẩu mới." });
        }
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower());
        if (user == null)
        {
            _logger.LogInformation("Forgot password request for non-existent email: {Email}", request.Email);
            return Ok(new { Message = "Nếu email của bạn chính xác là email đã đăng ký tài khoản, hãy kiểm tra hộp thư để lấy mật khẩu mới." });
        }
        //  Nếu người dùng tồn tại và chưa quá giới hạn, tăng bộ đếm và lưu vào cache với thời hạn 24 giờ
        var newCount = requestCount + 1;
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromDays(1)); // Key sẽ hết hạn sau 24 giờ
        _cache.Set(cacheKey, newCount, cacheEntryOptions);

        _logger.LogInformation("Forgot password request #{Count} for email: {Email}", newCount, request.Email);
        var newPassword = GenerateRandomPassword();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Password has been reset for user {Username} (Email: {Email})", user.Username, request.Email);
        try
        {
            var subject = "Yêu cầu cấp lại mật khẩu cho tài khoản SubPhim";
            var body = $@"
                <h3>Xin chào {user.Username},</h3>
                <p>Bạn đã yêu cầu cấp lại mật khẩu cho tài khoản SubPhim của mình.</p>
                <p>Đây là mật khẩu mới của bạn:</p>
                <h2 style='color: #0d6efd; letter-spacing: 2px; border: 1px solid #ddd; padding: 10px; background-color: #f8f9fa;'>{newPassword}</h2>
                <p>Vui lòng đăng nhập bằng mật khẩu này và đổi lại mật khẩu trong ứng dụng để đảm bảo an toàn.</p>
                <p>Nếu bạn không yêu cầu điều này, vui lòng bỏ qua email này.</p>
                <p>Trân trọng,<br>Đội ngũ SubPhim</p>";

            await _emailService.SendEmailAsync(request.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRITICAL: Password for user {Username} was reset, but failed to send notification email to {Email}", user.Username, request.Email);
        }

        return Ok(new { Message = "Nếu email của bạn chính xác là email đã đăng ký tài khoản, hãy kiểm tra hộp thư để lấy mật khẩu mới." });
    }

    private static string GenerateRandomPassword(int length = 4)
    {
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        var random = new Random();
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = validChars[random.Next(validChars.Length)];
        }
        return new string(chars);
    }
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    [HttpPost("reset-devices")]
    [Authorize]
    public async Task<IActionResult> ResetDevices()
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.Include(u => u.Devices).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }
        if (user.LastDeviceResetUtc.HasValue && user.LastDeviceResetUtc.Value.AddDays(1) > DateTime.UtcNow)
        {
            var timeRemaining = user.LastDeviceResetUtc.Value.AddDays(1) - DateTime.UtcNow;
            return StatusCode(429, $"Bạn chỉ có thể reset thiết bị mỗi 24 giờ. Vui lòng thử lại sau {timeRemaining.Hours} giờ {timeRemaining.Minutes} phút.");
        }

        if (user.Devices.Any())
        {
            _context.Devices.RemoveRange(user.Devices);
            _logger.LogInformation("User '{Username}' (ID: {UserId}) has reset all their devices.", user.Username, user.Id);
        }
        user.LastDeviceResetUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Đã xóa toàn bộ thiết bị của bạn thành công!" });
    }
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return BadRequest("Mật khẩu mới không hợp lệ. Yêu cầu ít nhất 6 ký tự.");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return BadRequest("Mật khẩu hiện tại không đúng.");
        }
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();
        _logger.LogInformation("User '{Username}' (ID: {UserId}) has changed their password successfully.", user.Username, user.Id);

        return Ok(new { Message = "Đổi mật khẩu thành công!" });
    }
    public record SrtTranslateCheckRequest(int LineCount);
    [HttpPost("pre-srt-translate-check")]
    [Authorize]
    public async Task<IActionResult> PreSrtTranslateCheck([FromBody] SrtTranslateCheckRequest request)
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }
        if (user.Tier != SubscriptionTier.Free)
        {
            return Ok(new { CanTranslate = true, RemainingLines = 99999, Message = "OK" });
        }
        var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
        var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastSrtLineResetUtc, vietnamTimeZone);

        if (lastResetInVietnam.Date < vietnamNow.Date)
        {
            user.SrtLinesUsedToday = 0;
            user.LastSrtLineResetUtc = DateTime.UtcNow.Date;
        }

        int remainingLines = user.DailySrtLineLimit - user.SrtLinesUsedToday;

        if (remainingLines <= 0)
        {
            return StatusCode(429, new
            {
                CanTranslate = false,
                RemainingLines = 0,
                Message = $"Bạn đã hết {user.DailySrtLineLimit} lượt dịch SRT trong ngày."
            });
        }

        if (request.LineCount > remainingLines)
        {
            return StatusCode(429, new
            {
                CanTranslate = false,
                RemainingLines = remainingLines,
                Message = $"Bạn chỉ còn {remainingLines} lượt dịch, không đủ để dịch {request.LineCount} dòng."
            });
        }

        // Nếu đủ, trừ đi và lưu lại
        user.SrtLinesUsedToday += request.LineCount;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            CanTranslate = true,
            RemainingLines = user.DailySrtLineLimit - user.SrtLinesUsedToday,
            Message = "OK"
        });
    }
    [HttpPost("report-violation")]
    [Authorize]
    public async Task<IActionResult> ReportViolation([FromBody] ViolationReport report)
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }

        user.IsBlocked = true;

        _logger.LogWarning(
            "Tự động khóa tài khoản. User: {Username} (ID: {UserId}), Lý do: {Reason}, HWID: {Hwid}",
            user.Username, user.Id, report.Reason, report.Hwid
        );

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (await _context.BannedDevices.AnyAsync(b => b.Hwid == request.Hwid))
        {
            return StatusCode(403, "Thiết bị của bạn đã bị cấm đăng ký tài khoản mới.");
        }

        if (await _context.Devices.AnyAsync(d => d.Hwid == request.Hwid))
        {
            return BadRequest("Mỗi thiết bị chỉ được phép đăng ký một tài khoản duy nhất.");
        }

        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
        {
            return BadRequest("Tên tài khoản đã tồn tại.");
        }
        if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
        {
            return BadRequest("Email đã được sử dụng.");
        }
        string newUid;
        var random = new Random();
        do
        {
            // Tạo một số ngẫu nhiên 9 chữ số
            newUid = random.Next(100_000_000, 1_000_000_000).ToString();
        }
        // Kiểm tra để đảm bảo UID là duy nhất trong DB
        while (await _context.Users.AnyAsync(u => u.Uid == newUid));
        var user = new User
        {
            Uid = newUid,
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            IsBlocked = false
        };
        _tierSettingsService.ApplyTierSettings(user, SubscriptionTier.Free);
        user.Devices.Add(new Device { Hwid = request.Hwid, LastLogin = DateTime.UtcNow });
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok("Đăng ký thành công!");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "IP không xác định";
        var cacheKey = $"login_fail_{clientIp}_{request.Username}";

        // 1. Kiểm tra giới hạn đăng nhập sai
        if (_cache.TryGetValue(cacheKey, out int failCount) && failCount >= 5)
        {
            return StatusCode(429, "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau 1 giờ.");
        }

        // 2. Kiểm tra HWID đã bị ban chưa (kiểm tra lại ở cả login)
        if (await _context.BannedDevices.AnyAsync(b => b.Hwid == request.Hwid))
        {
            return StatusCode(403, "Thiết bị của bạn đã bị cấm truy cập hệ thống.");
        }

        var user = await _context.Users
            .Include(u => u.Devices)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());

        // 3. Xử lý logic khi đăng nhập sai
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            var newFailCount = failCount + 1;
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(1));
            _cache.Set(cacheKey, newFailCount, cacheEntryOptions);

            _logger.LogWarning("Login failed for user {Username} from IP {IP}. Fail count: {Count}", request.Username, clientIp, newFailCount);
            return BadRequest("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        // Nếu đăng nhập thành công, xóa key cache
        _cache.Remove(cacheKey);

        if (user.IsBlocked)
        {
            return StatusCode(403, "Tài khoản của bạn đã bị khóa.");
        }

        if (user.Tier != SubscriptionTier.Free && user.Tier != SubscriptionTier.Lifetime && user.SubscriptionExpiry < DateTime.UtcNow)
        {
            _logger.LogInformation("Tài khoản '{Username}' (ID: {UserId}) đã hết hạn. Tự động chuyển về gói Free.", user.Username, user.Id);
            _tierSettingsService.ApplyTierSettings(user, SubscriptionTier.Free);
            await _context.SaveChangesAsync();
        }


        var device = user.Devices.FirstOrDefault(d => d.Hwid == request.Hwid);
        if (device == null)
        {
            if (user.Devices.Count >= user.MaxDevices)
            {
                // Thay vì báo lỗi, tìm và xóa thiết bị đăng nhập cũ nhất
                var oldestDevice = user.Devices.OrderBy(d => d.LastLogin).FirstOrDefault();
                if (oldestDevice != null)
                {
                    _context.Devices.Remove(oldestDevice);
                    _logger.LogInformation(
                        "User '{Username}' reached device limit ({Limit}). Removing oldest device (HWID: {OldHwid}, LastLogin: {LastLogin}) to add new device (HWID: {NewHwid}).",
                        user.Username, user.MaxDevices, oldestDevice.Hwid, oldestDevice.LastLogin, request.Hwid
                    );
                }
            }
            user.Devices.Add(new Device { Hwid = request.Hwid, LastLogin = DateTime.UtcNow, LastLoginIp = clientIp });
        }
        else
        {
            device.LastLogin = DateTime.UtcNow;
            device.LastLoginIp = clientIp;
        }

        await _context.SaveChangesAsync();
        var token = GenerateJwtToken(user);
        int dailyTranslationLimit;
        // Ưu tiên giá trị ghi đè trong DB trước tiên
        if (user.DailyRequestLimitOverride != -1)
        {
            dailyTranslationLimit = user.DailyRequestLimitOverride;
        }
        else // Nếu không có ghi đè, dùng mặc định theo gói
        {
            dailyTranslationLimit = user.Tier switch
            {
                SubscriptionTier.Free => 30,
                _ => 9999 // Các gói trả phí không giới hạn
            };
        }
        int remainingRequests = dailyTranslationLimit - user.DailyRequestCount;
                var tierSettings = await _context.TierDefaultSettings.FindAsync((int)user.Tier);
                // Ưu tiên giá trị ghi đè AioCharacterLimitOverride, nếu không có (-1) thì lấy từ TierDefaultSettings
                long aioCharacterLimit = user.AioCharacterLimitOverride != -1 
                    ? user.AioCharacterLimitOverride 
                    : (tierSettings?.AioCharacterLimit ?? 0);
                var userDto = new UserDto(
                    user.Id,
                    user.Username,
                    user.Uid,
                    user.Email,
                    user.Tier.ToString(),
                    user.SubscriptionExpiry ?? DateTime.MinValue,
                    token,
                    user.GrantedFeatures,
                    user.AllowedApiAccess,
                    Math.Max(0, remainingRequests),
                    user.VideosProcessedToday,
                    user.DailyVideoLimit,
                    user.Devices.OrderByDescending(d => d.LastLogin)
                                 .Select(d => new DeviceDto(d.Hwid, d.LastLoginIp, d.LastLogin)).ToList(),
                    user.SrtLinesUsedToday,
                    user.DailySrtLineLimit,
                    user.TtsCharactersUsed,
                    user.TtsCharacterLimit,
                    user.AioCharactersUsedToday,
                    aioCharacterLimit,
            user.LocalSrtLinesUsedToday,
            user.DailyLocalSrtLimit,
            user.VipSrtLinesUsedToday,
            user.DailyVipSrtLimit
        );

        return Ok(userDto);
    }

    [HttpGet("refresh-profile")]
    [Authorize]
    public async Task<IActionResult> RefreshProfile()
    {
        Debug.WriteLine("[AuthController.RefreshProfile] Received request to refresh user profile.");
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            Debug.WriteLine("[AuthController.RefreshProfile] Unauthorized: Invalid token, cannot parse user ID.");
            return Unauthorized("Token không hợp lệ.");
        }

        var user = await _context.Users
            .Include(u => u.Devices)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            Debug.WriteLine($"[AuthController.RefreshProfile] NotFound: User with ID {userId} not found.");
            return NotFound("Tài khoản không còn tồn tại.");
        }

        if (user.IsBlocked)
        {
            Debug.WriteLine($"[AuthController.RefreshProfile] Forbidden: User '{user.Username}' is blocked.");
            return StatusCode(403, "Tài khoản của bạn đã bị khóa.");
        }

        // --- BẮT ĐẦU LOGIC RESET TẬP TRUNG ---
        var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
        bool hasChanges = false;

        // 1. Reset bộ đếm Dịch Truyện
        var lastRequestResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastRequestResetUtc, vietnamTimeZone);
        if (lastRequestResetInVietnam.Date < vietnamNow.Date)
        {
            user.DailyRequestCount = 0;
            user.LastRequestResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting DailyRequestCount for user '{user.Username}'.");
        }

        // 2. Reset bộ đếm Xử lý Video
        var lastVideoResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastVideoResetUtc, vietnamTimeZone);
        if (lastVideoResetInVietnam.Date < vietnamNow.Date)
        {
            user.VideosProcessedToday = 0;
            user.LastVideoResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting VideosProcessedToday for user '{user.Username}'.");
        }

        // 3. Reset bộ đếm Dịch SRT
        var lastSrtLineResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastSrtLineResetUtc, vietnamTimeZone);
        if (lastSrtLineResetInVietnam.Date < vietnamNow.Date)
        {
            user.SrtLinesUsedToday = 0;
            user.LastSrtLineResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting SrtLinesUsedToday for user '{user.Username}'.");
        }

        // 4. Reset bộ đếm Dịch SRT Local
        var lastLocalSrtResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastLocalSrtResetUtc, vietnamTimeZone);
        if (lastLocalSrtResetInVietnam.Date < vietnamNow.Date)
        {
            user.LocalSrtLinesUsedToday = 0;
            user.LastLocalSrtResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting LocalSrtLinesUsedToday for user '{user.Username}'.");
        }
        
        // 5. Reset bộ đếm Dịch VIP SRT
        var lastVipSrtResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastVipSrtResetUtc, vietnamTimeZone);
        if (lastVipSrtResetInVietnam.Date < vietnamNow.Date)
        {
            user.VipSrtLinesUsedToday = 0;
            user.LastVipSrtResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting VipSrtLinesUsedToday for user '{user.Username}'.");
        }
        
        var lastTtsResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastTtsResetUtc, vietnamTimeZone);
        if (user.Tier == SubscriptionTier.Free && lastTtsResetInVietnam.Date < vietnamNow.Date)
        {
            user.TtsCharactersUsed = 0;
            user.LastTtsResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting TtsCharactersUsed for FREE user '{user.Username}'.");
        }
        var lastAioResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastAioResetUtc, vietnamTimeZone);
        if (lastAioResetInVietnam.Date < vietnamNow.Date)
        {
            user.AioCharactersUsedToday = 0;
            user.LastAioResetUtc = DateTime.UtcNow.Date;
            hasChanges = true;
            Debug.WriteLine($"[AuthController.RefreshProfile] Resetting AioCharactersUsedToday for user '{user.Username}'.");
        }
        if (hasChanges)
        {
            await _context.SaveChangesAsync();
            Debug.WriteLine($"[AuthController.RefreshProfile] Saved daily reset changes for user '{user.Username}'.");
        }
        var currentToken = HttpContext.Request.Headers["Authorization"]
                                      .ToString()
                                      .Replace("Bearer ", "");

        int dailyTranslationLimit;
        if (user.DailyRequestLimitOverride != -1) { dailyTranslationLimit = user.DailyRequestLimitOverride; }
        else { dailyTranslationLimit = user.Tier switch { SubscriptionTier.Free => 30, _ => 9999 }; }
        int remainingRequests = dailyTranslationLimit - user.DailyRequestCount;

        // Ưu tiên giá trị ghi đè AioCharacterLimitOverride, nếu không có (-1) thì lấy từ TierDefaultSettings
        var tierSettings = await _context.TierDefaultSettings.FindAsync((int)user.Tier);
        long aioCharacterLimit = user.AioCharacterLimitOverride != -1 
            ? user.AioCharacterLimitOverride 
            : (tierSettings?.AioCharacterLimit ?? 0);

        var userDto = new UserDto(
            user.Id,
            user.Username,
            user.Uid,
            user.Email,
            user.Tier.ToString(),
            user.SubscriptionExpiry ?? DateTime.MinValue,
            currentToken,
            user.GrantedFeatures,
            user.AllowedApiAccess,
            Math.Max(0, remainingRequests),
            user.VideosProcessedToday,
            user.DailyVideoLimit,
            user.Devices.OrderByDescending(d => d.LastLogin)
                         .Select(d => new DeviceDto(d.Hwid, d.LastLoginIp, d.LastLogin)).ToList(),
            user.SrtLinesUsedToday,
            user.DailySrtLineLimit,
            user.TtsCharactersUsed,
            user.TtsCharacterLimit,
            user.AioCharactersUsedToday,
             aioCharacterLimit,
    user.LocalSrtLinesUsedToday,
    user.DailyLocalSrtLimit,
    user.VipSrtLinesUsedToday,
    user.DailyVipSrtLimit
        );
        return Ok(userDto);
    }
    [HttpGet("usage-status")]
    [Authorize]
    public async Task<IActionResult> GetUsageStatus()
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }

        // Logic reset bộ đếm theo múi giờ Việt Nam
        var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
        var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastVideoResetUtc, vietnamTimeZone);

        if (lastResetInVietnam.Date < vietnamNow.Date)
        {
            user.VideosProcessedToday = 0;
            user.LastVideoResetUtc = DateTime.UtcNow.Date;
            await _context.SaveChangesAsync();
        }
        bool canProcess = true;
        string message = "Bạn có thể xử lý video mới.";
        if (user.DailyVideoLimit != -1 && user.VideosProcessedToday >= user.DailyVideoLimit)
        {
            canProcess = false;
            message = $"Đã đạt giới hạn {user.DailyVideoLimit} video/ngày.";
        }

        var nextResetUtc = user.LastVideoResetUtc.AddDays(1);

        var remaining = (user.DailyVideoLimit == -1) ? 99999 : user.DailyVideoLimit - user.VideosProcessedToday;

        var status = new UsageStatusDto(
            CanProcessNewVideo: canProcess,
            RemainingVideosToday: Math.Max(0, remaining),
            MaxVideoDurationMinutes: user.VideoDurationLimitMinutes, 
            LimitResetTimeUtc: nextResetUtc,
            Message: message
        );

        return Ok(status);
    }


    [HttpPost("start-processing")]
    [Authorize]
    public async Task<IActionResult> StartVideoProcessing()
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound("Không tìm thấy người dùng.");
        }
        var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
        var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(user.LastVideoResetUtc, vietnamTimeZone);

        if (lastResetInVietnam.Date < vietnamNow.Date)
        {
            user.VideosProcessedToday = 0; // Reset bộ đếm nếu đã sang ngày mới
            user.LastVideoResetUtc = DateTime.UtcNow.Date;
        }
        if (user.DailyVideoLimit != -1 && user.VideosProcessedToday >= user.DailyVideoLimit)
        {
            _logger.LogWarning("User '{Username}' (ID: {UserId}) was denied video processing. Limit reached: {Processed}/{Limit}",
                                user.Username, user.Id, user.VideosProcessedToday, user.DailyVideoLimit);

            return StatusCode(429, $"Bạn đã đạt giới hạn {user.DailyVideoLimit} cho hôm nay. Nâng cấp gói để mở khoá giới hạn.");
        }
        user.VideosProcessedToday++;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User '{Username}' (ID: {UserId}) granted video processing. New count: {Count}/{Limit}",
                                user.Username, user.Id, user.VideosProcessedToday, user.DailyVideoLimit);
        return Ok(new { Message = "Bắt đầu..." });
    }


    [HttpPost("pre-translate-check")]
    [Authorize]
    public async Task<IActionResult> PreTranslateCheck([FromBody] PreTranslateCheckRequest request)
    {
            var userIdString = User.FindFirstValue("id");
            if (!int.TryParse(userIdString, out int userId))
            {
                return Unauthorized();
            }
            var userInDb = await _context.Users.FindAsync(userId);
            if (userInDb == null)
            {
                return NotFound("Không tìm thấy người dùng.");
            }

            if (!Enum.TryParse<ApiProviderType>(request.Provider, true, out var requestedProvider))
            {
                return BadRequest("Tên nhà cung cấp API không hợp lệ.");
            }

            bool isAllowed = false;
            var allowedApisForUser = userInDb.AllowedApiAccess;

            if (allowedApisForUser != AllowedApis.None)
            {
                isAllowed = requestedProvider switch
                {
                    ApiProviderType.ChutesAI => allowedApisForUser.HasFlag(AllowedApis.ChutesAI),
                    ApiProviderType.Gemini => allowedApisForUser.HasFlag(AllowedApis.Gemini),
                    ApiProviderType.OpenRouter => allowedApisForUser.HasFlag(AllowedApis.OpenRouter),
                    _ => false
                };
            }
            else
            {
                if (userInDb.Tier == SubscriptionTier.Lifetime) { isAllowed = true; }
                else if (userInDb.Tier == SubscriptionTier.Free) { isAllowed = (requestedProvider == ApiProviderType.OpenRouter); }
                else { isAllowed = true; }
            }

            if (!isAllowed)
            {
                _logger.LogWarning("User {Username} (Tier: {Tier}) bị từ chối truy cập API {Provider}.", userInDb.Username, userInDb.Tier, requestedProvider);
                return StatusCode(403, $"Gói dịch vụ hoặc quyền của bạn không hỗ trợ API '{requestedProvider}'.");
            }

            int dailyLimit = userInDb.DailyRequestLimitOverride;
            if (dailyLimit == -1)
            {
                dailyLimit = userInDb.Tier switch
                {
                    SubscriptionTier.Free => 30,
                    _ => int.MaxValue
                };
            }

            if (dailyLimit == int.MaxValue || userInDb.Tier == SubscriptionTier.Lifetime)
            {
                await _context.SaveChangesAsync();
                return Ok(new { RemainingRequests = 9999 });
            }

            var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone);
            var lastResetInVietnam = TimeZoneInfo.ConvertTimeFromUtc(userInDb.LastRequestResetUtc, vietnamTimeZone);

            if (lastResetInVietnam.Date < vietnamNow.Date)
            {
                userInDb.DailyRequestCount = 0;
                userInDb.LastRequestResetUtc = DateTime.UtcNow;
            }

            if (userInDb.DailyRequestCount >= dailyLimit)
            {
                return StatusCode(429, $"Bạn đã hết {dailyLimit} lượt dịch trong ngày.");
            }

            userInDb.DailyRequestCount++;
            await _context.SaveChangesAsync();

            return Ok(new { RemainingRequests = dailyLimit - userInDb.DailyRequestCount });
        }

   
    public record PreTranslateCheckRequest(string Provider);

    
    public enum ApiProviderType
    {
        ChutesAI,
        Gemini,
        OpenRouter
    }
    [HttpPost("check-api-access")]
    [Authorize] // Chỉ người dùng đã đăng nhập mới được gọi
    public async Task<IActionResult> CheckApiAccess([FromBody] ApiAccessRequest request)
    {
        var userIdString = User.FindFirstValue("id");
        if (!int.TryParse(userIdString, out int userId))
        {
            return Unauthorized();
        }

        // Luôn lấy thông tin mới nhất từ DB để đảm bảo quyền là chính xác
        var userInDb = await _context.Users.FindAsync(userId);
        if (userInDb == null)
        {
            return NotFound("Tài khoản không tồn tại.");
        }

        // Chuyển đổi tên API từ chuỗi sang enum
        if (!Enum.TryParse<AllowedApis>(request.ApiName, true, out var apiToCheck))
        {
            return BadRequest($"Tên API không hợp lệ: {request.ApiName}");
        }

        // Kiểm tra quyền
        bool hasAccess = userInDb.AllowedApiAccess.HasFlag(apiToCheck);

        if (hasAccess)
        {
            return Ok(new { HasAccess = true, Message = "OK" });
        }
        else
        {
            return Ok(new { HasAccess = false, Message = $"Bạn không có quyền truy cập API '{request.ApiName}'. Vui lòng liên hệ admin." });
        }
    }
    public record ApiAccessRequest(string ApiName);
    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var keyString = _configuration["Jwt:Key"] ?? "SubPhim-Super-Secret-Key-For-JWT-Authentication-2024-@!#$";
        var key = Encoding.ASCII.GetBytes(keyString);

        var claims = new List<Claim>
    {
        new Claim("id", user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Tier.ToString()),
        new Claim("features", ((int)user.GrantedFeatures).ToString()),
        new Claim("allowedApis", ((int)user.AllowedApiAccess).ToString())
    };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

}
