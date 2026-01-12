using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Collections.Concurrent;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service quản lý giới hạn Request/Phút (RPM) cho từng Proxy.
    /// Chỉ tính request khi kết nối thành công đến API Gemini.
    /// </summary>
    public class ProxyRateLimiterService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProxyRateLimiterService> _logger;

        // Constants
        private const int DEFAULT_RPM_PER_PROXY = 10;
        private static readonly TimeSpan RPM_WINDOW = TimeSpan.FromMinutes(1);

        // Dictionary chứa semaphore cho mỗi proxy (key = proxyId)
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _proxySemaphores = new();
        
        // Track capacity hiện tại cho mỗi proxy để phát hiện thay đổi settings
        private readonly ConcurrentDictionary<int, int> _proxyCapacities = new();
        
        // Track các slot đang active cho mỗi proxy với thời gian release
        private readonly ConcurrentDictionary<string, (int ProxyId, DateTime ReleaseTime, CancellationTokenSource AutoReleaseCts)> _activeSlots = new();
        
        // Cache cho RPM settings
        private volatile int _currentRpmPerProxy = DEFAULT_RPM_PER_PROXY;
        private DateTime _lastSettingsRefresh = DateTime.MinValue;
        private readonly TimeSpan _settingsRefreshInterval = TimeSpan.FromSeconds(5);
        private readonly object _configLock = new();

        public ProxyRateLimiterService(
            IServiceProvider serviceProvider,
            ILogger<ProxyRateLimiterService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            _logger.LogInformation("ProxyRateLimiterService initialized with default RPM: {Rpm}", _currentRpmPerProxy);
        }

        /// <summary>
        /// Refresh settings từ database nếu cần.
        /// </summary>
        public async Task RefreshSettingsAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.UtcNow - _lastSettingsRefresh < _settingsRefreshInterval)
            {
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.LocalApiSettings.FindAsync(1);

                _lastSettingsRefresh = DateTime.UtcNow;

                if (settings == null)
                {
                    _logger.LogWarning("LocalApiSettings not found in database, using default RPM per proxy: {Rpm}", _currentRpmPerProxy);
                    return;
                }

                int newRpmPerProxy = settings.RpmPerProxy > 0 ? settings.RpmPerProxy : DEFAULT_RPM_PER_PROXY;

                lock (_configLock)
                {
                    if (newRpmPerProxy != _currentRpmPerProxy)
                    {
                        _logger.LogInformation(
                            "Updating ProxyRateLimiter RPM: {OldRpm} -> {NewRpm}",
                            _currentRpmPerProxy, newRpmPerProxy);

                        _currentRpmPerProxy = newRpmPerProxy;

                        // Clear semaphores cũ - chúng sẽ được tạo lại với capacity mới khi cần
                        foreach (var kvp in _proxySemaphores)
                        {
                            try { kvp.Value.Dispose(); }
                            catch { /* Ignore dispose errors */ }
                        }
                        _proxySemaphores.Clear();
                        _proxyCapacities.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing ProxyRateLimiter settings");
            }
        }

        /// <summary>
        /// Lấy RPM hiện tại được cấu hình.
        /// </summary>
        public int GetCurrentRpmPerProxy() => _currentRpmPerProxy;

        /// <summary>
        /// Kiểm tra xem proxy có slot khả dụng không (không block).
        /// </summary>
        public bool HasAvailableSlot(int proxyId)
        {
            var semaphore = GetOrCreateSemaphore(proxyId);
            return semaphore.CurrentCount > 0;
        }

        /// <summary>
        /// Lấy số slot khả dụng của proxy.
        /// </summary>
        public int GetAvailableSlots(int proxyId)
        {
            var semaphore = GetOrCreateSemaphore(proxyId);
            return semaphore.CurrentCount;
        }

        /// <summary>
        /// Thử acquire slot cho proxy (không block).
        /// Trả về slotId nếu thành công, null nếu không có slot.
        /// </summary>
        public async Task<string?> TryAcquireSlotAsync(int proxyId, string requestId, CancellationToken cancellationToken = default)
        {
            await RefreshSettingsAsync();
            
            var semaphore = GetOrCreateSemaphore(proxyId);
            
            // Thử lấy slot ngay lập tức (timeout = 0)
            if (await semaphore.WaitAsync(0, cancellationToken))
            {
                return RegisterSlot(proxyId, requestId, semaphore);
            }
            
            return null;
        }

        /// <summary>
        /// Acquire slot cho proxy (block nếu cần đợi).
        /// Trả về slotId.
        /// </summary>
        public async Task<string> AcquireSlotAsync(int proxyId, string requestId, CancellationToken cancellationToken = default)
        {
            await RefreshSettingsAsync();
            
            var semaphore = GetOrCreateSemaphore(proxyId);
            var startWait = DateTime.UtcNow;
            
            _logger.LogDebug(
                "Request {RequestId} waiting for proxy {ProxyId} slot. Available: {Available}/{Max}",
                requestId, proxyId, semaphore.CurrentCount, _currentRpmPerProxy);
            
            await semaphore.WaitAsync(cancellationToken);
            
            var waitTime = DateTime.UtcNow - startWait;
            if (waitTime.TotalMilliseconds > 100)
            {
                _logger.LogInformation(
                    "Request {RequestId} waited {WaitMs}ms for proxy {ProxyId} slot",
                    requestId, waitTime.TotalMilliseconds, proxyId);
            }
            
            return RegisterSlot(proxyId, requestId, semaphore);
        }

        /// <summary>
        /// Acquire slot với timeout. Trả về null nếu timeout.
        /// </summary>
        public async Task<string?> TryAcquireSlotWithTimeoutAsync(int proxyId, string requestId, int timeoutMs, CancellationToken cancellationToken = default)
        {
            await RefreshSettingsAsync();
            
            var semaphore = GetOrCreateSemaphore(proxyId);
            
            if (await semaphore.WaitAsync(timeoutMs, cancellationToken))
            {
                return RegisterSlot(proxyId, requestId, semaphore);
            }
            
            _logger.LogDebug(
                "Request {RequestId} timed out waiting for proxy {ProxyId} slot after {TimeoutMs}ms",
                requestId, proxyId, timeoutMs);
            
            return null;
        }

        /// <summary>
        /// Giải phóng slot sau khi request hoàn thành THÀNH CÔNG đến API Gemini.
        /// Slot sẽ tự động được giải phóng sau 1 phút.
        /// Gọi method này khi cần giải phóng sớm hơn (request thất bại do lỗi kết nối proxy).
        /// </summary>
        public void ReleaseSlotEarly(string slotId)
        {
            if (_activeSlots.TryRemove(slotId, out var entry))
            {
                // Hủy auto-release timer
                try
                {
                    entry.AutoReleaseCts.Cancel();
                    entry.AutoReleaseCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
                }

                // Release semaphore
                var semaphore = GetOrCreateSemaphore(entry.ProxyId);
                try
                {
                    semaphore.Release();
                    _logger.LogDebug(
                        "Released slot {SlotId} early for proxy {ProxyId}. Available: {Available}/{Max}",
                        slotId, entry.ProxyId, semaphore.CurrentCount, _currentRpmPerProxy);
                }
                catch (SemaphoreFullException)
                {
                    _logger.LogDebug("Slot {SlotId} was already released", slotId);
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogDebug("Semaphore for proxy {ProxyId} was disposed", entry.ProxyId);
                }
            }
        }

        /// <summary>
        /// Đánh dấu request đã thành công kết nối đến API Gemini.
        /// Slot sẽ được giữ và tự động release sau 1 phút (cho RPM window).
        /// Không cần gọi gì thêm - slot sẽ tự release.
        /// </summary>
        public void MarkSlotAsUsed(string slotId)
        {
            // Slot đã được đăng ký với auto-release timer
            // Không cần làm gì thêm - chỉ log để tracking
            if (_activeSlots.ContainsKey(slotId))
            {
                _logger.LogDebug("Slot {SlotId} marked as successfully used, will auto-release after 1 minute", slotId);
            }
        }

        /// <summary>
        /// Lấy thông tin trạng thái của proxy.
        /// </summary>
        public (int rpmPerProxy, int availableSlots, int activeSlots) GetProxyStatus(int proxyId)
        {
            var semaphore = GetOrCreateSemaphore(proxyId);
            int activeCount = _activeSlots.Values.Count(s => s.ProxyId == proxyId);
            return (_currentRpmPerProxy, semaphore.CurrentCount, activeCount);
        }

        /// <summary>
        /// Lấy hoặc tạo semaphore cho proxy.
        /// </summary>
        private SemaphoreSlim GetOrCreateSemaphore(int proxyId)
        {
            return _proxySemaphores.GetOrAdd(proxyId, id =>
            {
                _proxyCapacities[id] = _currentRpmPerProxy;
                _logger.LogDebug("Created semaphore for proxy {ProxyId} with capacity {Capacity}", id, _currentRpmPerProxy);
                return new SemaphoreSlim(_currentRpmPerProxy, _currentRpmPerProxy);
            });
        }

        /// <summary>
        /// Đăng ký slot sau khi acquire thành công.
        /// </summary>
        private string RegisterSlot(int proxyId, string requestId, SemaphoreSlim semaphore)
        {
            var slotId = $"{requestId}_proxy{proxyId}_{Guid.NewGuid():N}";
            var autoReleaseCts = new CancellationTokenSource();
            var releaseTime = DateTime.UtcNow.Add(RPM_WINDOW);
            
            _activeSlots[slotId] = (proxyId, releaseTime, autoReleaseCts);
            
            _logger.LogDebug(
                "Acquired slot {SlotId} for proxy {ProxyId}. Available: {Available}/{Max}",
                slotId, proxyId, semaphore.CurrentCount, _currentRpmPerProxy);
            
            // Schedule auto-release sau RPM window (1 phút) sử dụng Timer
            ScheduleAutoRelease(slotId, proxyId, semaphore, autoReleaseCts.Token);
            
            return slotId;
        }

        /// <summary>
        /// Lên lịch tự động release slot sau RPM window sử dụng Timer.
        /// Timer hiệu quả hơn Task.Run vì không tạo thread mới cho mỗi slot.
        /// </summary>
        private void ScheduleAutoRelease(string slotId, int proxyId, SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            // Use Timer for more efficient scheduling
            Timer? timer = null;
            timer = new Timer(_ =>
            {
                // Check if cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    timer?.Dispose();
                    return;
                }
                
                try
                {
                    if (_activeSlots.TryRemove(slotId, out var entry))
                    {
                        try { entry.AutoReleaseCts.Dispose(); }
                        catch { /* Ignore */ }

                        try
                        {
                            semaphore.Release();
                            _logger.LogDebug(
                                "Auto-released slot {SlotId} for proxy {ProxyId} after {Window}",
                                slotId, proxyId, RPM_WINDOW);
                        }
                        catch (SemaphoreFullException)
                        {
                            // Already released
                        }
                        catch (ObjectDisposedException)
                        {
                            // Semaphore disposed
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in auto-release for slot {SlotId}", slotId);
                }
                finally
                {
                    timer?.Dispose();
                }
            }, null, RPM_WINDOW, Timeout.InfiniteTimeSpan);
            
            // Register cancellation to dispose timer if slot is released early
            cancellationToken.Register(() =>
            {
                try { timer?.Dispose(); }
                catch { /* Ignore */ }
            });
        }
    }
}
