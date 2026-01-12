using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    public interface ITierSettingsService
    {
        /// <summary>
        /// Áp dụng các cài đặt mặc định cho một tier cụ thể từ cấu hình vào một đối tượng User.
        /// </summary>
        void ApplyTierSettings(User user, SubscriptionTier tier);
    }
}