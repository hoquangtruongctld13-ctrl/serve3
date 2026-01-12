using Microsoft.Win32;
using System;
using System.Security.Cryptography;
using System.Text;

namespace subphimv1.Services
{
    public static class CredentialService
    {
        private const string RegistryKeyPath = @"Software\LauncherAIO";
        private const string UsernameValueName = "Username";
        private const string PasswordValueName = "Password";
        // --- BẮT ĐẦU CODE MỚI ---
        private const string ServerUrlValueName = "ServerUrl";
        // --- KẾT THÚC CODE MỚI ---

        private static readonly byte[] s_entropy = { 1, 8, 3, 2, 5, 4, 7, 6 };

        public static void SaveCredentials(string username, string password)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                if (key == null) return;
                key.SetValue(UsernameValueName, username);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] encryptedPasswordBytes = ProtectedData.Protect(passwordBytes, s_entropy, DataProtectionScope.CurrentUser);
                string encryptedPasswordBase64 = Convert.ToBase64String(encryptedPasswordBytes);
                key.SetValue(PasswordValueName, encryptedPasswordBase64);
            }
            catch (Exception ex)
            {
            }
        }

        public static (string Username, string Password) LoadCredentials()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key == null) return (null, null);
                string username = key.GetValue(UsernameValueName) as string;
                string encryptedPasswordBase64 = key.GetValue(PasswordValueName) as string;

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(encryptedPasswordBase64))
                {
                    return (null, null);
                }

                byte[] encryptedPasswordBytes = Convert.FromBase64String(encryptedPasswordBase64);
                byte[] passwordBytes = ProtectedData.Unprotect(encryptedPasswordBytes, s_entropy, DataProtectionScope.CurrentUser);
                string password = Encoding.UTF8.GetString(passwordBytes);

                return (username, password);
            }
            catch (Exception ex)
            {
                ClearCredentials();
                return (null, null);
            }
        }

        public static void ClearCredentials()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null) return;

                if (key.GetValue(UsernameValueName) != null)
                {
                    key.DeleteValue(UsernameValueName);
                }
                if (key.GetValue(PasswordValueName) != null)
                {
                    key.DeleteValue(PasswordValueName);
                }
            }
            catch (Exception ex)
            {
            }
        }

        // --- BẮT ĐẦU CODE MỚI ---
        /// <summary>
        /// Lưu chỉ số server (0 hoặc 1) vào registry thay vì URL để bảo mật
        /// </summary>
        public static void SaveServerIndex(int serverIndex)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(ServerUrlValueName, serverIndex, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
            }
        }

        /// <summary>
        /// Đọc chỉ số server từ registry. Mặc định trả về 0 (Server 1)
        /// </summary>
        public static int LoadServerIndex()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var value = key?.GetValue(ServerUrlValueName);
                if (value is int intValue)
                {
                    return intValue;
                }
                // Hỗ trợ migration từ string cũ sang int mới
                if (value is string)
                {
                    // Xóa giá trị cũ, trả về mặc định
                    return 0;
                }
                return 0; // Mặc định Server 1
            }
            catch (Exception ex)
            {
                return 0; // Trả về server mặc định nếu có lỗi
            }
        }
        // --- KẾT THÚC CODE MỚI ---
    }
}