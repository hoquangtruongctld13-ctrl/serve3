using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.Threading.Tasks;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // << Rất quan trọng: Cho phép truy cập công khai
    public class AppUpdateController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AppUpdateController(AppDbContext context)
        {
            _context = context;
        }

        // DTO để định dạng dữ liệu trả về cho client
        public record UpdateCheckResponse(string LatestVersion, string DownloadUrl, string ReleaseNotes);

        [HttpGet("check")]
        public async Task<IActionResult> CheckForUpdate()
        {
            // Luôn lấy bản ghi có Id = 1
            var updateInfo = await _context.UpdateInfos
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(u => u.Id == 1);

            if (updateInfo == null)
            {
                // Nếu không có thông tin, trả về lỗi 404
                return NotFound(new { message = "Không tìm thấy thông tin cập nhật." });
            }

            // Trả về dữ liệu dưới dạng JSON
            var response = new UpdateCheckResponse(
                updateInfo.LatestVersion,
                updateInfo.DownloadUrl,
                updateInfo.ReleaseNotes ?? string.Empty // Đảm bảo không trả về null
            );

            return Ok(response);
        }
    }
}