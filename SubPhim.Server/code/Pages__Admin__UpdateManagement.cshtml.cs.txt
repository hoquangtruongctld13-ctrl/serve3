// VỊ TRÍ: Pages/Admin/UpdateManagement.cshtml.cs
// TẠO FILE MỚI

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SubPhim.Server.Data;
using System.Threading.Tasks;

namespace SubPhim.Server.Pages.Admin
{
    public class UpdateManagementModel : PageModel
    {
        private readonly AppDbContext _context;

        public UpdateManagementModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public UpdateInfo UpdateInfo { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Luôn lấy bản ghi có Id = 1
            UpdateInfo = await _context.UpdateInfos.FindAsync(1);

            if (UpdateInfo == null)
            {
                // Điều này không nên xảy ra vì đã seed data, nhưng để phòng hờ
                return NotFound("Không tìm thấy bản ghi thông tin cập nhật.");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Cập nhật thời gian
            UpdateInfo.LastUpdated = DateTime.UtcNow;

            _context.UpdateInfos.Update(UpdateInfo);
            await _context.SaveChangesAsync();

            SuccessMessage = "Đã cập nhật thông tin phiên bản mới thành công!";
            return RedirectToPage();
        }
    }
}