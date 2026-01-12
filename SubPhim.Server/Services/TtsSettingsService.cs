using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data; // Đảm bảo có using này

namespace SubPhim.Server.Services
{
    public class TtsSettingsService : ITtsSettingsService
    {
        private readonly AppDbContext _context;

        public TtsSettingsService(AppDbContext context)
        {
            _context = context;
        }

        // SỬA LỖI: Dùng TtsProvider
        public async Task<TtsModelSetting?> GetModelSettingsAsync(TtsProvider provider, string identifier)
        {
            return await _context.TtsModelSettings
                .AsNoTracking() // Thêm AsNoTracking để tối ưu
                .FirstOrDefaultAsync(s => s.Provider == provider && s.Identifier.ToLower() == identifier.ToLower());
        }
    }
}