using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service qu?n lý cooldown cho API keys b? l?i 429
    /// </summary>
    public class ApiKeyCooldownService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ApiKeyCooldownService> _logger;
        
        // Cache in-memory ?? ki?m tra nhanh mà không c?n query DB liên t?c
        private static readonly ConcurrentDictionary<int, DateTime> _cooldownCache = new();
        
        // Th?i gian cooldown theo s? l?n l?i 429 liên ti?p
        private static readonly Dictionary<int, TimeSpan> _cooldownDurations = new()
        {
            { 1, TimeSpan.FromSeconds(30) },   // L?n 1: 30 giây
            { 2, TimeSpan.FromMinutes(2) },    // L?n 2: 2 phút
            { 3, TimeSpan.FromMinutes(5) },    // L?n 3: 5 phút
            { 4, TimeSpan.FromMinutes(15) },   // L?n 4: 15 phút
        };

        public ApiKeyCooldownService(IServiceProvider serviceProvider, ILogger<ApiKeyCooldownService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// ??t API key vào cooldown sau khi g?p l?i 429
        /// </summary>
        public async Task SetCooldownAsync(int keyId, string errorDetail = "HTTP 429 - Rate Limit Exceeded")
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.ManagedApiKeys.FindAsync(keyId);
            if (key == null)
            {
                _logger.LogWarning("Không tìm th?y API Key ID {KeyId} ?? set cooldown", keyId);
                return;
            }

            // T?ng s? l?n g?p l?i 429 liên ti?p
            key.Consecutive429Count++;
            
            // Tính th?i gian cooldown d?a trên s? l?n l?i
            var cooldownDuration = _cooldownDurations.TryGetValue(key.Consecutive429Count, out var duration)
                ? duration
                : TimeSpan.FromMinutes(30); // Max 30 phút cho các l?n ti?p theo

            key.TemporaryCooldownUntil = DateTime.UtcNow.Add(cooldownDuration);
            key.DisabledReason = $"[COOLDOWN {(int)cooldownDuration.TotalSeconds}s] {errorDetail} (L?n {key.Consecutive429Count})";
            
            // C?p nh?t cache
            _cooldownCache.AddOrUpdate(keyId, key.TemporaryCooldownUntil.Value, (_, __) => key.TemporaryCooldownUntil.Value);
            
            await context.SaveChangesAsync();
            
            _logger.LogWarning(
                "API Key ID {KeyId} ?ã ???c ??t vào cooldown ??n {CooldownUntil} (Th?i gian: {Duration}, L?n th? {Count})",
                keyId, 
                key.TemporaryCooldownUntil.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                cooldownDuration,
                key.Consecutive429Count
            );
        }

        /// <summary>
        /// Ki?m tra xem key có ?ang trong cooldown không
        /// </summary>
        public bool IsInCooldown(int keyId)
        {
            // Ki?m tra cache tr??c
            if (_cooldownCache.TryGetValue(keyId, out var cooldownUntil))
            {
                if (DateTime.UtcNow < cooldownUntil)
                {
                    return true; // V?n trong cooldown
                }
                else
                {
                    // ?ã h?t cooldown, xóa kh?i cache
                    _cooldownCache.TryRemove(keyId, out _);
                    return false;
                }
            }
            
            return false; // Không có trong cache = không cooldown
        }

        /// <summary>
        /// Reset cooldown cho m?t key c? th? (dùng khi key ho?t ??ng bình th??ng tr? l?i)
        /// </summary>
        public async Task ResetCooldownAsync(int keyId)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.ManagedApiKeys.FindAsync(keyId);
            if (key == null) return;

            if (key.TemporaryCooldownUntil.HasValue || key.Consecutive429Count > 0)
            {
                key.TemporaryCooldownUntil = null;
                key.Consecutive429Count = 0;
                key.DisabledReason = null;
                
                // Xóa kh?i cache
                _cooldownCache.TryRemove(keyId, out _);
                
                await context.SaveChangesAsync();
                
                _logger.LogInformation("API Key ID {KeyId} ?ã ???c reset cooldown và consecutive error count", keyId);
            }
        }

        /// <summary>
        /// T? ??ng re-enable t?t c? keys ?ã h?t th?i gian cooldown
        /// </summary>
        public async Task ProcessExpiredCooldownsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var now = DateTime.UtcNow;
            
            var expiredKeys = await context.ManagedApiKeys
                .Where(k => k.TemporaryCooldownUntil.HasValue && k.TemporaryCooldownUntil.Value <= now)
                .ToListAsync();

            if (expiredKeys.Any())
            {
                foreach (var key in expiredKeys)
                {
                    key.TemporaryCooldownUntil = null;
                    
                    // KHÔNG reset Consecutive429Count ? ?ây, ch? reset khi key ho?t ??ng thành công
                    // N?u key l?i b? 429 ngay sau khi h?t cooldown, s? l?n l?i s? ti?p t?c t?ng
                    
                    if (key.DisabledReason?.Contains("[COOLDOWN") == true)
                    {
                        key.DisabledReason = null; // Ch? xóa reason n?u là do cooldown
                    }
                    
                    // Xóa kh?i cache
                    _cooldownCache.TryRemove(key.Id, out _);
                }

                await context.SaveChangesAsync();
                
                _logger.LogInformation("?ã t? ??ng re-enable {Count} API keys sau khi h?t cooldown", expiredKeys.Count);
            }
        }

        /// <summary>
        /// Vô hi?u hóa v?nh vi?n m?t key (dùng cho l?i nghiêm tr?ng nh? 401, invalid key)
        /// </summary>
        public async Task DisableKeyPermanentlyAsync(int keyId, string reason)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var key = await context.ManagedApiKeys.FindAsync(keyId);
            if (key == null) return;

            key.IsEnabled = false;
            key.DisabledReason = $"[PERMANENT] {reason}";
            key.TemporaryCooldownUntil = null;
            key.Consecutive429Count = 0;
            
            // Xóa kh?i cache
            _cooldownCache.TryRemove(keyId, out _);
            
            await context.SaveChangesAsync();
            
            _logger.LogError("API Key ID {KeyId} ?ã b? vô hi?u hóa V?NH VI?N. Lý do: {Reason}", keyId, reason);
        }

        /// <summary>
        /// L?y danh sách t?t c? keys ?ang trong cooldown
        /// </summary>
        public async Task<List<(int KeyId, DateTime CooldownUntil, int ErrorCount)>> GetKeysInCooldownAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var now = DateTime.UtcNow;
            
            return await context.ManagedApiKeys
                .Where(k => k.TemporaryCooldownUntil.HasValue && k.TemporaryCooldownUntil.Value > now)
                .Select(k => new ValueTuple<int, DateTime, int>(k.Id, k.TemporaryCooldownUntil.Value, k.Consecutive429Count))
                .ToListAsync();
        }
    }
}
