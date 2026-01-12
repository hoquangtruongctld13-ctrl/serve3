using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service xử lý cập nhật thống kê proxy theo kiểu fire-and-forget.
    /// Sử dụng Channel để queue các updates và xử lý batch để giảm I/O.
    /// </summary>
    public class ProxyStatsQueueService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProxyStatsQueueService> _logger;
        
        // Channels for different types of updates
        private readonly Channel<ProxySuccessUpdate> _successChannel;
        private readonly Channel<ProxyFailureUpdate> _failureChannel;
        
        // In-memory aggregation for batching
        private readonly ConcurrentDictionary<int, ProxyStatsAggregate> _pendingUpdates = new();
        
        // Configuration
        private const int BATCH_INTERVAL_MS = 5000; // Ghi database mỗi 5 giây
        private const int MAX_BATCH_SIZE = 50; // Tối đa 50 updates mỗi batch
        private const int CHANNEL_CAPACITY = 10000; // Capacity của channel
        
        public ProxyStatsQueueService(
            IServiceProvider serviceProvider,
            ILogger<ProxyStatsQueueService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            // Bounded channels để tránh memory leak nếu updates quá nhiều
            var options = new BoundedChannelOptions(CHANNEL_CAPACITY)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest nếu full
                SingleReader = true,
                SingleWriter = false
            };
            
            _successChannel = Channel.CreateBounded<ProxySuccessUpdate>(options);
            _failureChannel = Channel.CreateBounded<ProxyFailureUpdate>(options);
        }
        
        /// <summary>
        /// Queue a success update (fire-and-forget)
        /// </summary>
        public void RecordSuccess(int proxyId)
        {
            _successChannel.Writer.TryWrite(new ProxySuccessUpdate
            {
                ProxyId = proxyId,
                Timestamp = DateTime.UtcNow
            });
        }
        
        /// <summary>
        /// Queue a failure update (fire-and-forget)
        /// </summary>
        public void RecordFailure(int proxyId, string reason, bool isIntermittent = false, bool isTimeout = false)
        {
            _failureChannel.Writer.TryWrite(new ProxyFailureUpdate
            {
                ProxyId = proxyId,
                Reason = reason,
                IsIntermittent = isIntermittent,
                IsTimeout = isTimeout,
                Timestamp = DateTime.UtcNow
            });
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ProxyStatsQueueService started");
            
            // Xử lý song song cả success và failure channels
            var successTask = ProcessSuccessChannelAsync(stoppingToken);
            var failureTask = ProcessFailureChannelAsync(stoppingToken);
            var flushTask = PeriodicFlushAsync(stoppingToken);
            
            await Task.WhenAll(successTask, failureTask, flushTask);
            
            _logger.LogInformation("ProxyStatsQueueService stopped");
        }
        
        private async Task ProcessSuccessChannelAsync(CancellationToken stoppingToken)
        {
            await foreach (var update in _successChannel.Reader.ReadAllAsync(stoppingToken))
            {
                var aggregate = _pendingUpdates.GetOrAdd(update.ProxyId, id => new ProxyStatsAggregate { ProxyId = id });
                aggregate.SuccessCount++;
                aggregate.LastSuccessAt = update.Timestamp;
            }
        }
        
        private async Task ProcessFailureChannelAsync(CancellationToken stoppingToken)
        {
            await foreach (var update in _failureChannel.Reader.ReadAllAsync(stoppingToken))
            {
                var aggregate = _pendingUpdates.GetOrAdd(update.ProxyId, id => new ProxyStatsAggregate { ProxyId = id });
                
                if (update.IsTimeout)
                {
                    aggregate.TimeoutCount++;
                }
                else if (update.IsIntermittent)
                {
                    aggregate.IntermittentFailureCount++;
                }
                else
                {
                    aggregate.FailureCount++;
                }
                
                aggregate.LastFailureAt = update.Timestamp;
                aggregate.LastFailureReason = update.Reason;
            }
        }
        
        private async Task PeriodicFlushAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(BATCH_INTERVAL_MS, stoppingToken);
                    await FlushPendingUpdatesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in periodic flush");
                }
            }
            
            // Final flush on shutdown
            try
            {
                await FlushPendingUpdatesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in final flush");
            }
        }
        
        private async Task FlushPendingUpdatesAsync(CancellationToken stoppingToken)
        {
            if (_pendingUpdates.IsEmpty) return;
            
            // Lấy tất cả pending updates và clear
            var updates = new List<ProxyStatsAggregate>();
            foreach (var kvp in _pendingUpdates)
            {
                if (_pendingUpdates.TryRemove(kvp.Key, out var aggregate))
                {
                    updates.Add(aggregate);
                    if (updates.Count >= MAX_BATCH_SIZE) break;
                }
            }
            
            if (!updates.Any()) return;
            
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxyIds = updates.Select(u => u.ProxyId).ToList();
                var proxies = await context.Proxies
                    .Where(p => proxyIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, stoppingToken);
                
                foreach (var update in updates)
                {
                    if (!proxies.TryGetValue(update.ProxyId, out var proxy)) continue;
                    
                    // Apply success updates
                    if (update.SuccessCount > 0)
                    {
                        proxy.UsageCount += update.SuccessCount;
                        proxy.LastUsedAt = update.LastSuccessAt;
                        
                        // Reset failure count on success
                        if (proxy.FailureCount > 0)
                        {
                            proxy.FailureCount = 0;
                            proxy.LastFailureReason = null;
                        }
                    }
                    
                    // Apply failure updates
                    int totalFailures = update.FailureCount + update.IntermittentFailureCount;
                    // Timeout không tính là failure nghiêm trọng
                    
                    if (totalFailures > 0)
                    {
                        proxy.FailureCount += totalFailures;
                        proxy.LastFailedAt = update.LastFailureAt;
                        
                        if (!string.IsNullOrEmpty(update.LastFailureReason))
                        {
                            proxy.LastFailureReason = update.LastFailureReason.Length > 500
                                ? update.LastFailureReason.Substring(0, 500)
                                : update.LastFailureReason;
                        }
                    }
                }
                
                await context.SaveChangesAsync(stoppingToken);
                
                _logger.LogDebug("Flushed {Count} proxy stat updates to database", updates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing {Count} proxy stat updates", updates.Count);
                
                // Re-queue failed updates
                foreach (var update in updates)
                {
                    _pendingUpdates.TryAdd(update.ProxyId, update);
                }
            }
        }
        
        private record ProxySuccessUpdate
        {
            public int ProxyId { get; init; }
            public DateTime Timestamp { get; init; }
        }
        
        private record ProxyFailureUpdate
        {
            public int ProxyId { get; init; }
            public string Reason { get; init; } = "";
            public bool IsIntermittent { get; init; }
            public bool IsTimeout { get; init; }
            public DateTime Timestamp { get; init; }
        }
        
        private class ProxyStatsAggregate
        {
            public int ProxyId { get; init; }
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public int IntermittentFailureCount { get; set; }
            public int TimeoutCount { get; set; }
            public DateTime? LastSuccessAt { get; set; }
            public DateTime? LastFailureAt { get; set; }
            public string? LastFailureReason { get; set; }
        }
    }
}
