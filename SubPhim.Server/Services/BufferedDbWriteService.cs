// VỊ TRÍ: Services/BufferedDbWriteService.cs
// Service quản lý buffered writes để giảm DB lock trong WAL mode
// Gom các thay đổi nhỏ lại và ghi batch định kỳ

using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Collections.Concurrent;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service quản lý buffered database writes cho VbeeTts và Translation services.
    /// Sử dụng in-memory cache và periodic flush để giảm DB lock.
    /// </summary>
    public interface IBufferedDbWriteService
    {
        // Vbee TTS buffers
        void BufferVbeeSessionHeartbeat(string sessionId);
        void BufferVbeeCharacterUsage(string sessionId, int userId, long charactersUsed);
        void BufferVbeeSessionStatusChange(string sessionId, VbeeTtsSessionStatus newStatus, long? finalCharacters = null);
        
        // Translation buffers
        void BufferApiKeyUsage(int keyId, int tokensUsed);
        void BufferTranslationProgress(string sessionId, int translatedCount);
        
        // Force flush (for shutdown or critical operations)
        Task FlushAllAsync(CancellationToken cancellationToken = default);
    }

    public class BufferedDbWriteService : IBufferedDbWriteService, IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BufferedDbWriteService> _logger;
        private readonly Timer _flushTimer;
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        
        // === CONFIGURATION ===
        private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(10); // Ghi mỗi 10 giây
        private readonly int _maxBufferSize = 1000; // Force flush nếu buffer > 1000 items
        
        // === VBEE TTS BUFFERS ===
        // Heartbeat: chỉ cần lưu timestamp cuối cùng
        private readonly ConcurrentDictionary<string, DateTime> _vbeeHeartbeats = new();
        
        // Character usage: gom lại theo session
        private readonly ConcurrentDictionary<string, VbeeUsageBuffer> _vbeeUsage = new();
        
        // Session status changes: ưu tiên cao, ghi ngay trong batch tiếp theo
        private readonly ConcurrentQueue<VbeeStatusChange> _vbeeStatusQueue = new();
        
        // === TRANSLATION BUFFERS ===
        // API Key token usage
        private readonly ConcurrentDictionary<int, int> _apiKeyTokens = new();
        
        // Translation progress
        private readonly ConcurrentDictionary<string, int> _translationProgress = new();

        public BufferedDbWriteService(
            IServiceProvider serviceProvider,
            ILogger<BufferedDbWriteService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _flushTimer = new Timer(FlushTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        #region IHostedService Implementation

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("BufferedDbWriteService started. Flush interval: {Interval}s", _flushInterval.TotalSeconds);
            _flushTimer.Change(_flushInterval, _flushInterval);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("BufferedDbWriteService stopping. Flushing remaining buffers...");
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            await FlushAllAsync(cancellationToken);
            _logger.LogInformation("BufferedDbWriteService stopped.");
        }

        #endregion

        #region Vbee TTS Buffer Methods

        public void BufferVbeeSessionHeartbeat(string sessionId)
        {
            _vbeeHeartbeats[sessionId] = DateTime.UtcNow;
            CheckBufferSize();
        }

        public void BufferVbeeCharacterUsage(string sessionId, int userId, long charactersUsed)
        {
            _vbeeUsage.AddOrUpdate(
                sessionId,
                new VbeeUsageBuffer { UserId = userId, CharactersUsed = charactersUsed },
                (key, existing) =>
                {
                    existing.CharactersUsed += charactersUsed;
                    return existing;
                });
            CheckBufferSize();
        }

        public void BufferVbeeSessionStatusChange(string sessionId, VbeeTtsSessionStatus newStatus, long? finalCharacters = null)
        {
            _vbeeStatusQueue.Enqueue(new VbeeStatusChange
            {
                SessionId = sessionId,
                NewStatus = newStatus,
                FinalCharacters = finalCharacters,
                Timestamp = DateTime.UtcNow
            });
            CheckBufferSize();
        }

        #endregion

        #region Translation Buffer Methods

        public void BufferApiKeyUsage(int keyId, int tokensUsed)
        {
            _apiKeyTokens.AddOrUpdate(keyId, tokensUsed, (key, existing) => existing + tokensUsed);
            CheckBufferSize();
        }

        public void BufferTranslationProgress(string sessionId, int translatedCount)
        {
            _translationProgress.AddOrUpdate(sessionId, translatedCount, (key, existing) => translatedCount);
            CheckBufferSize();
        }

        #endregion

        #region Flush Logic

        private void CheckBufferSize()
        {
            int totalItems = _vbeeHeartbeats.Count + _vbeeUsage.Count + _vbeeStatusQueue.Count +
                             _apiKeyTokens.Count + _translationProgress.Count;
            
            if (totalItems >= _maxBufferSize)
            {
                _logger.LogWarning("Buffer size exceeded {Max}. Triggering immediate flush.", _maxBufferSize);
                _ = Task.Run(() => FlushAllAsync());
            }
        }

        private async void FlushTimerCallback(object? state)
        {
            try
            {
                await FlushAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled buffer flush");
            }
        }

        public async Task FlushAllAsync(CancellationToken cancellationToken = default)
        {
            if (!await _flushLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            {
                _logger.LogWarning("Flush already in progress, skipping this cycle.");
                return;
            }

            try
            {
                int totalFlushed = 0;

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // === FLUSH VBEE STATUS CHANGES (ưu tiên cao) ===
                totalFlushed += await FlushVbeeStatusChangesAsync(context, cancellationToken);

                // === FLUSH VBEE HEARTBEATS ===
                totalFlushed += await FlushVbeeHeartbeatsAsync(context, cancellationToken);

                // === FLUSH VBEE CHARACTER USAGE ===
                totalFlushed += await FlushVbeeUsageAsync(context, cancellationToken);

                // === FLUSH API KEY TOKENS ===
                totalFlushed += await FlushApiKeyTokensAsync(context, cancellationToken);

                // === FLUSH TRANSLATION PROGRESS ===
                totalFlushed += await FlushTranslationProgressAsync(context, cancellationToken);

                if (totalFlushed > 0)
                {
                    _logger.LogDebug("Flushed {Count} buffered items to database", totalFlushed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during buffer flush");
            }
            finally
            {
                _flushLock.Release();
            }
        }

        private async Task<int> FlushVbeeStatusChangesAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            int count = 0;
            var statusChanges = new List<VbeeStatusChange>();
            
            while (_vbeeStatusQueue.TryDequeue(out var change))
            {
                statusChanges.Add(change);
            }

            if (statusChanges.Count == 0) return 0;

            try
            {
                // Group by session để chỉ áp dụng status mới nhất
                var latestBySession = statusChanges
                    .GroupBy(s => s.SessionId)
                    .Select(g => g.OrderByDescending(x => x.Timestamp).First())
                    .ToList();

                foreach (var change in latestBySession)
                {
                    var session = await context.VbeeTtsSessions
                        .FirstOrDefaultAsync(s => s.SessionId == change.SessionId, cancellationToken);
                    
                    if (session != null)
                    {
                        session.Status = change.NewStatus;
                        if (change.FinalCharacters.HasValue)
                        {
                            session.CharactersProcessed = change.FinalCharacters.Value;
                        }
                        if (change.NewStatus == VbeeTtsSessionStatus.Completed || 
                            change.NewStatus == VbeeTtsSessionStatus.Cancelled ||
                            change.NewStatus == VbeeTtsSessionStatus.Failed)
                        {
                            session.CompletedAt = change.Timestamp;
                        }
                        count++;
                    }
                }

                if (count > 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing Vbee status changes");
                // Re-queue failed items
                foreach (var change in statusChanges)
                {
                    _vbeeStatusQueue.Enqueue(change);
                }
            }

            return count;
        }

        private async Task<int> FlushVbeeHeartbeatsAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            // Lấy snapshot và clear
            var heartbeats = _vbeeHeartbeats.ToArray();
            foreach (var kvp in heartbeats)
            {
                _vbeeHeartbeats.TryRemove(kvp.Key, out _);
            }

            if (heartbeats.Length == 0) return 0;

            try
            {
                var sessionIds = heartbeats.Select(h => h.Key).ToList();
                var sessions = await context.VbeeTtsSessions
                    .Where(s => sessionIds.Contains(s.SessionId))
                    .ToListAsync(cancellationToken);

                foreach (var session in sessions)
                {
                    if (heartbeats.FirstOrDefault(h => h.Key == session.SessionId) is var match && match.Key != null)
                    {
                        session.LastHeartbeatAt = match.Value;
                    }
                }

                if (sessions.Count > 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                }

                return sessions.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing Vbee heartbeats");
                // Re-add failed heartbeats
                foreach (var kvp in heartbeats)
                {
                    _vbeeHeartbeats.TryAdd(kvp.Key, kvp.Value);
                }
                return 0;
            }
        }

        private async Task<int> FlushVbeeUsageAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            // Lấy snapshot và clear
            var usageItems = _vbeeUsage.ToArray();
            foreach (var kvp in usageItems)
            {
                _vbeeUsage.TryRemove(kvp.Key, out _);
            }

            if (usageItems.Length == 0) return 0;

            try
            {
                // Group by userId để tối ưu
                var userUpdates = usageItems
                    .GroupBy(u => u.Value.UserId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Value.CharactersUsed));

                var sessionUpdates = usageItems.ToDictionary(u => u.Key, u => u.Value.CharactersUsed);

                // Update sessions
                var sessionIds = usageItems.Select(u => u.Key).ToList();
                var sessions = await context.VbeeTtsSessions
                    .Where(s => sessionIds.Contains(s.SessionId))
                    .ToListAsync(cancellationToken);

                foreach (var session in sessions)
                {
                    if (sessionUpdates.TryGetValue(session.SessionId, out var chars))
                    {
                        session.CharactersProcessed += chars;
                        session.CharactersCharged += chars;
                    }
                }

                // Update users
                var userIds = userUpdates.Keys.ToList();
                var users = await context.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToListAsync(cancellationToken);

                foreach (var user in users)
                {
                    if (userUpdates.TryGetValue(user.Id, out var chars))
                    {
                        user.VbeeCharactersUsed += chars;
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                return usageItems.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing Vbee usage");
                // Re-add failed items
                foreach (var kvp in usageItems)
                {
                    _vbeeUsage.TryAdd(kvp.Key, kvp.Value);
                }
                return 0;
            }
        }

        private async Task<int> FlushApiKeyTokensAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            // Lấy snapshot và clear
            var tokenItems = _apiKeyTokens.ToArray();
            foreach (var kvp in tokenItems)
            {
                _apiKeyTokens.TryRemove(kvp.Key, out _);
            }

            if (tokenItems.Length == 0) return 0;

            try
            {
                var keyIds = tokenItems.Select(t => t.Key).ToList();
                
                // Update ManagedApiKeys (LocalAPI)
                var managedKeys = await context.ManagedApiKeys
                    .Where(k => keyIds.Contains(k.Id))
                    .ToListAsync(cancellationToken);

                foreach (var key in managedKeys)
                {
                    if (tokenItems.FirstOrDefault(t => t.Key == key.Id) is var match && match.Key != 0)
                    {
                        key.TotalTokensUsed += match.Value;
                    }
                }

                // Update VipApiKeys (VIP Translation)
                var vipKeys = await context.VipApiKeys
                    .Where(k => keyIds.Contains(k.Id))
                    .ToListAsync(cancellationToken);

                foreach (var key in vipKeys)
                {
                    if (tokenItems.FirstOrDefault(t => t.Key == key.Id) is var match && match.Key != 0)
                    {
                        key.TotalTokensUsed += match.Value;
                    }
                }

                if (managedKeys.Count > 0 || vipKeys.Count > 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                }

                return tokenItems.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing API key tokens");
                // Re-add failed items
                foreach (var kvp in tokenItems)
                {
                    _apiKeyTokens.TryAdd(kvp.Key, kvp.Value);
                }
                return 0;
            }
        }

        private Task<int> FlushTranslationProgressAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            // Translation progress được track qua TranslatedSrtLines collection
            // Không cần lưu riêng vào TranslationJobDb, chỉ cần clear buffer
            var progressItems = _translationProgress.ToArray();
            foreach (var kvp in progressItems)
            {
                _translationProgress.TryRemove(kvp.Key, out _);
            }
            
            // Log nếu có items được buffer (chủ yếu để debug)
            if (progressItems.Length > 0)
            {
                _logger.LogDebug("Cleared {Count} translation progress items from buffer", progressItems.Length);
            }
            
            return Task.FromResult(progressItems.Length);
        }

        #endregion

        #region Helper Classes

        private class VbeeUsageBuffer
        {
            public int UserId { get; set; }
            public long CharactersUsed { get; set; }
        }

        private class VbeeStatusChange
        {
            public string SessionId { get; set; } = string.Empty;
            public VbeeTtsSessionStatus NewStatus { get; set; }
            public long? FinalCharacters { get; set; }
            public DateTime Timestamp { get; set; }
        }

        #endregion

        public void Dispose()
        {
            _flushTimer?.Dispose();
            _flushLock?.Dispose();
        }
    }
}
