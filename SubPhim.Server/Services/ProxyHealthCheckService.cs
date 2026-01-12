using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service để kiểm tra sức khỏe và độ trễ của proxy.
    /// Kiểm tra kết nối đến Google để đảm bảo proxy hoạt động với Gemini API.
    /// </summary>
    public class ProxyHealthCheckService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProxyHealthCheckService> _logger;
        private readonly ProxyService _proxyService;
        
        // Constants for health check
        private const int CHECK_TIMEOUT_SECONDS = 15;
        private const int MAX_CONCURRENT_CHECKS = 5; // Giới hạn kiểm tra đồng thời
        private const int DELAY_BETWEEN_CHECKS_MS = 200; // Delay giữa các lần check
        private const string GOOGLE_TEST_URL = "https://generativelanguage.googleapis.com/"; // URL Google AI để test
        
        // Track ongoing checks
        private static readonly ConcurrentDictionary<int, bool> _ongoingChecks = new();
        private static volatile bool _isBatchCheckRunning = false;
        private static readonly object _batchCheckLock = new();
        
        public ProxyHealthCheckService(
            IServiceProvider serviceProvider,
            ILogger<ProxyHealthCheckService> logger,
            ProxyService proxyService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _proxyService = proxyService;
        }
        
        /// <summary>
        /// Kiểm tra độ trễ của một proxy cụ thể
        /// </summary>
        public async Task<(bool success, int latencyMs, string status)> CheckProxyLatencyAsync(
            Proxy proxy, CancellationToken cancellationToken = default)
        {
            if (_ongoingChecks.ContainsKey(proxy.Id))
            {
                return (false, -1, "Check đang chạy");
            }
            
            _ongoingChecks[proxy.Id] = true;
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                using var httpClient = _proxyService.CreateHttpClientWithProxy(proxy);
                httpClient.Timeout = TimeSpan.FromSeconds(CHECK_TIMEOUT_SECONDS);
                
                // Chỉ gửi HEAD request để giảm tải
                using var request = new HttpRequestMessage(HttpMethod.Head, GOOGLE_TEST_URL);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0");
                
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                stopwatch.Stop();
                
                int latencyMs = (int)stopwatch.ElapsedMilliseconds;
                
                // Status code 200, 403, 405 đều hợp lệ (nghĩa là proxy hoạt động)
                if (response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Proxy {Id} ({Host}:{Port}) - Latency: {LatencyMs}ms", 
                        proxy.Id, proxy.Host, proxy.Port, latencyMs);
                    return (true, latencyMs, "OK");
                }
                else
                {
                    return (false, latencyMs, $"HTTP {(int)response.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return (false, (int)stopwatch.ElapsedMilliseconds, "Timeout");
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                var shortMessage = ex.Message.Length > 80 ? ex.Message.Substring(0, 80) + "..." : ex.Message;
                return (false, -1, $"Error: {shortMessage}");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var shortMessage = ex.Message.Length > 80 ? ex.Message.Substring(0, 80) + "..." : ex.Message;
                return (false, -1, $"Error: {shortMessage}");
            }
            finally
            {
                _ongoingChecks.TryRemove(proxy.Id, out _);
            }
        }
        
        /// <summary>
        /// Kiểm tra và cập nhật độ trễ vào database
        /// </summary>
        public async Task CheckAndUpdateProxyLatencyAsync(int proxyId, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var proxy = await context.Proxies.FindAsync(new object[] { proxyId }, cancellationToken);
            if (proxy == null)
            {
                _logger.LogWarning("Proxy ID {Id} not found", proxyId);
                return;
            }
            
            var (success, latencyMs, status) = await CheckProxyLatencyAsync(proxy, cancellationToken);
            
            proxy.LatencyMs = success ? latencyMs : null;
            proxy.LastLatencyCheckUtc = DateTime.UtcNow;
            proxy.LatencyCheckStatus = status;
            
            // Nếu check thất bại và proxy đang enabled, tăng failure count
            if (!success && proxy.IsEnabled)
            {
                proxy.FailureCount++;
                proxy.LastFailedAt = DateTime.UtcNow;
                proxy.LastFailureReason = $"Health check failed: {status}";
            }
            
            await context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Proxy {Id} ({Host}:{Port}) health check: {Status}, Latency: {LatencyMs}ms",
                proxy.Id, proxy.Host, proxy.Port, status, latencyMs);
        }
        
        /// <summary>
        /// Kiểm tra tất cả proxy với throttling để không gây quá tải
        /// </summary>
        public async Task CheckAllProxiesAsync(CancellationToken cancellationToken = default)
        {
            lock (_batchCheckLock)
            {
                if (_isBatchCheckRunning)
                {
                    _logger.LogWarning("Batch proxy check is already running. Skipping...");
                    return;
                }
                _isBatchCheckRunning = true;
            }
            
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var allProxies = await context.Proxies
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.Id)
                    .ToListAsync(cancellationToken);
                
                if (!allProxies.Any())
                {
                    _logger.LogInformation("No enabled proxies to check");
                    return;
                }
                
                _logger.LogInformation("Starting batch health check for {Count} proxies (max {MaxConcurrent} concurrent)...",
                    allProxies.Count, MAX_CONCURRENT_CHECKS);
                
                var semaphore = new SemaphoreSlim(MAX_CONCURRENT_CHECKS);
                var tasks = new List<Task>();
                int checkedCount = 0;
                int successCount = 0;
                
                foreach (var proxy in allProxies)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    await semaphore.WaitAsync(cancellationToken);
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var (success, latencyMs, status) = await CheckProxyLatencyAsync(proxy, cancellationToken);
                            
                            // Update trong scope riêng để tránh conflict
                            using var updateScope = _serviceProvider.CreateScope();
                            var updateContext = updateScope.ServiceProvider.GetRequiredService<AppDbContext>();
                            
                            var proxyToUpdate = await updateContext.Proxies.FindAsync(new object[] { proxy.Id }, cancellationToken);
                            if (proxyToUpdate != null)
                            {
                                proxyToUpdate.LatencyMs = success ? latencyMs : null;
                                proxyToUpdate.LastLatencyCheckUtc = DateTime.UtcNow;
                                proxyToUpdate.LatencyCheckStatus = status;
                                
                                await updateContext.SaveChangesAsync(cancellationToken);
                            }
                            
                            Interlocked.Increment(ref checkedCount);
                            if (success) Interlocked.Increment(ref successCount);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                    
                    // Delay nhỏ giữa các lần khởi chạy check để tránh spike
                    await Task.Delay(DELAY_BETWEEN_CHECKS_MS, cancellationToken);
                }
                
                await Task.WhenAll(tasks);
                
                // Refresh proxy cache sau khi check xong
                _proxyService.RefreshCache();
                
                _logger.LogInformation("Batch health check completed: {Success}/{Total} proxies OK",
                    successCount, checkedCount);
            }
            finally
            {
                _isBatchCheckRunning = false;
            }
        }
        
        /// <summary>
        /// Kiểm tra xem batch check có đang chạy không
        /// </summary>
        public bool IsBatchCheckRunning() => _isBatchCheckRunning;
        
        /// <summary>
        /// Kiểm tra một proxy cụ thể (fire-and-forget cho khi thêm proxy mới)
        /// </summary>
        public void QueueProxyCheck(int proxyId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Delay một chút để đảm bảo proxy đã được save
                    await Task.Delay(500);
                    await CheckAndUpdateProxyLatencyAsync(proxyId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in queued proxy check for ID {ProxyId}", proxyId);
                }
            });
        }
    }
}
