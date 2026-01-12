using SubPhim.Server.Data; // Đảm bảo có using này

namespace SubPhim.Server.Services
{
    public interface ITtsSettingsService
    {
        // SỬA LỖI: Dùng TtsProvider
        Task<TtsModelSetting?> GetModelSettingsAsync(TtsProvider provider, string identifier);
    }
}