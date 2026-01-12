
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using PagedList.Core;
using SubPhim.Server.Utils;

namespace SubPhim.Server.Pages.Admin.Users
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ITierSettingsService _tierSettingsService;
        public IndexModel(AppDbContext context, ITierSettingsService tierSettingsService)
        {
            _context = context;
            _tierSettingsService = tierSettingsService;
        }

        [BindProperty(SupportsGet = true)]
        public string? SearchString { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string Tab { get; set; } = "premium";

        public IPagedList<UserViewModel>? DisplayUsers { get; set; }

        public int PremiumUserCount { get; set; }
        public int MonthlyUserCount { get; set; }
        public int FreeUserCount { get; set; }
        [HttpGet]
        public async Task<IActionResult> OnGetTierDefaultsAsync(SubscriptionTier tier, bool? isPro = null)
        {
            // For Yearly tier, fetch the appropriate settings based on isPro parameter
            TierDefaultSetting? settings = null;
            
            if (tier == SubscriptionTier.Yearly && isPro.HasValue)
            {
                // Fetch settings matching the IsYearlyProSettings flag
                settings = await _context.TierDefaultSettings.AsNoTracking()
                    .Where(s => s.Tier == tier && s.IsYearlyProSettings == isPro.Value)
                    .FirstOrDefaultAsync();
                
                // If not found (e.g., admin hasn't created Pro settings yet), fall back to Standard settings (non-Pro)
                if (settings == null && isPro.Value)
                {
                    settings = await _context.TierDefaultSettings.AsNoTracking()
                        .Where(s => s.Tier == tier && !s.IsYearlyProSettings)
                        .FirstOrDefaultAsync();
                }
            }
            else
            {
                // For non-Yearly tiers or when isPro is not specified, just get any setting for that tier
                settings = await _context.TierDefaultSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Tier == tier);
            }

            if (settings == null)
            {
                return NotFound(new { message = "Không tìm thấy cấu hình mặc định cho gói này." });
            }
            var features = Enum.GetValues<GrantedFeatures>()
                .Where(f => f != GrantedFeatures.None && settings.GrantedFeatures.HasFlag(f))
                .Select(f => f.ToString())
                .ToList();

            var apis = Enum.GetValues<AllowedApis>()
                .Where(a => a != AllowedApis.None && settings.AllowedApis.HasFlag(a))
                .Select(a => a.ToString())
                .ToList();

            return new JsonResult(new
            {
                settings.VideoDurationMinutes,
                settings.DailyVideoCount,
                settings.DailyTranslationRequests,
                settings.DailySrtLineLimit,
                settings.DailyLocalSrtLimit,
                // === BẮT ĐẦU THÊM MỚI ===
                settings.AioCharacterLimit,
                settings.AioRequestsPerMinute,
                settings.TtsCharacterLimit,
                settings.DailyVipSrtLimit,
                // === KẾT THÚC THÊM MỚI ===
                AllowedApis = apis,
                GrantedFeatures = features
            });
        }
        public List<string> AllFeatureNames => Enum.GetNames(typeof(GrantedFeatures)).Where(f => f != "None").ToList();
        public List<string> AllApiNames => Enum.GetNames(typeof(AllowedApis)).Where(a => a != "None").ToList();

        private const int PageSize = 20;

        public async Task OnGetAsync()
        {
            IQueryable<User> baseQuery = _context.Users.AsNoTracking().Include(u => u.Devices);

            if (!string.IsNullOrEmpty(SearchString))
            {
                var searchLower = SearchString.ToLower();
                var searchQuery = baseQuery.Where(u => u.Username.ToLower().Contains(searchLower) ||
                                                     (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                                                     u.Uid.Contains(SearchString));
                await PaginateQuery(searchQuery);
            }
            else
            {
                PremiumUserCount = await baseQuery.CountAsync(u => u.Tier == SubscriptionTier.Yearly || u.Tier == SubscriptionTier.Lifetime);
                MonthlyUserCount = await baseQuery.CountAsync(u => u.Tier == SubscriptionTier.Monthly || u.Tier == SubscriptionTier.Daily);
                FreeUserCount = await baseQuery.CountAsync(u => u.Tier == SubscriptionTier.Free);

                IQueryable<User> tabQuery;
                switch (Tab)
                {
                    case "monthly":
                        tabQuery = baseQuery.Where(u => u.Tier == SubscriptionTier.Monthly || u.Tier == SubscriptionTier.Daily);
                        break;
                    case "free":
                        tabQuery = baseQuery.Where(u => u.Tier == SubscriptionTier.Free);
                        break;
                    default:
                        tabQuery = baseQuery.Where(u => u.Tier == SubscriptionTier.Yearly || u.Tier == SubscriptionTier.Lifetime);
                        break;
                }
                await PaginateQuery(tabQuery);
            }
        }

        private async Task PaginateQuery(IQueryable<User> query)
        {
            var totalItemCount = await query.CountAsync();
            var usersForCurrentPage = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            // Lấy tất cả cài đặt tier một lần để tối ưu
            var tierSettings = await _context.TierDefaultSettings.ToDictionaryAsync(ts => ts.Tier);

            var userViewModels = usersForCurrentPage.Select(u =>
            {
                var currentTierSettings = tierSettings.GetValueOrDefault(u.Tier);

                // Xác định giới hạn ký tự AIO: ưu tiên override, sau đó đến tier default
                long aioCharLimit = u.AioCharacterLimitOverride != -1
                    ? u.AioCharacterLimitOverride
                    : (currentTierSettings?.AioCharacterLimit ?? 0);

                // Xác định giới hạn RPM AIO: ưu tiên override, sau đó đến tier default
                int aioRpm = u.AioRpmOverride != -1
                    ? u.AioRpmOverride
                    : (currentTierSettings?.AioRequestsPerMinute ?? 0);

                return new UserViewModel
                {
                    Id = u.Id,
                    Uid = u.Uid,
                    Username = u.Username,
                    Email = u.Email,
                    Tier = u.Tier.ToString(),
                    SubscriptionExpiry = u.SubscriptionExpiry,
                    IsBlocked = u.IsBlocked,
                    IsYearlyPro = u.IsYearlyPro,
                    VideosProcessedToday = u.VideosProcessedToday,
                    DailyVideoLimit = u.DailyVideoLimit,
                    DailyRequestCount = u.DailyRequestCount,
                    DailyRequestLimitOverride = u.DailyRequestLimitOverride,
                    AllowedApiAccess = u.AllowedApiAccess,
                    GrantedFeatures = u.GrantedFeatures,
                    LastLogin = u.Devices.Any() ? u.Devices.Max(d => d.LastLogin) : (DateTime?)null,
                    SrtLinesUsedToday = u.SrtLinesUsedToday,
                    DailySrtLineLimit = u.DailySrtLineLimit,
                    DailyLocalSrtLimit = u.DailyLocalSrtLimit,
                    TtsCharactersUsed = u.TtsCharactersUsed,
                    TtsCharacterLimit = u.TtsCharacterLimit,
                    // === BẮT ĐẦU THÊM MỚI ===
                    AioCharactersUsedToday = u.AioCharactersUsedToday,
                    AioCharacterLimit = aioCharLimit,
                    AioRpm = aioRpm,
                    DailyVipSrtLimit = u.DailyVipSrtLimit
                    // === KẾT THÚC THÊM MỚI ===
                };
            }).ToList();

            DisplayUsers = new StaticPagedList<UserViewModel>(userViewModels, PageNumber, PageSize, totalItemCount);
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostResetAllDevicesAsync()
        {
            try
            {
                await _context.Devices.ExecuteDeleteAsync();
                TempData["SuccessMessage"] = "Thao tác thành công! Đã xóa toàn bộ thiết bị khỏi hệ thống.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Đã có lỗi xảy ra: " + ex.Message;
            }
            return RedirectToPage();
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostAddSubTimeAsync(int id, int days)
        {
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            DateTime startTime = (userToUpdate.SubscriptionExpiry.HasValue && userToUpdate.SubscriptionExpiry.Value > DateTime.UtcNow)
                ? userToUpdate.SubscriptionExpiry.Value
                : DateTime.UtcNow;

            userToUpdate.SubscriptionExpiry = startTime.AddDays(days);
            await _context.SaveChangesAsync();
            return new JsonResult(new { message = $"Đã thêm thành công {days} ngày.", newExpiry = userToUpdate.SubscriptionExpiry });
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostSubtractSubTimeAsync(int id, int days)
        {
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            if (!userToUpdate.SubscriptionExpiry.HasValue || userToUpdate.SubscriptionExpiry.Value <= DateTime.UtcNow)
            {
                return BadRequest(new { message = "Tài khoản đã hết hạn hoặc không có gói để trừ." });
            }

            var newExpiryDate = userToUpdate.SubscriptionExpiry.Value.AddDays(-days);
            string successMessage;

            if (newExpiryDate <= DateTime.UtcNow)
            {
                userToUpdate.SubscriptionExpiry = null;
                _tierSettingsService.ApplyTierSettings(userToUpdate, SubscriptionTier.Free);
                successMessage = $"Đã trừ {days} ngày. Tài khoản đã hết hạn và được chuyển về gói Free.";
            }
            else
            {
                userToUpdate.SubscriptionExpiry = newExpiryDate;
                successMessage = $"Đã trừ thành công {days} ngày.";
            }

            await _context.SaveChangesAsync();
            return new JsonResult(new { message = successMessage, newExpiry = userToUpdate.SubscriptionExpiry, newTier = userToUpdate.Tier.ToString() });
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostResetDevicesAsync(int id)
        {
            var devices = await _context.Devices.Where(d => d.UserId == id).ToListAsync();
            if (devices.Any())
            {
                _context.Devices.RemoveRange(devices);
                await _context.SaveChangesAsync();
            }
            return new JsonResult(new { message = "Đã xóa toàn bộ thiết bị của người dùng." });
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostBanUserAndDevicesAsync(int id)
        {
            var userToBan = await _context.Users.Include(u => u.Devices).FirstOrDefaultAsync(u => u.Id == id);
            if (userToBan == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            userToBan.IsBlocked = true;

            foreach (var device in userToBan.Devices)
            {
                if (!await _context.BannedDevices.AnyAsync(b => b.Hwid == device.Hwid))
                {
                    _context.BannedDevices.Add(new BannedDevice
                    {
                        Hwid = device.Hwid,
                        LastKnownIp = device.LastLoginIp,
                        AssociatedUsername = userToBan.Username,
                        BanReason = $"Bị cấm cùng với tài khoản '{userToBan.Username}' từ modal.",
                        BannedAt = DateTime.UtcNow
                    });
                }
            }
            await _context.SaveChangesAsync();
            return new JsonResult(new { message = $"Đã cấm tài khoản '{userToBan.Username}' và {userToBan.Devices.Count} thiết bị liên quan." });
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostUpdatePermissionsAsync([FromForm] UpdatePermissionsRequest request)
        {
            var userInDb = await _context.Users.FindAsync(request.Id);
            if (userInDb == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            if (userInDb.Tier != request.Tier)
            {
                _tierSettingsService.ApplyTierSettings(userInDb, request.Tier);
                DateTime startTime = DateTime.UtcNow;
                switch (request.Tier)
                {
                    case SubscriptionTier.Daily: userInDb.SubscriptionExpiry = startTime.AddDays(1); break;
                    case SubscriptionTier.Monthly: userInDb.SubscriptionExpiry = startTime.AddMonths(1); break;
                    case SubscriptionTier.Yearly: userInDb.SubscriptionExpiry = startTime.AddYears(1); break;
                    case SubscriptionTier.Lifetime: userInDb.SubscriptionExpiry = startTime.AddYears(100); break;
                }
            }

            userInDb.GrantedFeatures = request.Features?.Aggregate(GrantedFeatures.None, (current, featureName) =>
                Enum.TryParse<GrantedFeatures>(featureName, out var feature) ? current | feature : current) ?? GrantedFeatures.None;

            userInDb.AllowedApiAccess = request.Apis?.Aggregate(AllowedApis.None, (current, apiName) =>
                Enum.TryParse<AllowedApis>(apiName, out var api) ? current | api : current) ?? AllowedApis.None;

            userInDb.DailyVideoLimit = request.DailyVideoLimit;
            userInDb.DailyRequestLimitOverride = request.DailyRequestLimitOverride;
            userInDb.DailySrtLineLimit = request.DailySrtLineLimit;
            userInDb.DailyLocalSrtLimit = request.DailyLocalSrtLimit;

            // === BẮT ĐẦU THÊM MỚI ===
            userInDb.AioCharacterLimitOverride = request.AioCharacterLimitOverride;
            userInDb.AioRpmOverride = request.AioRpmOverride;
            userInDb.TtsCharacterLimit = request.TtsCharacterLimit;
            userInDb.DailyVipSrtLimit = request.DailyVipSrtLimit;
            userInDb.IsYearlyPro = request.IsYearlyPro;
            // === KẾT THÚC THÊM MỚI ===

            await _context.SaveChangesAsync();
            return new JsonResult(new { message = "Cập nhật thành công!" });
        }
    }
    public class UpdatePermissionsRequest
    {
        public int Id { get; set; }
        public List<string>? Features { get; set; }
        public List<string>? Apis { get; set; }
        public SubscriptionTier Tier { get; set; }
        public int DailyVideoLimit { get; set; }
        public int DailyRequestLimitOverride { get; set; }
        public int DailyLocalSrtLimit { get; set; }
        public int DailySrtLineLimit { get; set; }

        // === BẮT ĐẦU THÊM MỚI ===
        public long AioCharacterLimitOverride { get; set; }
        public int AioRpmOverride { get; set; }
        public long TtsCharacterLimit { get; set; }
        public int DailyVipSrtLimit { get; set; }
        public bool IsYearlyPro { get; set; }
        // === KẾT THÚC THÊM MỚI ===
    }

    public class UserViewModel
    {
        public int Id { get; set; }
        public string Uid { get; set; }
        public string Username { get; set; }
        public string? Email { get; set; }
        public string Tier { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsYearlyPro { get; set; }
        public DateTime? LastLogin { get; set; }
        public int VideosProcessedToday { get; set; }
        public int DailyVideoLimit { get; set; }
        public int DailyRequestCount { get; set; }
        public int DailyRequestLimitOverride { get; set; }
        public AllowedApis AllowedApiAccess { get; set; }
        public GrantedFeatures GrantedFeatures { get; set; }
        public int DailyLocalSrtLimit { get; set; }
        public int SrtLinesUsedToday { get; set; }
        public int DailySrtLineLimit { get; set; }
        public long TtsCharactersUsed { get; set; }
        public long TtsCharacterLimit { get; set; }

        // === BẮT ĐẦU THÊM MỚI ===
        public long AioCharactersUsedToday { get; set; }
        public long AioCharacterLimit { get; set; }
        public int AioRpm { get; set; }
        public int DailyVipSrtLimit { get; set; }
        // === KẾT THÚC THÊM MỚI ===
    }
}