using subphimv1.Services;
using subphimv1.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;

namespace subphimv1
{
    public partial class App : Application
    {
        private const string AppMutexName = "{D8B68FD9-3A8B-4A77-A8C8-6B03348E73AD}";
        private static Mutex _mutex = null;
        private CapcutWindow _capcutWindow;
        private JianyingWindow _jianyingWindow;
        public static ChatService ChatSvc { get; private set; }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        private ChatWindow _chatWindow;
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;
        private const bool IsApiKeyCheckEnabled = false;
        private const bool IsAntiDebugEnabled = false;
        private const string RequiredApiKey = "0986760738";
        public static UserViewModel User { get; private set; }
        public static UpdateService Updater { get; private set; }
        private HomepageWindow _homepageWindow;
        private MainWindow _mainWindow;
        private OcrComicWindow _ocrComicWindow;
        public static string CurrentVersion { get; }

        static App()
        {
            User = new UserViewModel();
            Updater = new UpdateService();
            ChatSvc = new ChatService();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            CurrentVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        }
        public void ShowJianyingWindow()
        {
            if (_jianyingWindow == null || !_jianyingWindow.IsLoaded)
            {
                _jianyingWindow = new JianyingWindow { Owner = _homepageWindow };
                _jianyingWindow.Closed += (s, args) => _jianyingWindow = null;
                _jianyingWindow.Show();
            }
            else
            {
                _jianyingWindow.Activate();
            }
        }
        public void ShowCapcutWindow()
        {
            if (_capcutWindow == null || !_capcutWindow.IsLoaded)
            {
                _capcutWindow = new CapcutWindow
                {
                    Owner = _homepageWindow
                };
                _capcutWindow.Closed += (s, args) => _capcutWindow = null;
                _capcutWindow.Show();
            }
            else
            {
                _capcutWindow.Activate();
            }
        }
        /// <summary>
        /// Gọi khi đăng nhập thành công. Heartbeat trong UserViewModel sẽ tự động refresh mỗi 30 giây,
        /// phương thức này chỉ thực hiện refresh ngay lập tức và cập nhật permissions.
        /// </summary>
        public void StartProfileRefreshTimer()
        {
            // Heartbeat trong UserViewModel đã xử lý việc refresh định kỳ (mỗi 30 giây)
            // Phương thức này chỉ thực hiện refresh ngay lập tức khi được gọi
            _ = RefreshUserProfileNow();
            if (_mainWindow != null)
            {
                _ = _mainWindow.UpdateFeaturePermissionsAsync();
            }
        }

        public void ShowOcrComicWindow()
        {
            if (_ocrComicWindow != null && !_ocrComicWindow.IsVisible)
            {
                _ocrComicWindow.Show();
                _ocrComicWindow.Activate();
            }
            else if (_ocrComicWindow == null)
            {
                _ocrComicWindow = new OcrComicWindow();
                _ocrComicWindow.Closed += (s, args) => _ocrComicWindow = null;
                _ocrComicWindow.Show();
                _ocrComicWindow.Activate();
            }
            else
            {
                _ocrComicWindow.Activate();
            }
        }

        /// <summary>
        /// Được gọi khi logout. Trong phiên bản mới, heartbeat được stop trong UserViewModel.Logout(),
        /// nhưng giữ phương thức này cho tương thích ngược nếu có code khác gọi.
        /// </summary>
        public void StopProfileRefreshTimer()
        {
            // Heartbeat được quản lý bởi UserViewModel, không cần làm gì ở đây
        }

