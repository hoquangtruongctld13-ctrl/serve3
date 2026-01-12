// VỊ TRÍ: Settings/UsageLimitsSettings.cs (TẠO FILE MỚI)

namespace SubPhim.Server.Settings
{
    // Lớp này đại diện cho toàn bộ section "UsageLimits"
    public class UsageLimitsSettings
    {
        public TierConfig Free { get; set; }
        public TierConfig Daily { get; set; }
        public TierConfig Monthly { get; set; }
        public TierConfig Yearly { get; set; }
        public TierConfig Lifetime { get; set; }
    }

    // Lớp này đại diện cho các thuộc tính của mỗi tier
    public class TierConfig
    {
        public int VideoDurationMinutes { get; set; }
        public int DailyVideoCount { get; set; }
        public int DailyTranslationRequests { get; set; }
        public string AllowedApis { get; set; } // Giữ là string để dễ parse
        public string GrantedFeatures { get; set; } // Giữ là string để dễ parse
        public int DailySrtLineLimit { get; set; }
        public long TtsCharacterLimit { get; set; }
    }
}