using System;

namespace SubPhim.Server.Helpers
{
    public static class TimeZoneHelper
    {
        private static readonly TimeZoneInfo VietNamTimeZone;

        static TimeZoneHelper()
        {
            try
            {
                VietNamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                VietNamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
        }

        public static DateTime ConvertToVietNamTime(DateTime utcDateTime)
        {
            if (utcDateTime.Kind != DateTimeKind.Utc)
            {
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
            }
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, VietNamTimeZone);
        }

        // =====================================================================
        // GIẢI QUYẾT LỖI Ở ĐÂY
        // =====================================================================

        // PHIÊN BẢN 1: Dành cho DateTime? (nullable, có thể là null)
        // Dùng cho: SubscriptionExpiry
        public static string ToVietNamTimeString(this DateTime? utcDateTime, string format = "dd/MM/yyyy HH:mm")
        {
            if (utcDateTime == null)
            {
                return "Không áp dụng";
            }
            // Gọi lại phiên bản không-nullable để tái sử dụng code
            return utcDateTime.Value.ToVietNamTimeString(format);
        }

        // PHIÊN BẢN 2: Dành cho DateTime (non-nullable, luôn có giá trị)
        // Dùng cho: CreatedAt, LastLogin
        public static string ToVietNamTimeString(this DateTime utcDateTime, string format = "dd/MM/yyyy HH:mm")
        {
            var vietNamTime = ConvertToVietNamTime(utcDateTime);
            return vietNamTime.ToString(format);
        }
    }
}