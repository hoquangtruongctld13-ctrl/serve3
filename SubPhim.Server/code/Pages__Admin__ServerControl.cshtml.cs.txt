using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace SubPhim.Server.Pages.Admin
{
    public class ServerControlModel : PageModel
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger<ServerControlModel> _logger;

        // Inject IHostApplicationLifetime để có thể điều khiển vòng đời ứng dụng
        public ServerControlModel(IHostApplicationLifetime appLifetime, ILogger<ServerControlModel> logger) // <-- ĐÃ SỬA LỖI
        {
            _appLifetime = appLifetime;
            _logger = logger;
        }

        [TempData]
        public string Message { get; set; }

        public void OnGet()
        {
            // Chỉ hiển thị trang, không cần logic
        }

        // Handler cho nút Khởi động lại
        public IActionResult OnPostRestart()
        {
            var adminUsername = User.Identity.Name ?? "Unknown Admin";
            _logger.LogWarning("Lệnh khởi động lại server được yêu cầu bởi admin: {AdminUsername}", adminUsername);

            // Gửi thông báo về cho trình duyệt của admin trước khi tắt server
            Message = "Lệnh khởi động lại đã được gửi. Server sẽ không khả dụng trong giây lát. Vui lòng làm mới trang sau khoảng 15-30 giây.";

            // Chạy việc tắt ứng dụng trên một luồng khác để request này có thể hoàn thành và gửi Message về.
            // Sau 1 giây, gọi StopApplication() để tắt tiến trình.
            Task.Delay(1000).ContinueWith(t =>
            {
                _appLifetime.StopApplication();
            });

            // Trả về trang ngay lập tức với thông báo
            return Page();
        }
    }
}