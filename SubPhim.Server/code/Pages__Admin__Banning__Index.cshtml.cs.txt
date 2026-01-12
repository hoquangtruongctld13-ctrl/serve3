
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Pages.Admin.Banning
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;

        public IndexModel(AppDbContext context) { _context = context; }

        public IList<BannedDevice> BannedList { get; set; }
        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        [BindProperty]
        public BannedDevice NewBan { get; set; }

        public async Task OnGetAsync()
        {
            BannedList = await _context.BannedDevices
                .OrderByDescending(b => b.BannedAt)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddBanAsync()
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(NewBan.Hwid))
            {
                TempData["ErrorMessage"] = "HWID không được để trống.";
                return RedirectToPage();
            }
            NewBan.BannedAt = DateTime.UtcNow;
            _context.BannedDevices.Add(NewBan);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đã thêm lệnh cấm thành công.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteBanAsync(int id)
        {
            var banToDelete = await _context.BannedDevices.FindAsync(id);
            if (banToDelete != null)
            {
                _context.BannedDevices.Remove(banToDelete);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã gỡ bỏ lệnh cấm.";
            }
            return RedirectToPage();
        }
    }
}