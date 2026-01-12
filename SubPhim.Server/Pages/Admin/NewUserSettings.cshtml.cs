using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin
{
    public class NewUserSettingsModel : PageModel
    {
        private readonly AppDbContext _context;

        public NewUserSettingsModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public TierSettingsViewModel FreeTierSettings { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public class TierSettingsViewModel
        {
            [Display(Name = "Giới hạn Video / Ngày")]
            public int DailyVideoCount { get; set; }

            [Display(Name = "Giới hạn Dịch Truyện / Ngày")]
            public int DailyTranslationRequests { get; set; }

            [Display(Name = "Giới hạn Dịch SRT (API bên thứ 3) / Ngày")]
            public int DailySrtLineLimit { get; set; }

            [Display(Name = "Giới hạn Dịch SRT (Local API Server) / Ngày")]
            public int DailyLocalSrtLimit { get; set; }

            [Display(Name = "Quyền Truy Cập API")]
            public AllowedApis AllowedApiAccess { get; set; }

            [Display(Name = "Quyền Truy Cập Tính Năng")]
            public GrantedFeatures GrantedFeatures { get; set; }

            [Display(Name = "Giới hạn Ký tự TTS / Ngày")]
            public long TtsCharacterLimit { get; set; }

            // === BẮT ĐẦU THÊM MỚI ===
            [Display(Name = "Giới hạn Ký tự AIO / Ngày")]
            public long AioCharacterLimit { get; set; }

            [Display(Name = "Giới hạn Request AIO / Phút")]
            public int AioRequestsPerMinute { get; set; }
            
            [Display(Name = "Giới hạn Dịch VIP SRT / Ngày")]
            public int DailyVipSrtLimit { get; set; }
            // === KẾT THÚC THÊM MỚI ===
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var freeSettings = await _context.TierDefaultSettings.FirstOrDefaultAsync(t => t.Tier == SubscriptionTier.Free);

            if (freeSettings == null)
            {
                // Khởi tạo giá trị mặc định nếu chưa có trong DB
                freeSettings = new TierDefaultSetting
                {
                    Tier = SubscriptionTier.Free,
                    AioCharacterLimit = 5000,
                    AioRequestsPerMinute = 3
                };
            }

            FreeTierSettings = new TierSettingsViewModel
            {
                DailyVideoCount = freeSettings.DailyVideoCount,
                DailyTranslationRequests = freeSettings.DailyTranslationRequests,
                DailySrtLineLimit = freeSettings.DailySrtLineLimit,
                DailyLocalSrtLimit = freeSettings.DailyLocalSrtLimit,
                AllowedApiAccess = freeSettings.AllowedApiAccess,
                GrantedFeatures = freeSettings.GrantedFeatures,
                TtsCharacterLimit = freeSettings.TtsCharacterLimit,
                // === BẮT ĐẦU THÊM MỚI ===
                AioCharacterLimit = freeSettings.AioCharacterLimit,
                AioRequestsPerMinute = freeSettings.AioRequestsPerMinute,
                DailyVipSrtLimit = freeSettings.DailyVipSrtLimit
                // === KẾT THÚC THÊM MỚI ===
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var freeSettingsInDb = await _context.TierDefaultSettings.FirstOrDefaultAsync(t => t.Tier == SubscriptionTier.Free);
            if (freeSettingsInDb == null)
            {
                ErrorMessage = "Lỗi nghiêm trọng: Không tìm thấy cấu hình 'Free' để cập nhật.";
                return Page();
            }

            try
            {
                freeSettingsInDb.DailyVideoCount = FreeTierSettings.DailyVideoCount;
                freeSettingsInDb.DailyTranslationRequests = FreeTierSettings.DailyTranslationRequests;
                freeSettingsInDb.DailySrtLineLimit = FreeTierSettings.DailySrtLineLimit;
                freeSettingsInDb.DailyLocalSrtLimit = FreeTierSettings.DailyLocalSrtLimit;
                freeSettingsInDb.AllowedApiAccess = FreeTierSettings.AllowedApiAccess;
                freeSettingsInDb.GrantedFeatures = FreeTierSettings.GrantedFeatures;
                freeSettingsInDb.TtsCharacterLimit = FreeTierSettings.TtsCharacterLimit;
                // === BẮT ĐẦU THÊM MỚI ===
                freeSettingsInDb.AioCharacterLimit = FreeTierSettings.AioCharacterLimit;
                freeSettingsInDb.AioRequestsPerMinute = FreeTierSettings.AioRequestsPerMinute;
                freeSettingsInDb.DailyVipSrtLimit = FreeTierSettings.DailyVipSrtLimit;
                // === KẾT THÚC THÊM MỚI ===

                await _context.SaveChangesAsync();
                SuccessMessage = "Đã cập nhật thành công cài đặt mặc định cho người dùng đăng ký mới!";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Đã có lỗi xảy ra khi lưu cài đặt: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}