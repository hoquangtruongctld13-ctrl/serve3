using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace subphimv1.Services
{
    public class UpdateService : INotifyPropertyChanged
    {
        private System.Timers.Timer _timer;

        #region Public Properties for Binding
        private string _latestVersion;
        public string LatestVersion { get => _latestVersion; set { _latestVersion = value; OnPropertyChanged(nameof(LatestVersion)); } }

        private string _releaseNotes = "Vui lòng đăng nhập để xem thông tin cập nhật.";
        public string ReleaseNotes { get => _releaseNotes; set { _releaseNotes = value; OnPropertyChanged(nameof(ReleaseNotes)); } }

        private string _downloadUrl;
        public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(nameof(DownloadUrl)); } }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable { get => _isUpdateAvailable; set { _isUpdateAvailable = value; OnPropertyChanged(nameof(IsUpdateAvailable)); } }

        private bool _isUpdating;
        public bool IsUpdating { get => _isUpdating; set { _isUpdating = value; OnPropertyChanged(nameof(IsUpdating)); } }

        private bool _forceUpdate;
        public bool ForceUpdate { get => _forceUpdate; set { _forceUpdate = value; OnPropertyChanged(nameof(ForceUpdate)); } }

        private double _downloadProgress;
        public double DownloadProgress { get => _downloadProgress; set { _downloadProgress = value; OnPropertyChanged(nameof(DownloadProgress)); } }

        private string _updateStatusText = "Tải về và Cập nhật";
        public string UpdateStatusText { get => _updateStatusText; set { _updateStatusText = value; OnPropertyChanged(nameof(UpdateStatusText)); } }
        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        private bool _autoUpdateTriggered;

        protected void OnPropertyChanged(string propertyName)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        public UpdateService()
        {
        }

        public void Start()
        {
            if (_timer != null && _timer.Enabled)
            {
                Debug.WriteLine("[UpdateService] Timer is already running. Start request ignored.");
                return;
            }
            Debug.WriteLine("[UpdateService] Starting update check service.");

            // === THAY ĐỔI: Đặt timer thành 5 phút ===
            _timer = new System.Timers.Timer(5 * 60 * 1000); // 5 phút
            _timer.Elapsed += async (s, e) => await CheckForUpdateAsync();
            _timer.AutoReset = true;

            // Chạy kiểm tra ngay lập tức mà không cần đợi timer tick lần đầu
            Task.Run(CheckForUpdateAsync);

            _timer.Enabled = true;
        }
        public void Stop()
        {
            if (_timer != null)
            {
                Debug.WriteLine("[UpdateService] Stopping update check service.");
                _timer.Enabled = false;
                _timer.Dispose();
                _timer = null;
            }
            // Reset lại trạng thái UI
            IsUpdateAvailable = false;
        }

        public async Task CheckForUpdateAsync()
        {
            if (IsUpdating) return;

            bool isLoggedIn = false;
            Application.Current?.Dispatcher.Invoke(() => isLoggedIn = App.User.IsLoggedIn);
            if (!isLoggedIn)
            {
                IsUpdateAvailable = false;
                ForceUpdate = false;
                _autoUpdateTriggered = false;
                ReleaseNotes = "Vui lòng đăng nhập để xem thông tin cập nhật.";
                return;
            }

            var (success, updateInfo) = await ApiService.CheckForUpdateAsync();

            if (!success || updateInfo == null)
            {
                Debug.WriteLine("[UpdateService] Failed to get update info from server.");
                ForceUpdate = false;
                _autoUpdateTriggered = false;
                return;
            }

            try
            {
                ReleaseNotes = updateInfo.ReleaseNotes;
                DownloadUrl = updateInfo.DownloadUrl;
                LatestVersion = updateInfo.LatestVersion;
                ForceUpdate = updateInfo.ForceUpdate;
                string currentVersionStr = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                IsUpdateAvailable = IsNewerVersion(updateInfo.LatestVersion, currentVersionStr) && !string.IsNullOrEmpty(DownloadUrl);
                Debug.WriteLine($"[UpdateService] Check complete. Current: {currentVersionStr}, Latest: {updateInfo.LatestVersion}, Update Available: {IsUpdateAvailable}, ForceUpdate: {ForceUpdate}");

                // === FORCE UPDATE LOGIC ===
                // Chỉ auto update nếu:
                // 1. Có bản cập nhật mới
                // 2. Admin bật ForceUpdate trên server
                // 3. User đang ở Homepage (không có window/tab nào khác đang mở)
                // 4. Chưa trigger auto update trong session này
                if (IsUpdateAvailable && ForceUpdate && !_autoUpdateTriggered)
                {
                    // Kiểm tra xem user có đang ở homepage không
                    bool isAtHomepage = false;
                    Application.Current?.Dispatcher.Invoke(() => 
                    {
                        isAtHomepage = (Application.Current as App)?.IsOnlyHomepageVisible() ?? false;
                    });

                    if (isAtHomepage)
                    {
                        _autoUpdateTriggered = true;
                        Debug.WriteLine("[UpdateService] User at homepage. Starting forced auto-update...");
                        UpdateStatusText = "Đang cập nhật bắt buộc...";
                        await StartUpdateAsync();
                    }
                    else
                    {
                        // User đang làm việc - chỉ hiển thị thông báo, không auto update
                        Debug.WriteLine("[UpdateService] User is working. Showing notification instead of auto-update.");
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            (Application.Current as App)?.ShowNotification(
                                $"⚠️ Có bản cập nhật bắt buộc ({updateInfo.LatestVersion}). Vui lòng quay về Homepage để cập nhật.", 
                                isError: true);
                        });
                    }
                }
                else if (!ForceUpdate)
                {
                    _autoUpdateTriggered = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error processing update info: {ex.Message}");
                IsUpdateAvailable = false;
                ForceUpdate = false;
            }
        }

        public async Task StartUpdateAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl) || IsUpdating) return;

            IsUpdating = true;
            UpdateStatusText = "Đang khởi động trình cập nhật...";
            DownloadProgress = 0;

            try
            {
                string appPath = Process.GetCurrentProcess().MainModule.FileName;
                string appDir = Path.GetDirectoryName(appPath);
                string updaterPath = Path.Combine(appDir, "updater.exe");

                // Kiểm tra updater.exe có tồn tại không
                if (!File.Exists(updaterPath))
                {
                    throw new FileNotFoundException($"Không tìm thấy trình cập nhật: {updaterPath}");
                }

                // Khởi động updater với tham số: downloadUrl và appExePath
                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{DownloadUrl}\" \"{appPath}\"",
                    UseShellExecute = true,
                    WorkingDirectory = appDir
                };

                Process.Start(startInfo);

                // Đợi một chút để updater khởi động
                await Task.Delay(500);

                // Đóng ứng dụng để updater có thể cập nhật
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                if (ex is Win32Exception winEx && winEx.NativeErrorCode == 1223)
                {
                    UpdateStatusText = "Đã hủy cập nhật";
                }
                else
                {
                    UpdateStatusText = $"Lỗi: {ex.Message}";
                }
                IsUpdating = false;
                OnPropertyChanged(nameof(UpdateStatusText));
                OnPropertyChanged(nameof(IsUpdating));
            }
        }
        private bool IsNewerVersion(string latestStr, string currentStr)
        {
            try
            {
                var latestVer = new Version(latestStr.TrimStart('v'));
                var currentVer = new Version(currentStr);
                return latestVer > currentVer;
            }
            catch
            {
                return false;
            }
        }
    }
}