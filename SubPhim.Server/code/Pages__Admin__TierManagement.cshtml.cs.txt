// VỊ TRÍ: Pages/Admin/TierManagement.cshtml.cs
// THAY THẾ TOÀN BỘ FILE

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Threading.Tasks;

namespace SubPhim.Server.Pages.Admin
{
    public class TierManagementModel : PageModel
    {
        private readonly AppDbContext _context;

        public TierManagementModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Dictionary<SubscriptionTier, TierConfigModel> Configs { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public class TierConfigModel
        {
            public int VideoDurationMinutes { get; set; }
            public int DailyVideoCount { get; set; }
            public int DailyTranslationRequests { get; set; }
            public AllowedApis AllowedApis { get; set; }
            public GrantedFeatures GrantedFeatures { get; set; }
            public int DailySrtLineLimit { get; set; }
            public long TtsCharacterLimit { get; set; }
            public int DailyLocalSrtLimit { get; set; }

            // === BẮT ĐẦU THÊM MỚI ===
            public long AioCharacterLimit { get; set; }
            public int AioRequestsPerMinute { get; set; }
            public int DailyVipSrtLimit { get; set; }
            public bool IsYearlyProSettings { get; set; }
            // === KẾT THÚC THÊM MỚI ===
        }

        public async Task OnGetAsync()
        {
            var defaultSettings = await _context.TierDefaultSettings.ToListAsync();

            // For most tiers, there will be one setting. For Yearly, there might be two (Pro and Standard)
            // We load settings prioritizing Pro settings for Yearly tier if they exist
            foreach (var setting in defaultSettings)
            {
                // For Yearly tier, we want to load Pro settings if available, otherwise load Standard settings
                if (setting.Tier == SubscriptionTier.Yearly)
                {
                    // If no Yearly config loaded yet, add this one
                    // If Yearly config exists but this one is Pro, replace with Pro settings
                    if (!Configs.ContainsKey(setting.Tier) || setting.IsYearlyProSettings)
                    {
                        if (Configs.ContainsKey(setting.Tier))
                        {
                            Configs.Remove(setting.Tier);
                        }
                        Configs.Add(setting.Tier, new TierConfigModel
                        {
                            VideoDurationMinutes = setting.VideoDurationMinutes,
                            DailyVideoCount = setting.DailyVideoCount,
                            DailyTranslationRequests = setting.DailyTranslationRequests,
                            AllowedApis = setting.AllowedApis,
                            GrantedFeatures = setting.GrantedFeatures,
                            DailySrtLineLimit = setting.DailySrtLineLimit,
                            TtsCharacterLimit = setting.TtsCharacterLimit,
                            DailyLocalSrtLimit = setting.DailyLocalSrtLimit,
                            AioCharacterLimit = setting.AioCharacterLimit,
                            AioRequestsPerMinute = setting.AioRequestsPerMinute,
                            DailyVipSrtLimit = setting.DailyVipSrtLimit,
                            IsYearlyProSettings = setting.IsYearlyProSettings
                        });
                    }
                }
                else if (!Configs.ContainsKey(setting.Tier))
                {
                    // For non-Yearly tiers, just add if not already present
                    Configs.Add(setting.Tier, new TierConfigModel
                    {
                        VideoDurationMinutes = setting.VideoDurationMinutes,
                        DailyVideoCount = setting.DailyVideoCount,
                        DailyTranslationRequests = setting.DailyTranslationRequests,
                        AllowedApis = setting.AllowedApis,
                        GrantedFeatures = setting.GrantedFeatures,
                        DailySrtLineLimit = setting.DailySrtLineLimit,
                        TtsCharacterLimit = setting.TtsCharacterLimit,
                        DailyLocalSrtLimit = setting.DailyLocalSrtLimit,
                        AioCharacterLimit = setting.AioCharacterLimit,
                        AioRequestsPerMinute = setting.AioRequestsPerMinute,
                        DailyVipSrtLimit = setting.DailyVipSrtLimit,
                        IsYearlyProSettings = setting.IsYearlyProSettings
                    });
                }
            }
        }
        public async Task<IActionResult> OnPostSaveDefaultsAsync()
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var configEntry in Configs)
                {
                    var tier = configEntry.Key;
                    var formConfig = configEntry.Value;

                    // Find existing setting by tier and IsYearlyProSettings
                    var settingInDb = await _context.TierDefaultSettings
                        .Where(s => s.Tier == tier && s.IsYearlyProSettings == formConfig.IsYearlyProSettings)
                        .FirstOrDefaultAsync();
                    
                    if (settingInDb != null)
                    {
                        settingInDb.VideoDurationMinutes = formConfig.VideoDurationMinutes;
                        settingInDb.DailyVideoCount = formConfig.DailyVideoCount;
                        settingInDb.DailyTranslationRequests = formConfig.DailyTranslationRequests;
                        settingInDb.AllowedApis = formConfig.AllowedApis;
                        settingInDb.GrantedFeatures = formConfig.GrantedFeatures;
                        settingInDb.DailySrtLineLimit = formConfig.DailySrtLineLimit;
                        settingInDb.TtsCharacterLimit = formConfig.TtsCharacterLimit;
                        settingInDb.DailyLocalSrtLimit = formConfig.DailyLocalSrtLimit;
                        settingInDb.AioCharacterLimit = formConfig.AioCharacterLimit;
                        settingInDb.AioRequestsPerMinute = formConfig.AioRequestsPerMinute;
                        settingInDb.DailyVipSrtLimit = formConfig.DailyVipSrtLimit;
                        settingInDb.IsYearlyProSettings = formConfig.IsYearlyProSettings;
                    }
                    else
                    {
                        // Create new setting if it doesn't exist
                        _context.TierDefaultSettings.Add(new TierDefaultSetting
                        {
                            Tier = tier,
                            VideoDurationMinutes = formConfig.VideoDurationMinutes,
                            DailyVideoCount = formConfig.DailyVideoCount,
                            DailyTranslationRequests = formConfig.DailyTranslationRequests,
                            AllowedApis = formConfig.AllowedApis,
                            GrantedFeatures = formConfig.GrantedFeatures,
                            DailySrtLineLimit = formConfig.DailySrtLineLimit,
                            TtsCharacterLimit = formConfig.TtsCharacterLimit,
                            DailyLocalSrtLimit = formConfig.DailyLocalSrtLimit,
                            AioCharacterLimit = formConfig.AioCharacterLimit,
                            AioRequestsPerMinute = formConfig.AioRequestsPerMinute,
                            DailyVipSrtLimit = formConfig.DailyVipSrtLimit,
                            IsYearlyProSettings = formConfig.IsYearlyProSettings
                        });
                    }
                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                SuccessMessage = "Đã lưu thành công cấu hình mặc định. Các tài khoản mới hoặc được áp dụng tier sẽ nhận cài đặt này.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ErrorMessage = "Lỗi khi lưu cấu hình mặc định: " + ex.Message;
            }
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostApplyToAllUsersAsync(SubscriptionTier tier)
        {
            if (!Configs.TryGetValue(tier, out var configToApply))
            {
                ErrorMessage = "Không tìm thấy cấu hình cho nhóm được chọn.";
                return RedirectToPage();
            }

            try
            {
                // Nếu đây là cấu hình PRO cho gói Yearly, chỉ áp dụng cho user có IsYearlyPro = true
                // Ngược lại, áp dụng cho tất cả users trong tier (ngoại trừ PRO users nếu đây là cấu hình thường)
                IQueryable<User> query = _context.Users.Where(u => u.Tier == tier);
                
                if (tier == SubscriptionTier.Yearly)
                {
                    if (configToApply.IsYearlyProSettings)
                    {
                        // Cấu hình PRO: chỉ áp dụng cho user đã được đánh dấu PRO
                        query = query.Where(u => u.IsYearlyPro);
                    }
                    else
                    {
                        // Cấu hình thường: chỉ áp dụng cho user KHÔNG được đánh dấu PRO
                        query = query.Where(u => !u.IsYearlyPro);
                    }
                }
                
                var usersToUpdate = await query.ToListAsync();

                if (!usersToUpdate.Any())
                {
                    string proSuffix = (tier == SubscriptionTier.Yearly && configToApply.IsYearlyProSettings) ? " PRO" : "";
                    SuccessMessage = $"Không có người dùng nào thuộc nhóm '{tier}{proSuffix}' để áp dụng.";
                    return RedirectToPage();
                }

                foreach (var user in usersToUpdate)
                {
                    user.VideoDurationLimitMinutes = configToApply.VideoDurationMinutes;
                    user.DailyVideoLimit = configToApply.DailyVideoCount;
                    user.DailyRequestLimitOverride = configToApply.DailyTranslationRequests;
                    user.AllowedApiAccess = configToApply.AllowedApis;
                    user.GrantedFeatures = configToApply.GrantedFeatures;
                    user.DailySrtLineLimit = configToApply.DailySrtLineLimit;
                    user.TtsCharacterLimit = configToApply.TtsCharacterLimit;
                    user.DailyLocalSrtLimit = configToApply.DailyLocalSrtLimit;
                    user.DailyVipSrtLimit = configToApply.DailyVipSrtLimit;

                    user.AioCharacterLimitOverride = -1;
                    user.AioRpmOverride = -1;
                }

                var count = await _context.SaveChangesAsync();
                string proLabel = (tier == SubscriptionTier.Yearly && configToApply.IsYearlyProSettings) ? " PRO" : "";
                SuccessMessage = $"Đã áp dụng thành công cấu hình mới cho {count} người dùng thuộc nhóm '{tier}{proLabel}'.";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Lỗi khi cập nhật hàng loạt người dùng: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}