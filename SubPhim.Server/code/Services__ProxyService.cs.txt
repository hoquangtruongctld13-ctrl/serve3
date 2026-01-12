using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service for managing and rotating proxies for Google API HTTP requests.
    /// Supports SOCKS4, SOCKS5 and HTTP proxies.
    /// </summary>
    public class ProxyService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProxyService> _logger;
        
        // Configuration constants
        private const int MaxConsecutiveFailuresBeforeDisable = 5;
        private const int MaxFailureReasonLength = 500;
        private const int ProxyConnectTimeoutSeconds = 10; // Gi?m t? 30s xu?ng 10s ?? fail fast
        private const int HttpClientTimeoutMinutes = 3; // Gi?m t? 5 ph�t xu?ng 3 ph�t
        
        // Database retry configuration
        private const int MaxDatabaseRetries = 3;
        private const int InitialRetryDelayMs = 100;
        private const int MaxRetryDelayMs = 1000;
        
        // Round-robin index for proxy selection
        private static int _proxyRoundRobinIndex = 0;
        private static readonly object _proxyLock = new();
        
        // Proxy cache to avoid excessive DB queries
        // Using separate lock for cache to avoid deadlocks with proxy selection lock
        private static readonly object _cacheLock = new();
        private static List<Proxy> _cachedProxies = new();
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
        private static volatile bool _cacheRefreshInProgress = false;
        
        // Track when refresh started to detect stuck refresh operations
        private static DateTime _refreshStartedAt = DateTime.MinValue;
        private static readonly TimeSpan MaxRefreshWaitTime = TimeSpan.FromSeconds(30);

        public ProxyService(IServiceProvider serviceProvider, ILogger<ProxyService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Execute a database operation with retry logic for handling SQLite lock errors.
        /// Uses exponential backoff to avoid overwhelming the database.
        /// </summary>
        private async Task ExecuteWithRetryAsync(Func<Task> operation, string operationName)
        {
            int retryCount = 0;
            int delayMs = InitialRetryDelayMs;
            
            while (true)
            {
                try
                {
                    await operation();
                    return; // Success
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5 && retryCount < MaxDatabaseRetries)
                {
                    // SQLite Error 5: database is locked
                    retryCount++;
                    _logger.LogWarning("Database locked during {OperationName} (attempt {Retry}/{Max}). Retrying after {Delay}ms...", 
                        operationName, retryCount, MaxDatabaseRetries, delayMs);
                    
                    await Task.Delay(delayMs);
                    delayMs = CalculateNextDelay(delayMs);
                }
                catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqliteEx && 
                                                   sqliteEx.SqliteErrorCode == 5 && 
                                                   retryCount < MaxDatabaseRetries)
                {
                    // Handle DbUpdateException wrapping SQLite lock error
                    retryCount++;
                    _logger.LogWarning("Database locked during {OperationName} (attempt {Retry}/{Max}). Retrying after {Delay}ms...", 
                        operationName, retryCount, MaxDatabaseRetries, delayMs);
                    
                    await Task.Delay(delayMs);
                    delayMs = CalculateNextDelay(delayMs);
                }
                catch (Exception ex)
                {
                    // Total attempts = retryCount + 1 (initial attempt)
                    _logger.LogWarning(ex, "Failed to execute {OperationName} after {TotalAttempts} attempts ({Retries} retries)", 
                        operationName, retryCount + 1, retryCount);
                    throw;
                }
            }
        }

        /// <summary>
        /// Calculate the next retry delay using exponential backoff with jitter.
        /// Random.Shared is thread-safe (introduced in .NET 6) and designed for concurrent access.
        /// </summary>
        private static int CalculateNextDelay(int currentDelayMs)
        {
            return Math.Min(currentDelayMs * 2 + Random.Shared.Next(0, 100), MaxRetryDelayMs);
        }

        /// <summary>
        /// Get the next proxy using round-robin selection.
        /// </summary>
        /// <param name="excludeProxyIds">Optional set of proxy IDs to exclude (e.g., proxies that failed in current request)</param>
        public async Task<Proxy?> GetNextProxyAsync(HashSet<int>? excludeProxyIds = null)
        {
            var proxies = await GetEnabledProxiesAsync();
            
            // Filter out excluded proxies
            if (excludeProxyIds != null && excludeProxyIds.Count > 0)
            {
                proxies = proxies.Where(p => !excludeProxyIds.Contains(p.Id)).ToList();
            }
            
            if (!proxies.Any())
            {
                // AUTO-RECOVERY: N?u kh�ng c�n proxy n�o ???c b?t, t? ??ng b?t l?i c�c proxy c� �t l?i
                var reEnabledCount = await TryAutoReEnableProxiesAsync();
                
                if (reEnabledCount > 0)
                {
                    _logger.LogWarning("?? AUTO-RECOVERY: Re-enabled {Count} proxies with low failure count. Retrying proxy selection...", reEnabledCount);
                    
                    // Refresh cache v� th? l?i
                    proxies = await GetEnabledProxiesAsync();
                    
                    // Filter l?i n?u c?n
                    if (excludeProxyIds != null && excludeProxyIds.Count > 0)
                    {
                        proxies = proxies.Where(p => !excludeProxyIds.Contains(p.Id)).ToList();
                    }
                }
                
                if (!proxies.Any())
                {
                    if (excludeProxyIds != null && excludeProxyIds.Count > 0)
                    {
                        _logger.LogWarning("No proxies available after excluding {ExcludeCount} failed proxies. Will use direct connection.", 
                            excludeProxyIds.Count);
                    }
                    else
                    {
                        _logger.LogWarning("No proxies are enabled. Requests will be sent directly.");
                    }
                    return null;
                }
            }

            lock (_proxyLock)
            {
                if (_proxyRoundRobinIndex >= proxies.Count)
                    _proxyRoundRobinIndex = 0;
                
                var selectedProxy = proxies[_proxyRoundRobinIndex];
                _proxyRoundRobinIndex++;
                
                _logger.LogDebug("Selected proxy {ProxyId}: {Host}:{Port} ({Type})", 
                    selectedProxy.Id, selectedProxy.Host, selectedProxy.Port, selectedProxy.Type);
                
                return selectedProxy;
            }
        }
        
        /// <summary>
        /// Auto-recovery: Re-enable proxies with low failure count when all proxies are disabled.
        /// This prevents the system from being stuck without proxies due to transient errors.
        /// Only re-enables proxies with fewer than MaxFailureCountForAutoReEnable failures.
        /// </summary>
        private const int MaxFailureCountForAutoReEnable = 10;
        
        private async Task<int> TryAutoReEnableProxiesAsync()
        {
            try
            {
                // Use a captured variable to return the count from the retry operation
                // This is a standard closure pattern in C#
                int reenabledCount = 0;
                
                await ExecuteWithRetryAsync(async () =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // Ki?m tra xem c� proxy n�o ?ang enabled kh�ng
                    var enabledCount = await context.Proxies.CountAsync(p => p.IsEnabled);
                    
                    if (enabledCount > 0)
                    {
                        // V?n c�n proxy enabled, kh�ng c?n auto-recovery
                        reenabledCount = 0;
                        return;
                    }
                    
                    // T�m c�c proxy b? t?t c� �t h?n MaxFailureCountForAutoReEnable l?n l?i
                    var proxiesToReEnable = await context.Proxies
                        .Where(p => !p.IsEnabled && p.FailureCount < MaxFailureCountForAutoReEnable)
                        .ToListAsync();
                    
                    if (!proxiesToReEnable.Any())
                    {
                        _logger.LogWarning("?? AUTO-RECOVERY: No proxies eligible for re-enabling. All proxies have {MaxFailures}+ failures.", 
                            MaxFailureCountForAutoReEnable);
                        reenabledCount = 0;
                        return;
                    }
                    
                    // B?t l?i c�c proxy n�y v� reset failure count v? 0
                    foreach (var proxy in proxiesToReEnable)
                    {
                        proxy.IsEnabled = true;
                        proxy.FailureCount = 0; // Reset failure count ?? cho c? h?i m?i
                        proxy.LastFailureReason = $"[AUTO-RECOVERY] Re-enabled at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
                        
                        _logger.LogInformation("?? AUTO-RECOVERY: Re-enabling proxy {ProxyId} ({Host}:{Port}) - previous failures: {PreviousFailures}", 
                            proxy.Id, proxy.Host, proxy.Port, proxy.FailureCount);
                    }
                    
                    await context.SaveChangesAsync();
                    
                    // Force refresh cache immediately with the new data
                    // Don't just invalidate - actually update the cache with fresh data
                    lock (_cacheLock)
                    {
                        _cachedProxies = proxiesToReEnable; // Directly set the re-enabled proxies
                        _lastCacheUpdate = DateTime.UtcNow;
                        _cacheRefreshInProgress = false;
                    }
                    
                    reenabledCount = proxiesToReEnable.Count;
                    
                    _logger.LogWarning("?? AUTO-RECOVERY COMPLETE: Re-enabled {Count} proxies with < {MaxFailures} failures", 
                        reenabledCount, MaxFailureCountForAutoReEnable);
                }, "TryAutoReEnableProxies");
                
                return reenabledCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto re-enable proxies");
                return 0;
            }
        }

        /// <summary>
        /// Get list of enabled proxies (with caching).
        /// Thread-safe implementation to avoid race conditions.
        /// </summary>
        /// <param name="forceRefresh">If true, bypass cache and force refresh from database</param>
        private async Task<List<Proxy>> GetEnabledProxiesAsync(bool forceRefresh = false)
        {
            bool needsRefresh;
            bool shouldTakeOverRefresh = false;
            
            // First check: read cache state under lock
            lock (_cacheLock)
            {
                needsRefresh = forceRefresh || 
                               DateTime.UtcNow - _lastCacheUpdate >= CacheDuration || 
                               !_cachedProxies.Any();
                
                if (!needsRefresh)
                {
                    // Return a copy to prevent external modification of cache
                    return _cachedProxies.ToList();
                }
                
                // Check if another thread is refreshing
                if (_cacheRefreshInProgress)
                {
                    // Check if the refresh is stuck (taking too long)
                    var refreshDuration = DateTime.UtcNow - _refreshStartedAt;
                    if (refreshDuration > MaxRefreshWaitTime)
                    {
                        // Refresh is stuck - take over
                        _logger.LogWarning(
                            "?? Cache refresh appears stuck (running for {Duration:F1}s). Taking over refresh operation.",
                            refreshDuration.TotalSeconds);
                        shouldTakeOverRefresh = true;
                        // Will set _cacheRefreshInProgress below
                    }
                    else
                    {
                        // Return current cache while refresh is in progress
                        // This prevents multiple DB queries and potential race conditions
                        return _cachedProxies.ToList();
                    }
                }
                
                // Mark refresh in progress and record start time
                _cacheRefreshInProgress = true;
                _refreshStartedAt = DateTime.UtcNow;
            }
            
            try
            {
                if (shouldTakeOverRefresh)
                {
                    _logger.LogInformation("Taking over stuck cache refresh operation");
                }
                
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var freshProxies = await context.Proxies
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.Id)
                    .AsNoTracking() // Improve performance, we don't need to track these
                    .ToListAsync();
                
                // Update cache under lock
                lock (_cacheLock)
                {
                    _cachedProxies = freshProxies;
                    _lastCacheUpdate = DateTime.UtcNow;
                    _cacheRefreshInProgress = false;
                    
                    _logger.LogInformation("Refreshed proxy cache: {Count} proxies available", _cachedProxies.Count);
                    
                    // Return a copy to prevent external modification
                    return _cachedProxies.ToList();
                }
            }
            catch (Exception ex)
            {
                // Reset refresh flag on error - CRITICAL: must always reset to prevent stuck state
                lock (_cacheLock)
                {
                    _cacheRefreshInProgress = false;
                }
                
                _logger.LogError(ex, "Failed to refresh proxy cache from database");
                
                // Return current cache (even if stale) on error
                lock (_cacheLock)
                {
                    return _cachedProxies.ToList();
                }
            }
        }

        /// <summary>
        /// Invalidate proxy cache (call when changes are made from Admin panel).
        /// Thread-safe implementation.
        /// </summary>
        public void RefreshCache()
        {
            lock (_cacheLock)
            {
                _lastCacheUpdate = DateTime.MinValue;
                // Also clear the cached list to force immediate refresh
                _cachedProxies = new List<Proxy>();
                // IMPORTANT: Also reset the refresh flag to allow immediate refresh
                // This prevents stuck state if RefreshCache is called while refresh is in progress
                _cacheRefreshInProgress = false;
                _logger.LogInformation("Proxy cache invalidated and cleared, will refresh on next request");
            }
        }
        
        /// <summary>
        /// Force refresh cache from database immediately.
        /// Use this when you need to ensure cache is up-to-date right away.
        /// </summary>
        public async Task ForceRefreshCacheAsync()
        {
            _logger.LogInformation("Force refreshing proxy cache from database...");
            await GetEnabledProxiesAsync(forceRefresh: true);
        }

        /// <summary>
        /// Create HttpClient with the configured proxy.
        /// </summary>
        public HttpClient CreateHttpClientWithProxy(Proxy? proxy)
        {
            if (proxy == null)
            {
                _logger.LogDebug("No proxy specified, creating direct HttpClient");
                return new HttpClient { Timeout = TimeSpan.FromMinutes(HttpClientTimeoutMinutes) };
            }

            var handler = CreateHttpMessageHandler(proxy);
            var httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(HttpClientTimeoutMinutes)
            };

            _logger.LogDebug("Created HttpClient with proxy {ProxyId}: {Type}://{Host}:{Port}", 
                proxy.Id, proxy.Type, proxy.Host, proxy.Port);

            return httpClient;
        }

        /// <summary>
        /// Create HttpMessageHandler based on proxy type.
        /// </summary>
        private HttpMessageHandler CreateHttpMessageHandler(Proxy proxy)
        {
            switch (proxy.Type)
            {
                case ProxyType.Http:
                    return CreateHttpProxyHandler(proxy);
                    
                case ProxyType.Socks4:
                case ProxyType.Socks5:
                    return CreateSocksProxyHandler(proxy);
                    
                default:
                    _logger.LogWarning("Unknown proxy type {Type}, using direct connection", proxy.Type);
                    return new HttpClientHandler();
            }
        }

        /// <summary>
        /// Check if proxy has valid authentication credentials.
        /// </summary>
        private static bool HasCredentials(Proxy proxy)
        {
            return !string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password);
        }

        /// <summary>
        /// Build proxy URI with optional embedded credentials.
        /// </summary>
        private static Uri BuildProxyUri(string scheme, Proxy proxy)
        {
            if (HasCredentials(proxy))
            {
                // URL-encode username and password to handle special characters
                var encodedUsername = Uri.EscapeDataString(proxy.Username!);
                var encodedPassword = Uri.EscapeDataString(proxy.Password!);
                return new Uri($"{scheme}://{encodedUsername}:{encodedPassword}@{proxy.Host}:{proxy.Port}");
            }
            return new Uri($"{scheme}://{proxy.Host}:{proxy.Port}");
        }

        /// <summary>
        /// Create handler for HTTP proxy.
        /// Uses SocketsHttpHandler with DefaultProxyCredentials for proper HTTPS tunneling
        /// authentication on Linux/Google Cloud VM environments.
        /// </summary>
        private SocketsHttpHandler CreateHttpProxyHandler(Proxy proxy)
        {
            // Build proxy URI with embedded credentials for HTTPS CONNECT tunnel authentication
            // This approach works better on Linux environments and Google Cloud VMs
            var proxyUri = BuildProxyUri("http", proxy);
            var webProxy = new WebProxy(proxyUri);
            
            // Also set credentials on WebProxy for compatibility with some proxy servers
            if (HasCredentials(proxy))
            {
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            }

            var handler = new SocketsHttpHandler
            {
                Proxy = webProxy,
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(ProxyConnectTimeoutSeconds),
                // Set default proxy credentials for CONNECT tunnel authentication
                DefaultProxyCredentials = HasCredentials(proxy)
                    ? new NetworkCredential(proxy.Username, proxy.Password)
                    : null,
                // Bypass SSL certificate validation when using proxy
                // This is needed because some proxies may cause certificate chain issues
                SslOptions = CreateSslOptionsForProxy()
            };

            return handler;
        }

        /// <summary>
        /// Create handler for SOCKS proxy (SOCKS4/SOCKS5).
        /// </summary>
        private SocketsHttpHandler CreateSocksProxyHandler(Proxy proxy)
        {
            var socksScheme = proxy.Type == ProxyType.Socks5 ? "socks5" : "socks4";
            var proxyUri = BuildProxyUri(socksScheme, proxy);

            return new SocketsHttpHandler
            {
                Proxy = new WebProxy(proxyUri),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(ProxyConnectTimeoutSeconds),
                // Bypass SSL certificate validation when using proxy
                SslOptions = CreateSslOptionsForProxy()
            };
        }
        
        /// <summary>
        /// Create SSL options that bypass certificate validation for proxy connections.
        /// This is necessary because some proxies may intercept SSL traffic or cause
        /// certificate chain validation issues (RemoteCertificateNameMismatch, RemoteCertificateChainErrors).
        /// </summary>
        private static System.Net.Security.SslClientAuthenticationOptions CreateSslOptionsForProxy()
        {
            return new System.Net.Security.SslClientAuthenticationOptions
            {
                // Accept all certificates when using proxy
                // This handles cases where proxy causes certificate mismatch or chain errors
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
        }

        /// <summary>
        /// Record a successful proxy usage and reset failure count.
        /// This helps with intermittent proxies (sometimes work, sometimes don't).
        /// </summary>
        public async Task RecordProxySuccessAsync(int proxyId)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxy = await context.Proxies.FindAsync(proxyId);
                if (proxy != null)
                {
                    proxy.UsageCount++;
                    proxy.LastUsedAt = DateTime.UtcNow;
                    
                    // Reset failure count on success - this helps with intermittent proxies
                    if (proxy.FailureCount > 0)
                    {
                        _logger.LogInformation("Proxy {ProxyId} ({Host}:{Port}) succeeded after {FailureCount} failures. Resetting failure count.",
                            proxyId, proxy.Host, proxy.Port, proxy.FailureCount);
                        proxy.FailureCount = 0;
                        proxy.LastFailureReason = null;
                    }
                    
                    await context.SaveChangesAsync();
                }
            }, $"RecordProxySuccess(ID={proxyId})");
        }

        /// <summary>
        /// Record a proxy failure and optionally disable if too many consecutive failures.
        /// For intermittent failures (HTML response, etc.), this increments failure count but doesn't immediately disable.
        /// </summary>
        /// <param name="proxyId">The proxy ID</param>
        /// <param name="reason">Failure reason</param>
        /// <param name="isIntermittent">True if this is an intermittent failure (proxy may work next time)</param>
        /// <param name="isTimeoutError">True if this is a timeout/cancellation error (very transient)</param>
        public async Task RecordProxyFailureAsync(int proxyId, string reason, bool isIntermittent = false, bool isTimeoutError = false)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxy = await context.Proxies.FindAsync(proxyId);
                if (proxy != null)
                {
                    proxy.FailureCount++;
                    proxy.LastFailedAt = DateTime.UtcNow;
                    
                    // Safely truncate reason to max length
                    if (!string.IsNullOrEmpty(reason))
                    {
                        proxy.LastFailureReason = reason.Length > MaxFailureReasonLength 
                            ? reason.Substring(0, MaxFailureReasonLength) 
                            : reason;
                    }
                    
                    // Determine disable threshold based on error type
                    // Timeout errors need much higher threshold since they're very transient
                    int disableThreshold;
                    if (isTimeoutError)
                    {
                        disableThreshold = MaxConsecutiveFailuresBeforeDisable * 4;  // 20 failures for timeout
                    }
                    else if (isIntermittent)
                    {
                        disableThreshold = MaxConsecutiveFailuresBeforeDisable * 2;  // 10 failures for intermittent
                    }
                    else
                    {
                        disableThreshold = MaxConsecutiveFailuresBeforeDisable;      // 5 failures for regular
                    }
                    
                    // Auto-disable proxy after too many consecutive failures
                    if (proxy.FailureCount >= disableThreshold)
                    {
                        proxy.IsEnabled = false;
                        _logger.LogWarning("Proxy {ProxyId} ({Host}:{Port}) disabled due to {FailureCount} consecutive failures (threshold: {Threshold}, intermittent: {IsIntermittent}, timeout: {IsTimeout})", 
                            proxyId, proxy.Host, proxy.Port, proxy.FailureCount, disableThreshold, isIntermittent, isTimeoutError);
                        RefreshCache();
                    }
                    else if (isTimeoutError)
                    {
                        _logger.LogDebug("Proxy {ProxyId} ({Host}:{Port}) timeout failure #{FailureCount}/{Threshold} (transient, not disabling): {Reason}",
                            proxyId, proxy.Host, proxy.Port, proxy.FailureCount, disableThreshold, reason);
                    }
                    else if (isIntermittent)
                    {
                        _logger.LogInformation("Proxy {ProxyId} ({Host}:{Port}) intermittent failure #{FailureCount}/{Threshold}: {Reason}",
                            proxyId, proxy.Host, proxy.Port, proxy.FailureCount, disableThreshold, reason);
                    }
                    
                    await context.SaveChangesAsync();
                }
            }, $"RecordProxyFailure(ID={proxyId})");
        }
        
        /// <summary>
        /// Immediately disable a proxy due to critical connection error.
        /// This is called when proxy is completely unreachable (timeout, connection refused, host down, etc.)
        /// </summary>
        public async Task DisableProxyImmediatelyAsync(int proxyId, string reason)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxy = await context.Proxies.FindAsync(proxyId);
                if (proxy != null && proxy.IsEnabled)
                {
                    proxy.IsEnabled = false;
                    proxy.FailureCount++;
                    proxy.LastFailedAt = DateTime.UtcNow;
                    
                    // Safely truncate reason to max length
                    if (!string.IsNullOrEmpty(reason))
                    {
                        proxy.LastFailureReason = reason.Length > MaxFailureReasonLength 
                            ? reason.Substring(0, MaxFailureReasonLength) 
                            : reason;
                    }
                    
                    await context.SaveChangesAsync();
                    RefreshCache();
                    
                    _logger.LogWarning("?? Proxy {ProxyId} ({Host}:{Port}) PERMANENTLY DISABLED due to critical error: {Reason}", 
                        proxyId, proxy.Host, proxy.Port, reason);
                }
            }, $"DisableProxyImmediately(ID={proxyId})");
        }
        
        /// <summary>
        /// Check if an exception is a timeout/cancellation error.
        /// These are transient errors - the proxy may still be working fine.
        /// </summary>
        public static bool IsTimeoutOrCancellationError(Exception ex)
        {
            // Direct cancellation exceptions
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return true;
            }
            
            // Check message for timeout patterns
            var message = ex.Message ?? string.Empty;
            if (message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("task was canceled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("request was canceled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Check if HttpRequestException wraps a cancellation
            if (ex is HttpRequestException httpEx && httpEx.InnerException != null)
            {
                return IsTimeoutOrCancellationError(httpEx.InnerException);
            }
            
            // Check inner exception
            if (ex.InnerException != null)
            {
                return IsTimeoutOrCancellationError(ex.InnerException);
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if an exception indicates a critical proxy failure that should result in immediate disable.
        /// Critical failures include: host unreachable, connection refused, etc.
        /// NOTE: Timeout/cancellation errors are NOT critical - they are transient.
        /// </summary>
        public static bool IsCriticalProxyError(Exception ex)
        {
            // IMPORTANT: Timeout and cancellation are NOT critical errors
            // They are transient and the proxy may work fine on retry
            if (IsTimeoutOrCancellationError(ex))
            {
                return false;
            }
            
            // Check for SocketException with specific error codes
            if (ex is SocketException socketEx)
            {
                return socketEx.SocketErrorCode switch
                {
                    // NOTE: TimedOut removed - it's transient, not critical
                    SocketError.ConnectionRefused => true,  // 10061 - Connection refused
                    SocketError.HostUnreachable => true,    // 10065 - Host unreachable
                    SocketError.NetworkUnreachable => true, // 10051 - Network unreachable
                    SocketError.HostNotFound => true,       // 11001 - Host not found
                    SocketError.HostDown => true,           // Host is down
                    SocketError.NetworkDown => true,        // Network is down
                    SocketError.AddressNotAvailable => true,// Address not available
                    _ => false
                };
            }
            
            // Check for HttpRequestException that wraps SocketException
            if (ex is HttpRequestException httpEx)
            {
                // Check inner exception
                if (httpEx.InnerException is SocketException innerSocketEx)
                {
                    return IsCriticalProxyError(innerSocketEx);
                }
                
                // Check message patterns for critical errors (excluding timeout patterns)
                var message = httpEx.Message ?? string.Empty;
                
                // Skip if it's a timeout message
                if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                
                return message.Contains("10061") ||  // Connection refused
                       message.Contains("10065") ||  // Host unreachable
                       message.Contains("10051") ||  // Network unreachable
                       message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("actively refused", StringComparison.OrdinalIgnoreCase);
            }
            
            // Check inner exceptions recursively
            if (ex.InnerException != null)
            {
                return IsCriticalProxyError(ex.InnerException);
            }
            
            return false;
        }
        
        /// <summary>
        /// Get a user-friendly description of the proxy error for logging.
        /// </summary>
        public static string GetProxyErrorDescription(Exception ex)
        {
            // Handle timeout/cancellation with clear message
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return "Request timeout (transient) - proxy may still be working";
            }
            
            if (ex is SocketException socketEx)
            {
                return socketEx.SocketErrorCode switch
                {
                    SocketError.TimedOut => "Connection timed out (transient) - will retry",
                    SocketError.ConnectionRefused => "Connection refused - proxy service not running or port blocked",
                    SocketError.HostUnreachable => "Host unreachable - proxy server not accessible",
                    SocketError.NetworkUnreachable => "Network unreachable - routing issue or firewall block",
                    SocketError.HostNotFound => "Host not found - invalid hostname or DNS issue",
                    SocketError.HostDown => "Host is down - proxy server offline",
                    SocketError.NetworkDown => "Network is down - network connectivity issue",
                    _ => $"Socket error: {socketEx.SocketErrorCode}"
                };
            }
            
            if (ex is HttpRequestException httpEx && httpEx.InnerException is SocketException innerSocketEx)
            {
                return GetProxyErrorDescription(innerSocketEx);
            }
            
            var message = ex.Message ?? string.Empty;
            
            // Check for timeout patterns in message
            if (message.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "Request timeout (transient) - proxy may still be working";
            }
            
            return message.Length > MaxFailureReasonLength 
                ? message.Substring(0, MaxFailureReasonLength) 
                : message;
        }

        /// <summary>
        /// Parse proxy list from text (one proxy per line).
        /// Supported formats:
        /// - host:port (defaults to SOCKS5)
        /// - type://host:port (type = http, socks4, socks5)
        /// - type://username:password@host:port (with authentication)
        /// </summary>
        public List<Proxy> ParseProxyList(string proxyText)
        {
            var proxies = new List<Proxy>();
            
            if (string.IsNullOrWhiteSpace(proxyText))
                return proxies;

            var lines = proxyText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                var proxy = ParseProxyLine(trimmedLine);
                if (proxy != null)
                {
                    proxies.Add(proxy);
                }
            }

            _logger.LogInformation("Parsed {Count} proxies from text input", proxies.Count);
            return proxies;
        }

        private Proxy? ParseProxyLine(string line)
        {
            try
            {
                var proxy = new Proxy();
                
                // Determine proxy type from scheme
                if (line.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
                {
                    proxy.Type = ProxyType.Socks5;
                    line = line.Substring(9);
                }
                else if (line.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
                {
                    proxy.Type = ProxyType.Socks4;
                    line = line.Substring(9);
                }
                else if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    proxy.Type = ProxyType.Http;
                    line = line.Substring(7);
                }
                else if (line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    proxy.Type = ProxyType.Http;
                    line = line.Substring(8);
                }
                else
                {
                    // Default to SOCKS5
                    proxy.Type = ProxyType.Socks5;
                }

                // Check for authentication (username:password@host:port)
                var atIndex = line.LastIndexOf('@');
                if (atIndex > 0)
                {
                    var auth = line.Substring(0, atIndex);
                    var hostPort = line.Substring(atIndex + 1);
                    
                    var authParts = auth.Split(':');
                    if (authParts.Length >= 2)
                    {
                        proxy.Username = authParts[0];
                        proxy.Password = string.Join(":", authParts.Skip(1)); // Password may contain ':'
                    }
                    
                    line = hostPort;
                }

                // Parse host:port
                var colonIndex = line.LastIndexOf(':');
                if (colonIndex <= 0)
                {
                    _logger.LogWarning("Invalid proxy format (missing port): {Line}", line);
                    return null;
                }

                proxy.Host = line.Substring(0, colonIndex).Trim();
                var portStr = line.Substring(colonIndex + 1).Trim();
                
                if (!int.TryParse(portStr, out int port) || port <= 0 || port > 65535)
                {
                    _logger.LogWarning("Invalid proxy port: {Port}", portStr);
                    return null;
                }
                
                proxy.Port = port;
                
                if (string.IsNullOrEmpty(proxy.Host))
                {
                    _logger.LogWarning("Empty proxy host");
                    return null;
                }

                return proxy;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse proxy line: {Line}", line);
                return null;
            }
        }

        /// <summary>
        /// Get the count of active proxies.
        /// </summary>
        public async Task<int> GetActiveProxyCountAsync()
        {
            var proxies = await GetEnabledProxiesAsync();
            return proxies.Count;
        }
    }
}