        public async Task RefreshUserProfileNow()
        {
            if (User == null || !User.IsLoggedIn)
            {
                StopProfileRefreshTimer();
                return;
            }

            try
            {
                var (success, refreshedUser, message) = await ApiService.RefreshUserProfileAsync();
                var (statusSuccess, usageStatus, statusMessage) = await ApiService.GetUsageStatusAsync();
                
                if (success && refreshedUser != null)
                {
                    User.UpdateFromDto(refreshedUser);
                    User.OnRefreshSuccess(); // Reset bộ đếm lỗi của heartbeat
                    if (statusSuccess)
                    {
                        User.UpdateUsageStatus(usageStatus);
                    }
                }
                else
                {
                    // Heartbeat trong UserViewModel sẽ tự động xử lý việc logout nếu mất kết nối liên tục
                    Debug.WriteLine($"[App] Profile refresh failed: {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Exception during profile refresh: {ex.Message}");
                // Heartbeat sẽ phát hiện và xử lý lỗi kết nối
            }
        }

        public void ToggleChatWindow()
        {

            if (_chatWindow == null || !_chatWindow.IsLoaded)
            {

                _chatWindow = new ChatWindow(ChatSvc, User.Username)
                {
                    Owner = this.MainWindow
                };
                _chatWindow.Show();
            }
            else
            {
                if (_chatWindow.IsVisible)
                {

                    _chatWindow.Hide();
                }
                else
                {
                    _chatWindow.Show();
                    _chatWindow.Activate();
                }
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Debug.WriteLine($"[UNHANDLED EXCEPTION] {ex?.Message}");
                Debug.WriteLine($"Stack: {ex?.StackTrace}");

                CustomMessageBox.Show(
                    $"Lỗi nghiêm trọng:\n{ex?.Message}\n\nỨng dụng sẽ đóng.",
                    "Lỗi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            };

            DispatcherUnhandledException += (s, args) =>
            {
                // -- START FIX: Hiện chi tiết lỗi để debug trên máy khách --
                var errorMsg = $"Lỗi UI: {args.Exception.Message}\n\n" +
                               $"Source: {args.Exception.Source}\n" +
                               $"TargetSite: {args.Exception.TargetSite}\n\n" +
                               $"Stack Trace:\n{args.Exception.StackTrace}";

                CustomMessageBox.Show(
                    errorMsg,
                    "Lỗi Chi Tiết (Debug)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                // -- END FIX --

                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Debug.WriteLine($"[TASK EXCEPTION] {args.Exception.Message}");
                args.SetObserved(); // Không crash app
            };

            bool createdNew;
            _mutex = new Mutex(true, AppMutexName, out createdNew);
            if (!createdNew)
            {
                ActivateExistingInstance();
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            User = new UserViewModel();
            Updater = new UpdateService();

            // Kiểm tra font Segoe MDL2 Assets
            CheckRequiredFonts();

            if (IsApiKeyCheckEnabled) { /* giữ nguyên */ }
            if (IsAntiDebugEnabled) { AntiDebug.Initialize(checkIntervalMilliseconds: 2000); }
            ShowHomepageWindow();
        }

        private void CheckRequiredFonts()
        {
            try
            {
                // Kiểm tra xem font Segoe MDL2 Assets có tồn tại không
                var segoeMdl2Font = new FontFamily("Segoe MDL2 Assets");
                var typeface = new Typeface(segoeMdl2Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                GlyphTypeface glyphTypeface;
                if (!typeface.TryGetGlyphTypeface(out glyphTypeface))
                {
                    // Font không tồn tại
                    Debug.WriteLine("[FONT WARNING] Segoe MDL2 Assets font is not available on this system.");

                    // Thêm fallback font vào Application Resources
                    if (Application.Current.Resources.Contains("IconFontFamily"))
                    {
                        Application.Current.Resources["IconFontFamily"] = new FontFamily("Segoe UI Symbol, Arial");
                    }
                    else
                    {
                        Application.Current.Resources.Add("IconFontFamily", new FontFamily("Segoe UI Symbol, Arial"));
                    }
                }
                else
                {
                    // Font tồn tại
                    if (Application.Current.Resources.Contains("IconFontFamily"))
                    {
                        Application.Current.Resources["IconFontFamily"] = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol");
                    }
                    else
                    {
                        Application.Current.Resources.Add("IconFontFamily", new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol"));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FONT ERROR] Error checking fonts: {ex.Message}");
                // Nếu có lỗi, sử dụng fallback font
                try
                {
                    if (Application.Current.Resources.Contains("IconFontFamily"))
                    {
                        Application.Current.Resources["IconFontFamily"] = new FontFamily("Segoe UI Symbol, Arial");
                    }
                    else
                    {
                        Application.Current.Resources.Add("IconFontFamily", new FontFamily("Segoe UI Symbol, Arial"));
                    }
                }
                catch { }
            }
        }

        private void ActivateExistingInstance()
        {
            var currentProcess = Process.GetCurrentProcess();
            var otherProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                                        .Where(p => p.Id != currentProcess.Id);

            foreach (var process in otherProcesses)
            {
                IntPtr mainWindowHandle = process.MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(mainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(mainWindowHandle);
                    break;
                }
            }
        }
        public void ShowHomepageWindow()
        {
            if (_homepageWindow != null && !_homepageWindow.IsVisible)
            {
                _homepageWindow.Show();
            }
            else if (_homepageWindow == null)
            {
                _homepageWindow = new HomepageWindow();
                _homepageWindow.Closed += (s, args) => _homepageWindow = null;
                _homepageWindow.Show();
            }
            _mainWindow?.Hide();
        }

        /// <summary>
        /// Kiểm tra xem user hiện đang ở màn hình Homepage (không có tab/window nào khác đang mở).
        /// Dùng cho logic ForceUpdate - chỉ auto update khi user không đang làm việc.
        /// </summary>
        public bool IsOnlyHomepageVisible()
        {
            // Chỉ trả về true nếu:
            // 1. Homepage đang hiển thị
            // 2. MainWindow (SubPhim) không hiển thị
            // 3. Không có cửa sổ phụ nào khác đang mở
            
            bool homepageVisible = _homepageWindow != null && _homepageWindow.IsVisible;
            bool mainWindowHidden = _mainWindow == null || !_mainWindow.IsVisible;
            bool ocrWindowHidden = _ocrComicWindow == null || !_ocrComicWindow.IsVisible;
            bool capcutWindowHidden = _capcutWindow == null || !_capcutWindow.IsVisible;
            bool jianyingWindowHidden = _jianyingWindow == null || !_jianyingWindow.IsVisible;
            bool chatWindowHidden = _chatWindow == null || !_chatWindow.IsVisible;
            
            return homepageVisible && mainWindowHidden && ocrWindowHidden && 
                   capcutWindowHidden && jianyingWindowHidden && chatWindowHidden;
        }

        public void ShowNotification(string message, bool isError)
        {
            if (_homepageWindow != null && _homepageWindow.IsVisible)
            {
                _homepageWindow.ShowNotification(message, isError);
            }
            else
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    CustomMessageBox.Show(message, isError ? "Lỗi" : "Thông báo", MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        CustomMessageBox.Show(message, isError ? "Lỗi" : "Thông báo", MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information)
                    );
                }
            }
        }

        public void ShowMainWindow()
        {
            string ffmpegPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
            if (!Directory.Exists(ffmpegPath))
            {
                throw new DirectoryNotFoundException($"FFmpeg directory not found: {ffmpegPath}");
            }
            Unosquare.FFME.Library.FFmpegDirectory = ffmpegPath;
            bool isFirstTimeShow = false;
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (s, args) =>
                {
                    _mainWindow = null;
                    this.ShowHomepageWindow();
                };
                isFirstTimeShow = true;
            }
            _mainWindow.Show();
            _mainWindow.Activate();
            _homepageWindow?.Hide();
            if (isFirstTimeShow)
            {
                _ = _mainWindow.UpdateFeaturePermissionsAsync();
            }
        }
        protected override async void OnExit(ExitEventArgs e)
        {
            if (CapcutPatcher.IsActive)
            {
                await CapcutPatcher.CleanupAsync();
            }
            if (JianyingPatcher.IsActive)
            {
                await JianyingPatcher.CleanupAsync();
            }
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;

            if (IsAntiDebugEnabled)
                AntiDebug.Dispose();

            base.OnExit(e);
        }
    }
}