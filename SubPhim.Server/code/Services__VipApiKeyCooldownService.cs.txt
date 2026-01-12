using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service quản lý cooldown cho VIP API keys bị lỗi 429.
    /// Hoạt động giống 100% như ApiKeyCooldownService cho LocalAPI.
    /// </summary>
    public class VipApiKeyCooldownService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<VipApiKeyCooldownService> _logger;
        
        // Cache in-memory để kiểm tra nhanh mà không cần query DB liên tục
        private static readonly ConcurrentDictionary<int, DateTime> _cooldownCache = new();
        
        // Thời gian cooldown theo số lần lỗi 429 liên tiếp
        private static readonly Dictionary<int, TimeSpan> _cooldownDurations = new()
        {
            { 1, TimeSpan.FromSeconds(30) },   // Lần 1: 30 giây
            { 2, TimeSpan.FromMinutes(2) },    // Lần 2: 2 phút
            { 3, TimeSpan.FromMinutes(5) },    // Lần 3: 5 phút
            { 4, TimeSpan.FromMinutes(15) },   // Lần 4: 15 phút
        };

        public VipApiKeyCooldownService(IServiceProvider serviceProvider, ILogger<VipApiKeyCooldownService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Đặt VIP API key vào cooldown sau khi gặp lỗi 429
        /// </summary>
        public async Task SetCooldownAsync(int keyId, string errorDetail = "HTTP 429 - Rate Limit Exceeded")
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.VipApiKeys.FindAsync(keyId);
            if (key == null)
            {
                _logger.LogWarning("Không tìm thấy VIP API Key ID {KeyId} để set cooldown", keyId);
                return;
            }

            // Tăng số lần gặp lỗi 429 liên tiếp
            key.Consecutive429Count++;
            
            // Tính thời gian cooldown dựa trên số lần lỗi
            var cooldownDuration = _cooldownDurations.TryGetValue(key.Consecutive429Count, out var duration)
                ? duration
                : TimeSpan.FromMinutes(30); // Max 30 phút cho các lần tiếp theo

            key.TemporaryCooldownUntil = DateTime.UtcNow.Add(cooldownDuration);
            key.DisabledReason = $"[COOLDOWN {(int)cooldownDuration.TotalSeconds}s] {errorDetail} (Lần {key.Consecutive429Count})";
            
            // Cập nhật cache
            _cooldownCache.AddOrUpdate(keyId, key.TemporaryCooldownUntil.Value, (_, __) => key.TemporaryCooldownUntil.Value);
            
            await context.SaveChangesAsync();
            
            _logger.LogWarning(
                "VIP API Key ID {KeyId} đã được đặt vào cooldown đến {CooldownUntil} (Thời gian: {Duration}, Lần thứ {Count})",
                keyId, 
                key.TemporaryCooldownUntil.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                cooldownDuration,
                key.Consecutive429Count
            );
        }

        /// <summary>
        /// Kiểm tra xem key có đang trong cooldown không
        /// </summary>
        public bool IsInCooldown(int keyId)
        {
            // Kiểm tra cache trước
            if (_cooldownCache.TryGetValue(keyId, out var cooldownUntil))
            {
                if (DateTime.UtcNow < cooldownUntil)
                {
                    return true; // Vẫn trong cooldown
                }
                else
                {
                    // Đã hết cooldown, xóa khỏi cache
                    _cooldownCache.TryRemove(keyId, out _);
                    return false;
                }
            }
            
            return false; // Không có trong cache = không cooldown
        }

        /// <summary>
        /// Reset cooldown cho một key cụ thể (dùng khi key hoạt động bình thường trở lại)
        /// </summary>
        public async Task ResetCooldownAsync(int keyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.VipApiKeys.FindAsync(keyId);
            if (key == null) return;

            if (key.TemporaryCooldownUntil.HasValue || key.Consecutive429Count > 0)
            {
                key.TemporaryCooldownUntil = null;
                key.Consecutive429Count = 0;
                key.DisabledReason = null;
                
                // Xóa khỏi cache
                _cooldownCache.TryRemove(keyId, out _);
                
                await context.SaveChangesAsync();
                
                _logger.LogInformation("VIP API Key ID {KeyId} đã được reset cooldown và consecutive error count", keyId);
            }
        }

        /// <summary>
        /// Tự động re-enable tất cả keys đã hết thời gian cooldown
        /// </summary>
        public async Task ProcessExpiredCooldownsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var now = DateTime.UtcNow;
            
            var expiredKeys = await context.VipApiKeys
                .Where(k => k.TemporaryCooldownUntil.HasValue && k.TemporaryCooldownUntil.Value <= now)
                .ToListAsync();

            if (expiredKeys.Any())
            {
                foreach (var key in expiredKeys)
                {
                    key.TemporaryCooldownUntil = null;
                    
                    // KHÔNG reset Consecutive429Count ở đây, chỉ reset khi key hoạt động thành công
                    // Nếu key lại bị 429 ngay sau khi hết cooldown, số lần lỗi sẽ tiếp tục tăng
                    
                    if (key.DisabledReason?.Contains("[COOLDOWN") == true)
                    {
                        key.DisabledReason = null; // Chỉ xóa reason nếu là do cooldown
                    }
                    
                    // Xóa khỏi cache
                    _cooldownCache.TryRemove(key.Id, out _);
                }

                await context.SaveChangesAsync();
                
                _logger.LogInformation("Đã tự động re-enable {Count} VIP API keys sau khi hết cooldown", expiredKeys.Count);
            }
        }

        /// <summary>
        /// Vô hiệu hóa vĩnh viễn một key (dùng cho lỗi nghiêm trọng như 401, invalid key)
        /// </summary>
        public async Task DisableKeyPermanentlyAsync(int keyId, string reason)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.VipApiKeys.FindAsync(keyId);
            if (key == null) return;

            key.IsEnabled = false;
            key.DisabledReason = $"[PERMANENT] {reason}";
            key.TemporaryCooldownUntil = null;
            key.Consecutive429Count = 0;
            
            // Xóa khỏi cache
            _cooldownCache.TryRemove(keyId, out _);
            
            await context.SaveChangesAsync();
            
            _logger.LogError("VIP API Key ID {KeyId} đã bị vô hiệu hóa VĨNH VIỄN. Lý do: {Reason}", keyId, reason);
        }

        /// <summary>
        /// Lấy danh sách tất cả keys đang trong cooldown
        /// </summary>
        public async Task<List<(int KeyId, DateTime CooldownUntil, int ErrorCount)>> GetKeysInCooldownAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var now = DateTime.UtcNow;
            
            return await context.VipApiKeys
                .Where(k => k.TemporaryCooldownUntil.HasValue && k.TemporaryCooldownUntil.Value > now)
                .Select(k => new ValueTuple<int, DateTime, int>(k.Id, k.TemporaryCooldownUntil.Value, k.Consecutive429Count))
                .ToListAsync();
        }
    }
}
