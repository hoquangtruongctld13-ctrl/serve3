using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Security.Claims;

namespace SubPhim.Server.Pages.Admin
{
    // Vẫn giữ lại để tránh lỗi 400
    [IgnoreAntiforgeryToken]
    public class LoginModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(AppDbContext context, ILogger<LoginModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Không cần các thuộc tính này nữa vì chúng ta đọc trực tiếp
        // public string Username { get; set; }
        // public string Password { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string username, string password, string returnUrl = null)
        {
            if (string.IsNullOrEmpty(username))
            {
                return Page();
            }
            // ...
            var userInDb = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToUpper() == username.ToUpper());

            if (userInDb == null || !BCrypt.Net.BCrypt.Verify(password, userInDb.PasswordHash))
            {
                ErrorMessage = "Tài khoản hoặc mật khẩu không đúng.";
                return RedirectToPage(new { returnUrl });
            }
            if (!userInDb.IsAdmin)
            {
                _logger.LogWarning("Nỗ lực đăng nhập Admin không thành công từ tài khoản không phải admin: {Username}", username);
                ErrorMessage = "Tài khoản này không có quyền truy cập vào trang quản trị.";
                return RedirectToPage(new { returnUrl });
            }

            _logger.LogInformation("Admin login successful for user: {Username}", username);

            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, userInDb.Username),
        new Claim("Admin", "true")
    };
            var claimsIdentity = new ClaimsIdentity(claims, "AdminCookie");
            var authProperties = new AuthenticationProperties { IsPersistent = true };
            await HttpContext.SignInAsync("AdminCookie", new ClaimsPrincipal(claimsIdentity), authProperties);

            return LocalRedirect(returnUrl ?? "/Admin/Users");
        }
    }
}