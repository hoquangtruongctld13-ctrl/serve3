using System;
using System.Management; 
using System.Security.Cryptography;
using System.Text;

namespace subphimv1.Helpers
{
    public static class HwidHelper
    {

        private static string _hwidCache = null;
        public static string GetHwid()
        {
            if (_hwidCache != null)
            {
                return _hwidCache;
            }

            try
            {
                string motherboardId = GetManagementObjectProperty("Win32_BaseBoard", "SerialNumber");
                string cpuId = GetManagementObjectProperty("Win32_Processor", "ProcessorId");
                string diskId = GetManagementObjectProperty("Win32_DiskDrive", "SerialNumber");
                string rawId = $"MB:{motherboardId}-CPU:{cpuId}-DISK:{diskId}";
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId));
                    _hwidCache = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    return _hwidCache;
                }
            }
            catch (Exception ex)
            {
                string fallbackRawId = $"MACHINE:{Environment.MachineName}-USER:{Environment.UserName}";
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallbackRawId));
                    _hwidCache = "fallback_" + BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    return _hwidCache;
                }
            }
        }
        private static string GetManagementObjectProperty(string wmiClass, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var propertyValue = obj[property]?.ToString();
                        if (!string.IsNullOrEmpty(propertyValue))
                        {
                            return propertyValue.Trim();
                        }
                    }
                }
            }
            catch (Exception ex) {}
            return "N/A"; 
        }
    }
}