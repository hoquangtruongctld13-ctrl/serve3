using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace SubPhim.Server.Data
{
    public static class SeedData
    {
        // Phương thức này sẽ được gọi từ Program.cs
        public static async Task InitializeAsync(AppDbContext context)
        {
            // --- BƯỚC 1: KIỂM TRA XEM CÓ CẦN SEED DATA HAY KHÔNG ---
            // Để tránh việc tạo đi tạo lại mỗi lần khởi động,
            // chúng ta chỉ tạo dữ liệu nếu bảng Users chỉ có 1 tài khoản (là admin).
            // Bạn có thể thay đổi logic này nếu muốn.
            if (await context.Users.CountAsync() > 1)
            {
                return; // Database đã có dữ liệu, không làm gì cả.
            }

            // --- BƯỚC 2: TẠO DANH SÁCH 1000 TÀI KHOẢN ẢO ---
            Console.WriteLine("Bắt đầu tạo 1000 tài khoản ảo...");
            var usersToAdd = new List<User>();
            var random = new Random();

            for (int i = 1; i <= 1000; i++)
            {
                // Chọn ngẫu nhiên một gói trả phí
                var paidTiers = new[] { SubscriptionTier.Daily, SubscriptionTier.Monthly, SubscriptionTier.Yearly };
                var randomPaidTier = paidTiers[random.Next(paidTiers.Length)];

                var user = new User
                {
                    // Chúng ta KHÔNG set ID, hãy để database tự làm
                    Username = $"user{i:D4}", // Sẽ tạo ra user0001, user0002,...
                    Email = $"user{i:D4}@example.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"), // Mật khẩu chung cho tất cả user ảo

                    // Gán 20% là tài khoản Free, 80% là tài khoản trả phí ngẫu nhiên
                    Tier = (i % 5 == 0) ? SubscriptionTier.Free : randomPaidTier,

                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 365)), // Ngày tạo ngẫu nhiên trong năm qua
                    IsBlocked = (i % 50 == 0), // Cứ 50 tài khoản thì khóa 1
                    MaxDevices = (i % 5 == 0) ? 1 : 3, // Gói Free có 1 thiết bị, trả phí có 3

                    // Quyền mặc định cho từng loại
                    AllowedApiAccess = (i % 5 == 0)
                        ? AllowedApis.OpenRouter // Gói Free chỉ được dùng OpenRouter
                        : AllowedApis.ChutesAI | AllowedApis.Gemini | AllowedApis.OpenRouter, // Gói trả phí được dùng tất cả

                    DailyRequestLimitOverride = -1, // Dùng mặc định
                    GrantedFeatures = GrantedFeatures.None,
                };

                // Đặt ngày hết hạn cho các gói trả phí
                if (user.Tier != SubscriptionTier.Free)
                {
                    user.SubscriptionExpiry = DateTime.UtcNow.AddDays(random.Next(1, 365));
                }

                usersToAdd.Add(user);
            }

            // --- BƯỚC 3: LƯU TẤT CẢ VÀO DATABASE MỘT LẦN DUY NHẤT ---
            // Đây là cách làm hiệu quả nhất, thay vì gọi SaveChanges() 1000 lần trong vòng lặp.
            await context.Users.AddRangeAsync(usersToAdd);
            await context.SaveChangesAsync();

            Console.WriteLine("Hoàn tất! Đã tạo thành công 1000 tài khoản ảo.");
        }
    }
}