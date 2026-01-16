using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;

namespace SubPhim.Server.Pages.Admin.LocalApi
{
    public class ProxyModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ProxyService _proxyService;
        private readonly ProxyHealthCheckService _healthCheckService;
        private readonly ILogger<ProxyModel> _logger;

        public ProxyModel(
            AppDbContext context, 
            ProxyService proxyService, 
            ProxyHealthCheckService healthCheckService,
            ILogger<ProxyModel> logger)
        {
            _context = context;
            _proxyService = proxyService;
            _healthCheckService = healthCheckService;
            _logger = logger;
        }

        public List<Proxy> Proxies { get; set; } = new();
        public int ActiveProxyCount { get; set; }
        public int MeasuredProxyCount { get; set; }
        public bool IsCheckRunning { get; set; }
        
        // KiotProxy Auto-Rotation settings
        public KiotProxySettingsModel KiotProxySettings { get; set; } = new();
        
        public class KiotProxySettingsModel
        {
            public bool Enabled { get; set; }
            public string ApiKeys { get; set; } = "";
            public int IntervalMinutes { get; set; } = 5;
            public string Region { get; set; } = "random";
            public string ProxyType { get; set; } = "socks5";
        }

        [TempData] public string SuccessMessage { get; set; }
        [TempData] public string ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
            IsCheckRunning = _healthCheckService.IsBatchCheckRunning();
        }

        private async Task LoadDataAsync()
        {
            Proxies = await _context.Proxies
                .OrderByDescending(p => p.IsEnabled)
                .ThenBy(p => p.LatencyMs.HasValue ? 0 : 1) // Proxy đã check lên đầu
                .ThenBy(p => p.LatencyMs ?? int.MaxValue) // Sắp xếp theo latency thấp nhất
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            
            ActiveProxyCount = Proxies.Count(p => p.IsEnabled);
            MeasuredProxyCount = Proxies.Count(p => p.LatencyMs.HasValue && p.LatencyCheckStatus == "OK");
            
            // Load KiotProxy settings
            var settings = await _context.LocalApiSettings.FindAsync(1);
            if (settings != null)
            {
                KiotProxySettings = new KiotProxySettingsModel
                {
                    Enabled = settings.KiotProxyRotationEnabled,
                    ApiKeys = settings.KiotProxyApiKeys ?? "",
                    IntervalMinutes = settings.KiotProxyRotationIntervalMinutes,
                    Region = settings.KiotProxyRegion ?? "random",
                    ProxyType = settings.KiotProxyType ?? "socks5"
                };
            }
        }

        /// <summary>
        /// Thêm proxy từ danh sách text
        /// </summary>
        public async Task<IActionResult> OnPostAddProxiesAsync([FromForm] string proxyList)
        {
            if (string.IsNullOrWhiteSpace(proxyList))
            {
                ErrorMessage = "Vui lòng nhập ít nhất một proxy.";
                return RedirectToPage();
            }

            try
            {
                var parsedProxies = _proxyService.ParseProxyList(proxyList);
                
                if (!parsedProxies.Any())
                {
                    ErrorMessage = "Không có proxy hợp lệ nào được tìm thấy. Vui lòng kiểm tra định dạng.";
                    return RedirectToPage();
                }

                int addedCount = 0;
                int duplicateCount = 0;
                var newProxyIds = new List<int>();

                foreach (var proxy in parsedProxies)
                {
                    // Kiểm tra trùng lặp
                    var exists = await _context.Proxies.AnyAsync(p => 
                        p.Host == proxy.Host && p.Port == proxy.Port);
                    
                    if (exists)
                    {
                        duplicateCount++;
                        continue;
                    }

                    _context.Proxies.Add(proxy);
                    addedCount++;
                }

                await _context.SaveChangesAsync();
                
                // Lấy IDs của các proxy vừa thêm để kiểm tra
                var addedProxies = await _context.Proxies
                    .Where(p => parsedProxies.Select(pp => pp.Host).Contains(p.Host))
                    .Select(p => p.Id)
                    .ToListAsync();
                
                // Queue kiểm tra độ trễ cho mỗi proxy mới (fire-and-forget)
                foreach (var proxyId in addedProxies)
                {
                    _healthCheckService.QueueProxyCheck(proxyId);
                }
                
                // Refresh cache
                _proxyService.RefreshCache();

                if (duplicateCount > 0)
                {
                    SuccessMessage = $"Đã thêm {addedCount} proxy. Bỏ qua {duplicateCount} proxy trùng lặp. Đang kiểm tra độ trễ...";
                }
                else
                {
                    SuccessMessage = $"Đã thêm thành công {addedCount} proxy. Đang kiểm tra độ trễ...";
                }

                _logger.LogInformation("Added {Count} proxies, skipped {Duplicates} duplicates. Queued latency checks.", addedCount, duplicateCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding proxies");
                ErrorMessage = $"Lỗi khi thêm proxy: {ex.Message}";
            }

            return RedirectToPage();
        }

        /// <summary>
        /// Bật/tắt proxy
        /// </summary>
        public async Task<IActionResult> OnPostToggleProxyAsync(int id)
        {
            var proxy = await _context.Proxies.FindAsync(id);
            if (proxy != null)
            {
                proxy.IsEnabled = !proxy.IsEnabled;
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                _logger.LogInformation("Proxy {Id} ({Host}:{Port}) toggled to {Status}", 
                    id, proxy.Host, proxy.Port, proxy.IsEnabled ? "ON" : "OFF");
            }
            return RedirectToPage();
        }

        /// <summary>
        /// Xóa một proxy
        /// </summary>
        public async Task<IActionResult> OnPostDeleteProxyAsync(int id)
        {
            var proxy = await _context.Proxies.FindAsync(id);
            if (proxy != null)
            {
                _context.Proxies.Remove(proxy);
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã xóa proxy {proxy.Host}:{proxy.Port}";
                _logger.LogInformation("Deleted proxy {Id} ({Host}:{Port})", id, proxy.Host, proxy.Port);
            }
            return RedirectToPage();
        }

        /// <summary>
        /// Reset thống kê proxy
        /// </summary>
        public async Task<IActionResult> OnPostResetProxyStatsAsync(int id)
        {
            var proxy = await _context.Proxies.FindAsync(id);
            if (proxy != null)
            {
                proxy.UsageCount = 0;
                proxy.FailureCount = 0;
                proxy.LastUsedAt = null;
                proxy.LastFailedAt = null;
                proxy.LastFailureReason = null;
                
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã reset thống kê proxy {proxy.Host}:{proxy.Port}";
                _logger.LogInformation("Reset stats for proxy {Id} ({Host}:{Port})", id, proxy.Host, proxy.Port);
            }
            return RedirectToPage();
        }

        /// <summary>
        /// Xóa nhiều proxy đã chọn
        /// </summary>
        public async Task<IActionResult> OnPostDeleteSelectedProxiesAsync([FromForm] int[] selectedProxyIds)
        {
            if (selectedProxyIds == null || !selectedProxyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một proxy để xóa.";
                return RedirectToPage();
            }

            var proxiesToDelete = await _context.Proxies
                .Where(p => selectedProxyIds.Contains(p.Id))
                .ToListAsync();

            if (proxiesToDelete.Any())
            {
                _context.Proxies.RemoveRange(proxiesToDelete);
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã xóa {proxiesToDelete.Count} proxy.";
                _logger.LogInformation("Deleted {Count} proxies", proxiesToDelete.Count);
            }

            return RedirectToPage();
        }

        /// <summary>
        /// Tắt nhiều proxy đã chọn
        /// </summary>
        public async Task<IActionResult> OnPostDisableSelectedProxiesAsync([FromForm] int[] selectedProxyIds)
        {
            if (selectedProxyIds == null || !selectedProxyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một proxy để tắt.";
                return RedirectToPage();
            }

            var proxiesToDisable = await _context.Proxies
                .Where(p => selectedProxyIds.Contains(p.Id))
                .ToListAsync();

            if (proxiesToDisable.Any())
            {
                foreach (var proxy in proxiesToDisable)
                {
                    proxy.IsEnabled = false;
                }
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã tắt {proxiesToDisable.Count} proxy.";
                _logger.LogInformation("Disabled {Count} proxies", proxiesToDisable.Count);
            }

            return RedirectToPage();
        }

        /// <summary>
        /// Bật nhiều proxy đã chọn
        /// </summary>
        public async Task<IActionResult> OnPostEnableSelectedProxiesAsync([FromForm] int[] selectedProxyIds)
        {
            if (selectedProxyIds == null || !selectedProxyIds.Any())
            {
                ErrorMessage = "Vui lòng chọn ít nhất một proxy để bật.";
                return RedirectToPage();
            }

            var proxiesToEnable = await _context.Proxies
                .Where(p => selectedProxyIds.Contains(p.Id))
                .ToListAsync();

            if (proxiesToEnable.Any())
            {
                foreach (var proxy in proxiesToEnable)
                {
                    proxy.IsEnabled = true;
                    // Reset failure count khi bật lại
                    proxy.FailureCount = 0;
                    proxy.LastFailureReason = null;
                }
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã bật {proxiesToEnable.Count} proxy.";
                _logger.LogInformation("Enabled {Count} proxies", proxiesToEnable.Count);
            }

            return RedirectToPage();
        }

        /// <summary>
        /// Xóa tất cả proxy
        /// </summary>
        public async Task<IActionResult> OnPostDeleteAllProxiesAsync()
        {
            var allProxies = await _context.Proxies.ToListAsync();
            
            if (allProxies.Any())
            {
                _context.Proxies.RemoveRange(allProxies);
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã xóa tất cả {allProxies.Count} proxy.";
                _logger.LogInformation("Deleted all {Count} proxies", allProxies.Count);
            }
            else
            {
                ErrorMessage = "Không có proxy nào để xóa.";
            }

            return RedirectToPage();
        }
        
        /// <summary>
        /// Kiểm tra độ trễ của một proxy
        /// </summary>
        public async Task<IActionResult> OnPostCheckProxyAsync(int id)
        {
            var proxy = await _context.Proxies.FindAsync(id);
            if (proxy == null)
            {
                ErrorMessage = "Không tìm thấy proxy.";
                return RedirectToPage();
            }
            
            try
            {
                var (success, latencyMs, status) = await _healthCheckService.CheckProxyLatencyAsync(proxy);
                
                // Luôn ghi latencyMs để biết proxy chậm bao nhiêu (cả khi fail/timeout)
                proxy.LatencyMs = latencyMs > 0 ? latencyMs : null;
                proxy.LastLatencyCheckUtc = DateTime.UtcNow;
                proxy.LatencyCheckStatus = status;
                
                await _context.SaveChangesAsync();
                
                if (success)
                {
                    SuccessMessage = $"Proxy {proxy.Host}:{proxy.Port} - Độ trễ: {latencyMs}ms";
                }
                else
                {
                    // Hiển thị cả latencyMs khi fail để biết proxy chậm bao nhiêu
                    ErrorMessage = $"Proxy {proxy.Host}:{proxy.Port} - {status} ({latencyMs}ms)";
                }
                
                _logger.LogInformation("Checked proxy {Id} ({Host}:{Port}): {Status}, Latency: {LatencyMs}ms", 
                    id, proxy.Host, proxy.Port, status, latencyMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking proxy {Id}", id);
                ErrorMessage = $"Lỗi khi kiểm tra proxy: {ex.Message}";
            }
            
            return RedirectToPage();
        }
        
        /// <summary>
        /// Kiểm tra tất cả proxy đang hoạt động
        /// </summary>
        public IActionResult OnPostCheckAllProxies()
        {
            if (_healthCheckService.IsBatchCheckRunning())
            {
                ErrorMessage = "Đang có quá trình kiểm tra proxy chạy. Vui lòng đợi...";
                return RedirectToPage();
            }
            
            // Start kiểm tra trong background (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _healthCheckService.CheckAllProxiesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch proxy check");
                }
            });
            
            SuccessMessage = "Đã bắt đầu kiểm tra tất cả proxy. Làm mới trang để xem kết quả.";
            _logger.LogInformation("Started batch proxy health check");
            
            return RedirectToPage();
        }
        
        /// <summary>
        /// Reset latency cho một proxy
        /// </summary>
        public async Task<IActionResult> OnPostResetLatencyAsync(int id)
        {
            var proxy = await _context.Proxies.FindAsync(id);
            if (proxy != null)
            {
                proxy.LatencyMs = null;
                proxy.LastLatencyCheckUtc = null;
                proxy.LatencyCheckStatus = null;
                
                await _context.SaveChangesAsync();
                _proxyService.RefreshCache();
                
                SuccessMessage = $"Đã reset latency cho proxy {proxy.Host}:{proxy.Port}";
                _logger.LogInformation("Reset latency for proxy {Id} ({Host}:{Port})", id, proxy.Host, proxy.Port);
            }
            
            return RedirectToPage();
        }
        
        /// <summary>
        /// Cập nhật cài đặt KiotProxy Auto-Rotation
        /// </summary>
        public async Task<IActionResult> OnPostUpdateKiotProxySettingsAsync(
            [FromForm] bool kiotProxyEnabled,
            [FromForm] string? kiotProxyApiKeys,
            [FromForm] int kiotProxyIntervalMinutes,
            [FromForm] string? kiotProxyRegion,
            [FromForm] string? kiotProxyType)
        {
            try
            {
                var settings = await _context.LocalApiSettings.FindAsync(1);
                if (settings == null)
                {
                    settings = new LocalApiSetting { Id = 1 };
                    _context.LocalApiSettings.Add(settings);
                }
                
                settings.KiotProxyRotationEnabled = kiotProxyEnabled;
                settings.KiotProxyApiKeys = kiotProxyApiKeys ?? "";
                settings.KiotProxyRotationIntervalMinutes = Math.Max(1, Math.Min(60, kiotProxyIntervalMinutes));
                settings.KiotProxyRegion = kiotProxyRegion ?? "random";
                settings.KiotProxyType = kiotProxyType ?? "socks5";
                
                await _context.SaveChangesAsync();
                
                SuccessMessage = "Đã lưu cài đặt KiotProxy Auto-Rotation thành công!";
                _logger.LogInformation("KiotProxy settings updated: Enabled={Enabled}, Keys={KeyCount}, Interval={Interval}min",
                    kiotProxyEnabled, 
                    (kiotProxyApiKeys ?? "").Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries).Length,
                    kiotProxyIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating KiotProxy settings");
                ErrorMessage = $"Lỗi khi lưu cài đặt: {ex.Message}";
            }
            
            return RedirectToPage();
        }
        
        /// <summary>
        /// Force xoay proxy ngay lập tức
        /// </summary>
        public IActionResult OnPostForceKiotProxyRotation()
        {
            try
            {
                // Lấy KiotProxyRotationService từ DI và gọi ForceRotationAll
                using var scope = HttpContext.RequestServices.CreateScope();
                var rotationServices = scope.ServiceProvider.GetServices<IHostedService>()
                    .OfType<KiotProxyRotationService>();
                
                foreach (var service in rotationServices)
                {
                    service.ForceRotationAll();
                }
                
                SuccessMessage = "Đã yêu cầu xoay proxy ngay lập tức! Proxy mới sẽ được lấy trong vài giây.";
                _logger.LogInformation("Force KiotProxy rotation requested");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forcing KiotProxy rotation");
                ErrorMessage = $"Lỗi: {ex.Message}";
            }
            
            return RedirectToPage();
        }
    }
}
