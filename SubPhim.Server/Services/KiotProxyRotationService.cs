using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Services
{
    /// <summary>
    /// Service tự động xoay proxy từ KiotProxy API.
    /// Hỗ trợ nhiều API key, xoay theo interval, và thêm/xóa proxy tự động.
    /// </summary>
    public class KiotProxyRotationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<KiotProxyRotationService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ProxyService _proxyService;
        private readonly ProxyHealthCheckService _healthCheckService;
        
        private const string KIOTPROXY_BASE_URL = "https://api.kiotproxy.com/api/v1/proxies";
        private const int CHECK_SETTINGS_INTERVAL_SECONDS = 30; // Kiểm tra cài đặt mỗi 30 giây
        
        // Lưu thời gian xoay tiếp theo cho mỗi API key
        private readonly ConcurrentDictionary<string, DateTime> _nextRotationTimes = new();
        
        // Lưu proxy hiện tại của mỗi API key (để xóa khi xoay)
        private readonly ConcurrentDictionary<string, int> _currentProxyIds = new();
        
        // File để lưu trữ mapping giữa API key và proxy ID (để khôi phục sau restart)
        private readonly string _mappingFilePath;
        
        public KiotProxyRotationService(
            IServiceProvider serviceProvider,
            ILogger<KiotProxyRotationService> logger,
            IHttpClientFactory httpClientFactory,
            ProxyService proxyService,
            ProxyHealthCheckService healthCheckService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _proxyService = proxyService;
            _healthCheckService = healthCheckService;
            
            // Lưu mapping file trong thư mục data
            var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            _mappingFilePath = Path.Combine(dataDir, "kiotproxy_mapping.json");
            
            // Load mapping từ file (nếu có)
            LoadProxyMapping();
        }
        
        // Track service state
        public bool IsEnabled { get; private set; } = false;
        public DateTime? LastCheckTime { get; private set; }
        private bool _wasEnabledLastCheck = false;
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("KiotProxyRotationService started (waiting for configuration)");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var isEnabled = await CheckIfEnabledAsync(stoppingToken);
                    IsEnabled = isEnabled;
                    LastCheckTime = DateTime.UtcNow;
                    
                    if (isEnabled)
                    {
                        if (!_wasEnabledLastCheck)
                        {
                            _logger.LogInformation("KiotProxyRotationService: ENABLED - Starting rotation");
                        }
                        _wasEnabledLastCheck = true;
                        
                        await ProcessRotationAsync(stoppingToken);
                        // Khi enabled, check mỗi 30 giây
                        await Task.Delay(TimeSpan.FromSeconds(CHECK_SETTINGS_INTERVAL_SECONDS), stoppingToken);
                    }
                    else
                    {
                        if (_wasEnabledLastCheck)
                        {
                            _logger.LogInformation("KiotProxyRotationService: DISABLED - Sleeping");
                            // Clear tracking khi bị disable
                            _nextRotationTimes.Clear();
                        }
                        _wasEnabledLastCheck = false;
                        
                        // Khi disabled, sleep lâu hơn (60 giây) để tiết kiệm resource
                        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in KiotProxyRotationService");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            
            _logger.LogInformation("KiotProxyRotationService stopped");
        }
        
        private async Task<bool> CheckIfEnabledAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);
                return settings?.KiotProxyRotationEnabled == true 
                    && !string.IsNullOrWhiteSpace(settings.KiotProxyApiKeys);
            }
            catch
            {
                return false;
            }
        }
        
        private async Task ProcessRotationAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var settings = await context.LocalApiSettings.FindAsync(new object[] { 1 }, ct);
            if (settings == null || !settings.KiotProxyRotationEnabled)
            {
                return; // Không bật hoặc không có settings
            }
            
            var apiKeys = ParseApiKeys(settings.KiotProxyApiKeys);
            if (!apiKeys.Any())
            {
                return; // Không có API key
            }
            
            var rotationInterval = TimeSpan.FromMinutes(settings.KiotProxyRotationIntervalMinutes);
            var region = settings.KiotProxyRegion ?? "random";
            var proxyType = settings.KiotProxyType ?? "socks5";
            
            // Xử lý từng API key
            foreach (var apiKey in apiKeys)
            {
                if (ct.IsCancellationRequested) break;
                
                try
                {
                    await ProcessApiKeyRotationAsync(apiKey, rotationInterval, region, proxyType, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing API key rotation for key ending ...{KeyEnd}",
                        apiKey.Length > 6 ? apiKey[^6..] : apiKey);
                }
            }
        }
        
        private async Task ProcessApiKeyRotationAsync(
            string apiKey, 
            TimeSpan rotationInterval, 
            string region, 
            string proxyType,
            CancellationToken ct)
        {
            var keyId = apiKey.Length > 6 ? apiKey[^6..] : apiKey; // Last 6 chars for logging
            
            // Kiểm tra xem đã đến lúc xoay chưa
            if (_nextRotationTimes.TryGetValue(apiKey, out var nextTime) && DateTime.UtcNow < nextTime)
            {
                return; // Chưa đến lúc xoay
            }
            
            _logger.LogInformation("KiotProxy: Rotating proxy for key ...{KeyId}, region={Region}, type={Type}",
                keyId, region, proxyType);
            
            // Gọi API lấy proxy mới
            var proxyData = await GetNewProxyAsync(apiKey, region, ct);
            
            if (proxyData == null)
            {
                _logger.LogWarning("KiotProxy: Failed to get new proxy for key ...{KeyId}", keyId);
                // Retry sau 1 phút nếu lỗi
                _nextRotationTimes[apiKey] = DateTime.UtcNow.AddMinutes(1);
                return;
            }
            
            // Kiểm tra proxy cũ có đang được sử dụng không
            if (_currentProxyIds.TryGetValue(apiKey, out var oldProxyId))
            {
                var isInUse = await IsProxyInUseAsync(oldProxyId, ct);
                if (isInUse)
                {
                    _logger.LogDebug("KiotProxy: Proxy ID {ProxyId} is still in use, deferring rotation for key ...{KeyId}", 
                        oldProxyId, keyId);
                    // Đợi 30 giây rồi thử lại
                    _nextRotationTimes[apiKey] = DateTime.UtcNow.AddSeconds(30);
                    return;
                }
                
                // Proxy không đang sử dụng, có thể xóa
                await DeleteProxyByIdAsync(oldProxyId, ct);
                _logger.LogDebug("KiotProxy: Deleted old proxy ID {ProxyId} for key ...{KeyId}", oldProxyId, keyId);
            }
            
            // Thêm proxy mới
            var newProxyId = await AddProxyAsync(proxyData, proxyType, ct);
            
            if (newProxyId > 0)
            {
                _currentProxyIds[apiKey] = newProxyId;
                _logger.LogInformation("KiotProxy: Added new proxy ID {ProxyId} for key ...{KeyId} ({Host}:{Port}, location: {Location})",
                    newProxyId, keyId, proxyData.Host, proxyData.Port, proxyData.Location);
                
                // Lưu mapping vào file để khôi phục sau restart
                SaveProxyMapping();
                
                // Queue kiểm tra latency cho proxy mới
                _healthCheckService.QueueProxyCheck(newProxyId);
                _logger.LogDebug("KiotProxy: Queued latency check for new proxy ID {ProxyId}", newProxyId);
            }
            
            // Đặt thời gian xoay tiếp theo
            _nextRotationTimes[apiKey] = DateTime.UtcNow.Add(rotationInterval);
            
            // Refresh cache
            _proxyService.RefreshCache();
        }
        
        /// <summary>
        /// Kiểm tra xem proxy có đang được sử dụng trong vòng PROXY_IN_USE_THRESHOLD_SECONDS giây không
        /// </summary>
        private const int PROXY_IN_USE_THRESHOLD_SECONDS = 60; // Nếu LastUsedAt trong 60 giây = đang dùng
        
        private async Task<bool> IsProxyInUseAsync(int proxyId, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxy = await context.Proxies.FindAsync(new object[] { proxyId }, ct);
                if (proxy == null)
                {
                    return false; // Proxy không tồn tại, không cần lo
                }
                
                // Kiểm tra LastUsedAt
                if (proxy.LastUsedAt.HasValue)
                {
                    var secondsSinceLastUse = (DateTime.UtcNow - proxy.LastUsedAt.Value).TotalSeconds;
                    if (secondsSinceLastUse < PROXY_IN_USE_THRESHOLD_SECONDS)
                    {
                        _logger.LogDebug("KiotProxy: Proxy {ProxyId} was used {Seconds:F0}s ago, considered in use",
                            proxyId, secondsSinceLastUse);
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KiotProxy: Error checking if proxy {ProxyId} is in use", proxyId);
                return true; // Nếu lỗi, coi như đang dùng để an toàn
            }
        }
        
        private async Task<KiotProxyData?> GetNewProxyAsync(string apiKey, string region, CancellationToken ct)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                var url = $"{KIOTPROXY_BASE_URL}/new?key={Uri.EscapeDataString(apiKey)}&region={Uri.EscapeDataString(region)}";
                
                var response = await httpClient.GetAsync(url, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                
                var result = JsonSerializer.Deserialize<KiotProxyResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (result?.Success == true && result.Data != null)
                {
                    var data = result.Data;
                    
                    // Parse proxy info
                    string? host = null;
                    int? port = null;
                    
                    // Ưu tiên SOCKS5
                    if (!string.IsNullOrEmpty(data.Socks5))
                    {
                        var parts = data.Socks5.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var p))
                        {
                            host = parts[0];
                            port = p;
                        }
                    }
                    
                    // Fallback HTTP
                    if (host == null && !string.IsNullOrEmpty(data.Http))
                    {
                        var parts = data.Http.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var p))
                        {
                            host = parts[0];
                            port = p;
                        }
                    }
                    
                    if (host != null && port != null)
                    {
                        return new KiotProxyData
                        {
                            Host = host,
                            Port = port.Value,
                            Location = data.Location ?? "KiotProxy",
                            RealIpAddress = data.RealIpAddress,
                            Http = data.Http,
                            Socks5 = data.Socks5
                        };
                    }
                }
                else
                {
                    _logger.LogWarning("KiotProxy API error: {Error}", result?.Error ?? result?.Message ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KiotProxy: Error calling API");
            }
            
            return null;
        }
        
        private async Task<int> AddProxyAsync(KiotProxyData data, string proxyType, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Kiểm tra trùng lặp
                var exists = await context.Proxies.AnyAsync(p => 
                    p.Host == data.Host && p.Port == data.Port, ct);
                
                if (exists)
                {
                    _logger.LogDebug("KiotProxy: Proxy {Host}:{Port} already exists", data.Host, data.Port);
                    var existing = await context.Proxies.FirstOrDefaultAsync(p => 
                        p.Host == data.Host && p.Port == data.Port, ct);
                    return existing?.Id ?? 0;
                }
                
                var proxy = new Proxy
                {
                    Host = data.Host,
                    Port = data.Port,
                    Type = proxyType == "http" ? ProxyType.Http : ProxyType.Socks5,
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow
                    // Note: Location info is logged, not stored in DB
                };
                
                context.Proxies.Add(proxy);
                await context.SaveChangesAsync(ct);
                
                return proxy.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KiotProxy: Error adding proxy to database");
                return 0;
            }
        }
        
        private async Task DeleteProxyByIdAsync(int proxyId, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var proxy = await context.Proxies.FindAsync(new object[] { proxyId }, ct);
                if (proxy != null)
                {
                    context.Proxies.Remove(proxy);
                    await context.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KiotProxy: Error deleting proxy ID {ProxyId}", proxyId);
            }
        }
        
        private List<string> ParseApiKeys(string? apiKeysText)
        {
            if (string.IsNullOrWhiteSpace(apiKeysText))
                return new List<string>();
            
            return apiKeysText
                .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .ToList();
        }
        
        /// <summary>
        /// Force rotation cho tất cả API keys (gọi từ Admin Panel)
        /// </summary>
        public void ForceRotationAll()
        {
            _nextRotationTimes.Clear();
            _logger.LogInformation("KiotProxy: Forced rotation for all API keys");
        }
        
        /// <summary>
        /// Lấy trạng thái hiện tại
        /// </summary>
        public Dictionary<string, (DateTime NextRotation, int? CurrentProxyId)> GetStatus()
        {
            var result = new Dictionary<string, (DateTime, int?)>();
            
            foreach (var kvp in _nextRotationTimes)
            {
                var keyId = kvp.Key.Length > 6 ? $"...{kvp.Key[^6..]}" : kvp.Key;
                _currentProxyIds.TryGetValue(kvp.Key, out var proxyId);
                result[keyId] = (kvp.Value, proxyId > 0 ? proxyId : null);
            }
            
            return result;
        }
        
        /// <summary>
        /// Lưu mapping giữa API key và proxy ID vào file
        /// </summary>
        private void SaveProxyMapping()
        {
            try
            {
                var mapping = _currentProxyIds.ToDictionary(k => k.Key, v => v.Value);
                var json = JsonSerializer.Serialize(mapping, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_mappingFilePath, json);
                _logger.LogDebug("KiotProxy: Saved proxy mapping to file ({Count} entries)", mapping.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KiotProxy: Failed to save proxy mapping to file");
            }
        }
        
        /// <summary>
        /// Load mapping giữa API key và proxy ID từ file
        /// </summary>
        private void LoadProxyMapping()
        {
            try
            {
                if (!File.Exists(_mappingFilePath))
                {
                    _logger.LogDebug("KiotProxy: No mapping file found, starting fresh");
                    return;
                }
                
                var json = File.ReadAllText(_mappingFilePath);
                var mapping = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                
                if (mapping != null)
                {
                    foreach (var kvp in mapping)
                    {
                        _currentProxyIds[kvp.Key] = kvp.Value;
                    }
                    _logger.LogInformation("KiotProxy: Loaded proxy mapping from file ({Count} entries)", mapping.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KiotProxy: Failed to load proxy mapping from file");
            }
        }
    }
    
    // Response models
    internal class KiotProxyResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public KiotProxyDataResponse? Data { get; set; }
    }
    
    internal class KiotProxyDataResponse
    {
        public string? Http { get; set; }
        public string? Socks5 { get; set; }
        public string? RealIpAddress { get; set; }
        public string? Location { get; set; }
        public int Ttl { get; set; }
        public int Ttc { get; set; }
    }
    
    internal class KiotProxyData
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string? Location { get; set; }
        public string? RealIpAddress { get; set; }
        public string? Http { get; set; }
        public string? Socks5 { get; set; }
    }
}
