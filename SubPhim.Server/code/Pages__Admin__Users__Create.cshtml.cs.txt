using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SubPhim.Server.Pages.Admin.Users
{
    public class CreateModel : PageModel
    {
        private readonly AppDbContext _context;

        public CreateModel(AppDbContext context)
        {
            _context = context;
        }

        // Dùng một ViewModel riêng để nhận dữ liệu từ form
        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Tên tài khoản là bắt buộc.")]
            public string Username { get; set; }

            [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
            public int MaxDevices { get; set; } = 1;
            public GrantedFeatures GrantedFeatures { get; set; }
            public AllowedApis AllowedApiAccess { get; set; }
        }

        public IActionResult OnGet()
        {
            // Chỉ hiển thị trang form trống
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Kiểm tra xem username hoặc email đã tồn tại chưa
            if (await _context.Users.AnyAsync(u => u.Username == Input.Username))
            {
                ModelState.AddModelError("Input.Username", "Tên tài khoản này đã tồn tại.");
                return Page();
            }
            if (!string.IsNullOrEmpty(Input.Email) && await _context.Users.AnyAsync(u => u.Email == Input.Email))
            {
                ModelState.AddModelError("Input.Email", "Email này đã được sử dụng.");
                return Page();
            }

            // *** BẮT ĐẦU SỬA LỖI: THÊM LẠI LOGIC TẠO UID ***
            string newUid;
            var random = new Random();
            do
            {
                // Tạo một số ngẫu nhiên 9 chữ số
                newUid = random.Next(100_000_000, 1_000_000_000).ToString();
            }
            // Kiểm tra để đảm bảo UID là duy nhất trong DB
            while (await _context.Users.AnyAsync(u => u.Uid == newUid));
            // *** KẾT THÚC SỬA LỖI ***

            // Khởi tạo user mới
            var user = new User
            {
                // *** SỬA LỖI: GÁN GIÁ TRỊ UID MỚI TẠO ***
                Uid = newUid,

                Username = Input.Username,
                Email = Input.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Input.Password),
                Tier = Input.Tier,
                MaxDevices = Input.MaxDevices,
                GrantedFeatures = Input.GrantedFeatures,
                AllowedApiAccess = Input.AllowedApiAccess,
                IsBlocked = false,
                CreatedAt = DateTime.UtcNow,
                LastRequestResetUtc = DateTime.UtcNow,
                LastVideoResetUtc = DateTime.UtcNow,
                DailyRequestCount = 0,
                VideosProcessedToday = 0,
                // Set giá trị mặc định cho các trường giới hạn để tránh lỗi null
                DailyRequestLimitOverride = -1,
                VideoDurationLimitMinutes = 30,
                DailyVideoLimit = 2
            };

            // Tự động set ngày hết hạn dựa trên gói
            DateTime startTime = DateTime.UtcNow;
            switch (Input.Tier)
            {
                case SubscriptionTier.Daily:
                    user.SubscriptionExpiry = startTime.AddDays(1);
                    break;
                case SubscriptionTier.Monthly:
                    user.SubscriptionExpiry = startTime.AddMonths(1);
                    break;
                case SubscriptionTier.Yearly:
                    user.SubscriptionExpiry = startTime.AddYears(1);
                    break;
                case SubscriptionTier.Lifetime:
                    user.SubscriptionExpiry = startTime.AddYears(100);
                    break;
                default: // Free
                    user.SubscriptionExpiry = null;
                    break;
            }

            // Nếu tạo user trả phí, tự động cấp quyền cơ bản nếu chưa được chọn
            if (user.Tier != SubscriptionTier.Free)
            {
                if (user.GrantedFeatures == GrantedFeatures.None)
                {
                    user.GrantedFeatures = (GrantedFeatures.SubPhim | GrantedFeatures.DichThuat | GrantedFeatures.Jianying);
                }

                if (user.AllowedApiAccess == AllowedApis.None)
                {
                    user.AllowedApiAccess = AllowedApis.ChutesAI | AllowedApis.Gemini | AllowedApis.OpenRouter;
                }
                user.DailySrtLineLimit = 999999;
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã tạo thành công người dùng '{user.Username}'.";
            return RedirectToPage("./Index");
        }
    }
}