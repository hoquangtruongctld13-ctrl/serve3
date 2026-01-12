using subphimv1.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace subphimv1
{
    public partial class HomepageWindow : Window
    {
        private const int DefaultServerIndex = 0;
        private bool _isUpdatingComboBoxes = false;
        private Timer _notificationTimer;

        public HomepageWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        #region Window Loading and Initialization
        private async void HomepageWindow_Loaded(object sender, RoutedEventArgs e)
        {
            int selectedIndex = DefaultServerIndex;
            string serverUrl = ApiService.GetServerUrl();

            _isUpdatingComboBoxes = true;
            LoginServerComboBox.SelectedIndex = selectedIndex;
            RegisterServerComboBox.SelectedIndex = selectedIndex;
            ForgotServerComboBox.SelectedIndex = selectedIndex;
            _isUpdatingComboBoxes = false;

            ApiService.UpdateApiBaseUrl(serverUrl);

            // Đăng ký lắng nghe thay đổi của IsLoggedIn để tự động load credentials khi bị logout
            App.User.PropertyChanged += User_PropertyChanged;

            var (savedUser, savedPass) = CredentialService.LoadCredentials();
            if (!string.IsNullOrEmpty(savedUser) && !string.IsNullOrEmpty(savedPass))
            {
                UsernameTextBox.Text = savedUser;
                PasswordBox.Password = savedPass;
                RememberMeCheckBox.IsChecked = true;
                LoginButton_Click(this, new RoutedEventArgs());
            }
        }

        /// <summary>
        /// Xử lý khi IsLoggedIn thay đổi từ true sang false (bị logout do mất kết nối).
        /// Tự động load lại credentials đã lưu vào form để user có thể login lại nhanh.
        /// </summary>
        private void User_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(App.User.IsLoggedIn) && !App.User.IsLoggedIn)
            {
                // User bị logout - kiểm tra xem có credentials đã lưu không
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var (savedUser, savedPass) = CredentialService.LoadCredentials();
                    if (!string.IsNullOrEmpty(savedUser) && !string.IsNullOrEmpty(savedPass))
                    {
                        // Chỉ load lại nếu form hiện tại trống (không bị xóa bởi user logout chủ động)
                        if (string.IsNullOrEmpty(UsernameTextBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
                        {
                            UsernameTextBox.Text = savedUser;
                            PasswordBox.Password = savedPass;
                            RememberMeCheckBox.IsChecked = true;
                        }
                    }
                }));
            }
        }

        #endregion

        #region Window Controls
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }
        #endregion

        #region View Switching (Login, Register, Forgot Password)
        private void SwitchToRegister_Click(object sender, RoutedEventArgs e)
        {
            LoginView.Visibility = Visibility.Collapsed;
            ForgotPasswordView.Visibility = Visibility.Collapsed;
            RegisterView.Visibility = Visibility.Visible;
            StatusMessageText.Visibility = Visibility.Collapsed;
            RegStatusMessageText.Visibility = Visibility.Collapsed;
        }

        private void SwitchToLogin_Click(object sender, RoutedEventArgs e)
        {
            RegisterView.Visibility = Visibility.Collapsed;
            ForgotPasswordView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            StatusMessageText.Visibility = Visibility.Collapsed;
            RegStatusMessageText.Visibility = Visibility.Collapsed;
        }

        private void SwitchToForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            LoginView.Visibility = Visibility.Collapsed;
            RegisterView.Visibility = Visibility.Collapsed;
            ForgotPasswordView.Visibility = Visibility.Visible;
        }
        #endregion

        #region Authentication and User Actions
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Đang kiểm tra...";
            StatusMessageText.Visibility = Visibility.Collapsed;
            bool shouldRemember = RememberMeCheckBox.IsChecked ?? false;
            App.User.RememberMe = shouldRemember;

            var result = await App.User.Login(UsernameTextBox.Text, PasswordBox.Password);

            if (result.success)
            {
                // -- START FIX: Thêm try-catch để bắt lỗi Element not found khi chuyển view --
                try
                {
                    (Application.Current as App)?.StartProfileRefreshTimer();
                    App.Updater?.Start();
                    await UpdateDashboardStatsAsync();
                    UpdateServerStatusDisplay();
                    ShowNotification("Đăng nhập thành công!", isError: false);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Lỗi trong quá trình xử lý sau đăng nhập:\n{ex.Message}\n\nTrace: {ex.StackTrace}", "Debug Login", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                // -- END FIX --
            }
            else
            {
                StatusMessageText.Text = result.message;
                StatusMessageText.Visibility = Visibility.Visible;
            }

            LoginButton.IsEnabled = true;
            LoginButton.Content = "Đăng Nhập";
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            RegStatusMessageText.Visibility = Visibility.Collapsed;
            bool isValid = true;
            if (RegPasswordBox.Password.Length < 6)
            {
                RegStatusMessageText.Text = "Mật khẩu phải có ít nhất 6 ký tự.";
                RegStatusMessageText.Visibility = Visibility.Visible;
                isValid = false;
            }
            if (RegPasswordBox.Password != RegConfirmPasswordBox.Password)
            {
                RegStatusMessageText.Text = "Mật khẩu xác nhận không khớp.";
                RegStatusMessageText.Visibility = Visibility.Visible;
                isValid = false;
            }
            if (!RegEmailTextBox.Text.Contains("@") || !RegEmailTextBox.Text.Contains("."))
            {
                RegStatusMessageText.Text = "Định dạng email không hợp lệ.";
                RegStatusMessageText.Visibility = Visibility.Visible;
                isValid = false;
            }

            if (!isValid) return;

            RegisterButton.IsEnabled = false;
            RegisterButton.Content = "Đang xử lý...";
            string usernameToRegister = RegUsernameTextBox.Text;
            string passwordToRegister = RegPasswordBox.Password;
            string emailToRegister = RegEmailTextBox.Text;

            var registerResult = await App.User.Register(usernameToRegister, passwordToRegister, emailToRegister);

            if (registerResult.success)
            {
                RegisterButton.Content = "Đang đăng nhập...";
                App.User.RememberMe = true;
                var loginResult = await App.User.Login(usernameToRegister, passwordToRegister);
                if (loginResult.success)
                {
                    (Application.Current as App)?.StartProfileRefreshTimer();
                    App.Updater?.Start();
                    await UpdateDashboardStatsAsync();
                    UpdateServerStatusDisplay();
                    ShowNotification("Đăng ký và đăng nhập thành công!", isError: false);
                }
                else
                {
                    SwitchToLogin_Click(null, null);
                    UsernameTextBox.Text = usernameToRegister;
                    PasswordBox.Password = passwordToRegister;
                    StatusMessageText.Text = $"Lỗi tự động đăng nhập: {loginResult.message}";
                    StatusMessageText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                RegStatusMessageText.Text = registerResult.message;
                RegStatusMessageText.Visibility = Visibility.Visible;
            }

            RegisterButton.IsEnabled = true;
            RegisterButton.Content = "Đăng Ký";
        }

        private async void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            ForgotPasswordButton.IsEnabled = false;
            ForgotPasswordButton.Content = "Đang gửi...";
            ForgotStatusMessageText.Visibility = Visibility.Collapsed;

            string email = ForgotEmailTextBox.Text;
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ForgotStatusMessageText.Text = "Vui lòng nhập một địa chỉ email hợp lệ.";
                ForgotStatusMessageText.Foreground = (SolidColorBrush)FindResource("WarningBrush");
                ForgotStatusMessageText.Visibility = Visibility.Visible;
                ForgotPasswordButton.IsEnabled = true;
                ForgotPasswordButton.Content = "Gửi Yêu Cầu";
                return;
            }
            var (success, message) = await ApiService.ForgotPasswordAsync(email);
            ForgotStatusMessageText.Text = message;
            ForgotStatusMessageText.Foreground = success ? (SolidColorBrush)FindResource("SuccessBrush") : (SolidColorBrush)FindResource("ErrorBrush");
            ForgotStatusMessageText.Visibility = Visibility.Visible;

            await Task.Delay(5000);
            ForgotPasswordButton.IsEnabled = true;
            ForgotPasswordButton.Content = "Gửi Yêu Cầu";
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            App.User.ForceLogoutAndClearCredentials();
            ProfilePopup.IsOpen = false;
            UsernameTextBox.Text = "";
            PasswordBox.Password = "";
            ShowNotification("Đã đăng xuất!", isError: false);
        }
        #endregion

        #region Profile Popup & Actions
        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
        }

        private void UpgradeButton_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            new UpgradeWindow { Owner = this }.ShowDialog();
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            ChangePasswordOverlay.Visibility = Visibility.Visible;
            CurrentPasswordBox.Focus();
        }

        private async void ResetDevices_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            var result = MessageBox.Show("Bạn có chắc chắn muốn xóa tất cả các thiết bị đã đăng nhập khỏi tài khoản này?\nHành động này chỉ có thể thực hiện 1 lần mỗi 24 giờ.",
                                         "Xác nhận Reset Thiết Bị", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var (success, message) = await ApiService.ResetDevicesAsync();
                ShowNotification(message, isError: !success);
            }
        }
        #endregion

        #region Change Password Overlay Logic
        private void CancelChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            ChangePasswordOverlay.Visibility = Visibility.Collapsed;
            CurrentPasswordBox.Password = string.Empty;
            NewPasswordBox.Password = string.Empty;
            ConfirmNewPasswordBox.Password = string.Empty;
            ChangePasswordStatusText.Visibility = Visibility.Collapsed;
        }

        private async void SubmitChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            ChangePasswordStatusText.Visibility = Visibility.Collapsed;
            string currentPass = CurrentPasswordBox.Password;
            string newPass = NewPasswordBox.Password;
            string confirmPass = ConfirmNewPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(currentPass) || string.IsNullOrWhiteSpace(newPass))
            {
                ChangePasswordStatusText.Text = "Vui lòng điền đầy đủ thông tin.";
                ChangePasswordStatusText.Visibility = Visibility.Visible;
                return;
            }
            if (newPass.Length < 6)
            {
                ChangePasswordStatusText.Text = "Mật khẩu mới phải có ít nhất 6 ký tự.";
                ChangePasswordStatusText.Visibility = Visibility.Visible;
                return;
            }
            if (newPass != confirmPass)
            {
                ChangePasswordStatusText.Text = "Mật khẩu xác nhận không khớp.";
                ChangePasswordStatusText.Visibility = Visibility.Visible;
                return;
            }

            SubmitChangePasswordButton.IsEnabled = false;
            SubmitChangePasswordButton.Content = "Đang xử lý...";
            var (success, message) = await ApiService.ChangePasswordAsync(currentPass, newPass);
            if (success)
            {
                ShowNotification("Đổi mật khẩu thành công! Sẽ tự động đăng xuất...", isError: false);
                await Task.Delay(2000);
                CancelChangePasswordButton_Click(null, null);
                App.User.ForceLogoutAndClearCredentials();
            }
            else
            {
                ChangePasswordStatusText.Text = message;
                ChangePasswordStatusText.Visibility = Visibility.Visible;
            }
            SubmitChangePasswordButton.IsEnabled = true;
            SubmitChangePasswordButton.Content = "Lưu Thay Đổi";
        }
        #endregion

        #region Feature Handling
        private async void FeatureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton || !App.User.IsLoggedIn) return;

            string buttonName = clickedButton.Name;
            string featureKey;

            // Xác định tính năng dựa vào tên của Button
            if (buttonName.Contains("WatchMovie")) featureKey = "WatchMovie";
            else if (buttonName.Contains("Tts")) featureKey = "TTS"; // Key nhận diện cho nút TTS
            else if (buttonName.Contains("SubPhim")) featureKey = "SubPhim";
            else if (buttonName.Contains("DichThuat")) featureKey = "DichThuat";
            else if (buttonName.Contains("OcrTruyen")) featureKey = "OcrTruyen";
            else if (buttonName.Contains("EditTruyen")) featureKey = "EditTruyen";
            else if (buttonName.Contains("CapCutVoice")) featureKey = "CapCutVoice";
            else if (buttonName.Contains("Capcut")) featureKey = "Capcut";
            else if (buttonName.Contains("Jianying")) featureKey = "Jianying";
            else if (buttonName.Contains("Veo3")) featureKey = "Veo3";
            else return; // Bỏ qua nếu không nhận diện được Button

            System.Diagnostics.Debug.WriteLine($"[Homepage] User clicked feature: {featureKey}");

            // --- XỬ LÝ CÁC TÍNH NĂNG OFFLINE/KHÔNG CẦN CHECK SERVER ---

            if (featureKey == "WatchMovie")
            {
                new MovieBrowserWindow { Owner = this }.Show();
                return;
            }

            // Logic mở TTS Studio
            if (featureKey == "TTS")
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Homepage] Checking TTS access with Capcut API...");
                    
                    // Kiểm tra quyền truy cập API Capcut trước khi cho phép mở TTS Studio
                    var (ttsHasAccess, ttsMessage) = await ApiService.CheckFeatureAccessAsync("Capcut");
                    
                    if (!ttsHasAccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Homepage] TTS access denied: {ttsMessage}");
                        ShowNotification(ttsMessage, isError: true);
                        return;
                    }
                    
                    System.Diagnostics.Debug.WriteLine("[Homepage] TTS access granted. Opening TtsStudioWindow...");
                    // Khởi tạo và hiển thị cửa sổ TTS Studio
                    var ttsWindow = new TtsStudioWindow();
                    ttsWindow.Owner = this;
                    ttsWindow.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Homepage] Error opening TtsStudioWindow: {ex.Message}");
                    ShowNotification("Không thể mở AIO TTS Studio. Vui lòng kiểm tra log.", true);
                }
                return; // Return ngay để không chạy xuống phần check API bên dưới
            }

            if (featureKey == "CapCutVoice")
            {
                new CapCutVoiceWindow { Owner = this }.Show();
                return;
            }
            if (featureKey == "Veo3")
            {
                new VEO3.VEO3Window { Owner = this }.Show();
                return;
            }

            // --- TÍNH NĂNG ĐANG BẢO TRÌ ---
            if (featureKey == "Capcut")
            {
                ShowNotification("🔧 Tính năng CapCut Pro đang được bảo trì. Vui lòng quay lại sau!", isError: true);
                return;
            }

            // --- XỬ LÝ CÁC TÍNH NĂNG CẦN GỌI SERVER API ---

            string featureNameToAskServer = featureKey;

            // Thêm log trước khi gọi API kiểm tra quyền
            System.Diagnostics.Debug.WriteLine($"[Homepage] Checking server access for: {featureNameToAskServer}");

            var (hasAccess, message) = await ApiService.CheckFeatureAccessAsync(featureNameToAskServer);

            if (hasAccess)
            {
                switch (featureKey)
                {
                    case "SubPhim": (Application.Current as App)?.ShowMainWindow(); break;
                    case "DichThuat": new TranslateWindow { Owner = this }.Show(); break;
                    case "OcrTruyen": (Application.Current as App)?.ShowOcrComicWindow(); break;
                    case "Capcut": (Application.Current as App)?.ShowCapcutWindow(); break;
                    case "Jianying": (Application.Current as App)?.ShowJianyingWindow(); break;
                    case "EditTruyen": ShowNotification("Chức năng đang được phát triển", isError: true); break;
                }
            }
            else
            {
                ShowNotification(message, isError: true);
            }
        }
        private void Navigation_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton checkedButton || this.IsLoaded == false) return;

            // Lấy Grid chứa Dashboard và Features 
            var dashboardGrid = FindName("DashboardView") as Grid;
            var featuresGrid = FindName("FeaturesView") as Grid;

            if (dashboardGrid == null || featuresGrid == null) return;

            if (checkedButton.Name == "DashboardButton")
            {
                dashboardGrid.Visibility = Visibility.Visible;
                featuresGrid.Visibility = Visibility.Collapsed;
            }
            else if (checkedButton.Name == "FeaturesButton")
            {
                dashboardGrid.Visibility = Visibility.Collapsed;
                featuresGrid.Visibility = Visibility.Visible;
            }
        }
        #endregion

        #region Server Update
        private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingComboBoxes || sender is not ComboBox comboBox || comboBox.SelectedIndex < 0) return;
            int selectedIndex = comboBox.SelectedIndex;
            string selectedUrl = ApiService.GetServerUrl();
            ApiService.UpdateApiBaseUrl(selectedUrl);
            UpdateServerStatusDisplay();

            _isUpdatingComboBoxes = true;
            if (LoginView.IsVisible) { RegisterServerComboBox.SelectedIndex = comboBox.SelectedIndex; ForgotServerComboBox.SelectedIndex = comboBox.SelectedIndex; }
            if (RegisterView.IsVisible) { LoginServerComboBox.SelectedIndex = comboBox.SelectedIndex; ForgotServerComboBox.SelectedIndex = comboBox.SelectedIndex; }
            if (ForgotPasswordView.IsVisible) { LoginServerComboBox.SelectedIndex = comboBox.SelectedIndex; RegisterServerComboBox.SelectedIndex = comboBox.SelectedIndex; }
            _isUpdatingComboBoxes = false;
            ShowNotification($"Đang sử dụng máy chủ: {selectedUrl}", isError: false);
        }

        private void ServerComboBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingComboBoxes || sender is not ComboBox comboBox || comboBox.SelectedIndex < 0) return;
            int selectedIndex = comboBox.SelectedIndex;
            string selectedUrl = ApiService.GetServerUrl();
            ApiService.UpdateApiBaseUrl(selectedUrl);
            UpdateServerStatusDisplay();
            _isUpdatingComboBoxes = true;
            LoginServerComboBox.SelectedIndex = comboBox.SelectedIndex;
            RegisterServerComboBox.SelectedIndex = comboBox.SelectedIndex;
            ForgotServerComboBox.SelectedIndex = comboBox.SelectedIndex;
            _isUpdatingComboBoxes = false;
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            // Gọi trực tiếp updater.exe thông qua UpdateService thay vì hiển thị cửa sổ download cũ
            if (App.Updater != null && App.Updater.IsUpdateAvailable)
            {
                await App.Updater.StartUpdateAsync();
            }
        }
        #endregion

        #region UI Helpers
        private async Task UpdateDashboardStatsAsync()
        {
            // Chỉ cần gọi hàm refresh chung của App,
            // DataBinding sẽ tự động cập nhật UI.
            await (Application.Current as App)?.RefreshUserProfileNow();
        }
        private void CommunityButton_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.ToggleChatWindow();
        }
        private void UpdateServerStatusDisplay()
        {
            string serverName = "Máy chủ mặc định";
            if (LoginServerComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content != null)
            {
                serverName = selectedItem.Content.ToString();
            }
            ServerStatusText.Text = $" | {serverName} - Hoạt động";
        }
        public void ShowNotification(string message, bool isError)
        {
            try
            {
                _notificationTimer?.Dispose();

                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => ShowNotification(message, isError));
                    return;
                }

                var successColor = ((SolidColorBrush)FindResource("SuccessBrush")).Color;
                var errorColor = ((SolidColorBrush)FindResource("ErrorBrush")).Color;

                // Kiểm tra element có null không trước khi thao tác
                if (NotificationText == null || NotificationBar == null || NotificationShadow == null)
                    return;

                NotificationText.Text = message;
                var targetColor = isError ? errorColor : successColor;
                NotificationBar.BorderBrush = new SolidColorBrush(targetColor);
                NotificationShadow.Color = targetColor;

                NotificationBar.Visibility = Visibility.Visible;

                // -- FIX: Animation thường gây lỗi Element not found nếu visual tree chưa sẵn sàng
                if (NotificationBar.RenderTransform is TranslateTransform transform)
                {
                    var showAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    transform.BeginAnimation(TranslateTransform.YProperty, showAnimation);

                    _notificationTimer = new Timer(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var hideAnimation = new DoubleAnimation(-150, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                                hideAnimation.Completed += (s, e) => NotificationBar.Visibility = Visibility.Collapsed;
                                transform.BeginAnimation(TranslateTransform.YProperty, hideAnimation);
                            }
                            catch { /* Bỏ qua lỗi animation khi ẩn */ }
                        });
                    }, null, 3000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                // Nếu lỗi ở đây, nó sẽ hiện ra MessageBox
                CustomMessageBox.Show($"Lỗi Notification Animation: {ex.Message}", "Debug UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion
    }
}
