using SubPhim.Server.Data; // Cần thiết để nhận diện SubscriptionTier
using System;
using System.Linq;

namespace SubPhim.Server.Utils
{
    public static class DisplayHelper
    {
        // Hàm này dùng ở trang quản lý User
        public static string GetSubscriptionStatus(SubscriptionTier tier, DateTime? expiry)
        {
            if (tier == SubscriptionTier.Lifetime)
            {
                return "Vĩnh viễn";
            }

            if (tier == SubscriptionTier.Free)
            {
                return "Miễn phí";
            }

            if (expiry == null)
            {
                return "Không xác định";
            }

            if (expiry.Value < DateTime.UtcNow)
            {
                return "Đã hết hạn";
            }

            var timeLeft = expiry.Value - DateTime.UtcNow;

            if (timeLeft.TotalDays >= 1)
            {
                return $"Còn {Math.Floor(timeLeft.TotalDays)} ngày";
            }
            if (timeLeft.TotalHours >= 1)
            {
                return $"Còn {Math.Floor(timeLeft.TotalHours)} giờ";
            }
            if (timeLeft.TotalMinutes > 1)
            {
                return $"Còn {Math.Floor(timeLeft.TotalMinutes)} phút";
            }

            return "Sắp hết hạn";
        }

        // Hàm này dùng ở trang quản lý User để hiển thị các quyền
        public static string FormatFlags<T>(T flags) where T : Enum
        {
            if (Convert.ToInt32(flags) == 0)
            {
                return "<span class=\"badge bg-secondary\">None</span>";
            }
            var setFlags = Enum.GetValues(typeof(T))
                .Cast<T>()
                .Where(f => !f.Equals(default(T)) && flags.HasFlag(f))
                .Select(f => $"<span class=\"badge bg-info text-dark me-1\">{f}</span>");
            return string.Join("", setFlags);
        }

        // Hàm này dùng ở trang quản lý User Details để chuyển giờ UTC sang giờ VN
        public static string ToVietNamTimeString(this DateTime? utcDateTime, string format = "G")
        {
            if (!utcDateTime.HasValue) return "Chưa có";
            try
            {
                var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime.Value, vietnamTimeZone);
                return localTime.ToString(format);
            }
            catch
            {
                return utcDateTime.Value.ToString(format) + " (UTC)";
            }
        }
    }
}