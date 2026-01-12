using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SubPhim.Server.Services;

namespace SubPhim.Server.Pages.Admin.Users
{
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DetailsModel> _logger;
        private readonly ITierSettingsService _tierSettingsService;
        public DetailsModel(AppDbContext context, ILogger<DetailsModel> logger, ITierSettingsService tierSettingsService) 
        {
            _context = context;
            _logger = logger;
            _tierSettingsService = tierSettingsService; 
        }

        [BindProperty]
        public User User { get; set; } = default!;

        [BindProperty]
        [Display(Name = "Số ngày muốn thêm")]
        public int DaysToAdd { get; set; } = 30;

        [BindProperty]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null) return NotFound();

            User = await _context.Users.Include(u => u.Devices).FirstOrDefaultAsync(m => m.Id == id);

            if (User == null) return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            var userInDb = await _context.Users.FindAsync(User.Id);

            if (userInDb == null)
            {
                return NotFound();
            }

            var oldTier = userInDb.Tier;
            var newTier = User.Tier;

            userInDb.Email = User.Email;
            userInDb.MaxDevices = User.MaxDevices;
            userInDb.IsBlocked = User.IsBlocked;
            userInDb.DailyRequestLimitOverride = User.DailyRequestLimitOverride;
            userInDb.VideosProcessedToday = User.VideosProcessedToday;
            userInDb.VideoDurationLimitMinutes = User.VideoDurationLimitMinutes;
            userInDb.DailyVideoLimit = User.DailyVideoLimit;
            userInDb.DailySrtLineLimit = User.DailySrtLineLimit;
            userInDb.SrtLinesUsedToday = User.SrtLinesUsedToday;
            userInDb.TtsCharacterLimit = User.TtsCharacterLimit;
            userInDb.TtsCharactersUsed = User.TtsCharactersUsed;
            var selectedApis = AllowedApis.None;
            if (Request.Form.ContainsKey("allowedApis"))
            {
                foreach (var apiName in Request.Form["allowedApis"])
                {
                    // SỬA LỖI: Gọi trực tiếp 'AllowedApis'
                    if (Enum.TryParse<AllowedApis>(apiName, out var api))
                    {
                        selectedApis |= api;
                    }
                }
            }
            userInDb.AllowedApiAccess = selectedApis;

            var selectedFeatures = GrantedFeatures.None;
            if (Request.Form.ContainsKey("features"))
            {
                foreach (var featureName in Request.Form["features"])
                {
                    if (Enum.TryParse<GrantedFeatures>(featureName, out var feature))
                    {
                        selectedFeatures |= feature;
                    }
                }
            }
            userInDb.GrantedFeatures = selectedFeatures;

            if (oldTier != newTier)
            {
                // 1. ÁP DỤNG TOÀN BỘ CÀI ĐẶT MẶC ĐỊNH CỦA TIER MỚI
                _tierSettingsService.ApplyTierSettings(userInDb, newTier);

                // 2. Đặt lại ngày hết hạn dựa trên tier mới
                DateTime startTime = DateTime.UtcNow;
                switch (newTier)
                {
                    case SubscriptionTier.Daily: userInDb.SubscriptionExpiry = startTime.AddDays(1); break;
                    case SubscriptionTier.Monthly: userInDb.SubscriptionExpiry = startTime.AddMonths(1); break;
                    case SubscriptionTier.Yearly: userInDb.SubscriptionExpiry = startTime.AddYears(1); break;
                    case SubscriptionTier.Lifetime: userInDb.SubscriptionExpiry = startTime.AddYears(100); break;
                        // Gói Free đã được xử lý trong ApplyTierSettings
                }
            }
            else if (User.SubscriptionExpiry != userInDb.SubscriptionExpiry)
            {
                // Chỉ cập nhật ngày hết hạn nếu tier không đổi
                userInDb.SubscriptionExpiry = User.SubscriptionExpiry;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật thông tin người dùng thành công!";
            return RedirectToPage("./Details", new { id = User.Id });
        }
        public async Task<IActionResult> OnPostSubtractSubTimeAsync(int id)
        {
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            // Nếu user không có ngày hết hạn hoặc đã hết hạn, không thể trừ
            if (!userToUpdate.SubscriptionExpiry.HasValue || userToUpdate.SubscriptionExpiry.Value <= DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "Không thể trừ ngày vì tài khoản đã hết hạn hoặc không có gói.";
                return RedirectToPage("./Details", new { id = id });
            }

            // Tính toán ngày hết hạn mới
            var newExpiryDate = userToUpdate.SubscriptionExpiry.Value.AddDays(-DaysToAdd);

            // Nếu sau khi trừ, ngày hết hạn rơi vào quá khứ hoặc hiện tại
            if (newExpiryDate <= DateTime.UtcNow)
            {
                // Set ngày hết hạn về null và chuyển họ về gói Free
                userToUpdate.SubscriptionExpiry = null;
                userToUpdate.Tier = SubscriptionTier.Free;
                TempData["SuccessMessage"] = $"Đã trừ {DaysToAdd} ngày. Tài khoản đã hết hạn và được chuyển về gói Free.";
            }
            else
            {
                // Nếu vẫn còn hạn, chỉ cập nhật ngày
                userToUpdate.SubscriptionExpiry = newExpiryDate;
                TempData["SuccessMessage"] = $"Đã trừ thành công {DaysToAdd} ngày sử dụng!";
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("./Details", new { id = id });
        }

        public async Task<IActionResult> OnPostBanUserAsync(int id)
        {
            // 1. Tải thông tin người dùng và CẢ các thiết bị của họ
            var userToBan = await _context.Users
                .Include(u => u.Devices) // Rất quan trọng: Phải Include để lấy được HWID
                .FirstOrDefaultAsync(u => u.Id == id);

            if (userToBan == null)
            {
                return NotFound();
            }

            // 2. Chặn tài khoản người dùng
            userToBan.IsBlocked = true;
            _logger.LogInformation("Tài khoản '{Username}' đã bị chặn bởi admin.", userToBan.Username);

            // 3. Lặp qua tất cả các thiết bị đã biết của người dùng và cấm chúng
            foreach (var device in userToBan.Devices)
            {
                // Kiểm tra xem HWID này đã bị cấm trước đó chưa để tránh trùng lặp
                bool isAlreadyBanned = await _context.BannedDevices.AnyAsync(b => b.Hwid == device.Hwid);
                if (!isAlreadyBanned)
                {
                    var newBan = new BannedDevice
                    {
                        Hwid = device.Hwid,
                        LastKnownIp = device.LastLoginIp, // Lấy IP cuối cùng đã biết
                        AssociatedUsername = userToBan.Username,
                        BanReason = $"Bị cấm cùng với tài khoản '{userToBan.Username}' bởi admin.",
                        BannedAt = DateTime.UtcNow
                    };
                    _context.BannedDevices.Add(newBan);
                    _logger.LogInformation("Thiết bị HWID '{Hwid}' đã bị thêm vào danh sách cấm.", device.Hwid);
                }
            }

            // 4. Lưu tất cả thay đổi vào database
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã cấm thành công tài khoản '{userToBan.Username}' và {userToBan.Devices.Count} thiết bị liên quan!";
            return RedirectToPage("./Details", new { id = id });
        }
        public async Task<IActionResult> OnPostAddSubTimeAsync(int id)
        {
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            // Nếu người dùng đang là Free, hoặc gói đã hết hạn, thời gian mới sẽ bắt đầu từ BÂY GIỜ.
            // Ngược lại, nếu gói vẫn còn hạn, thời gian mới sẽ được CỘNG DỒN vào ngày hết hạn hiện tại.
            DateTime startTime = (userToUpdate.SubscriptionExpiry.HasValue && userToUpdate.SubscriptionExpiry.Value > DateTime.UtcNow)
                ? userToUpdate.SubscriptionExpiry.Value
                : DateTime.UtcNow;

            userToUpdate.SubscriptionExpiry = startTime.AddDays(DaysToAdd);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã thêm thành công {DaysToAdd} ngày sử dụng!";
            return RedirectToPage("./Details", new { id = id });
        }

        // --- CÁC HÀNH ĐỘNG KHÁC GIỮ NGUYÊN ---

        public async Task<IActionResult> OnPostResetPasswordAsync(int id)
        {
            if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "Mật khẩu mới không hợp lệ! (Yêu cầu ít nhất 6 ký tự)";
                return RedirectToPage("./Details", new { id = id });
            }

            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            userToUpdate.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Reset mật khẩu thành công!";
            return RedirectToPage("./Details", new { id = id });
        }

        public async Task<IActionResult> OnPostResetDevicesAsync(int id)
        {
            var devices = await _context.Devices.Where(d => d.UserId == id).ToListAsync();
            if (devices.Any())
            {
                _context.Devices.RemoveRange(devices);
                await _context.SaveChangesAsync();
            }
            TempData["SuccessMessage"] = "Đã xóa toàn bộ thiết bị của người dùng này!";
            return RedirectToPage("./Details", new { id = id });
        }

        public async Task<IActionResult> OnPostDeleteUserAsync(int id)
        {
            var userToDelete = await _context.Users.Include(u => u.Devices).FirstOrDefaultAsync(u => u.Id == id);
            if (userToDelete == null) return NotFound();

            _context.Users.Remove(userToDelete);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã xóa vĩnh viễn người dùng '{userToDelete.Username}'!";
            return RedirectToPage("./Index");
        }
    }
}