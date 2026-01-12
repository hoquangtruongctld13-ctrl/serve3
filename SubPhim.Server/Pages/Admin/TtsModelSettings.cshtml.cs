using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using System.ComponentModel.DataAnnotations;

namespace SubPhim.Server.Pages.Admin
{
    public class TtsModelSettingsModel : PageModel
    {
        private readonly AppDbContext _context;

        public TtsModelSettingsModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public TtsModelSetting InputModel { get; set; } = new();

        public IList<TtsModelSetting> Settings { get; set; } = new List<TtsModelSetting>();

        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            Settings = await _context.TtsModelSettings.ToListAsync();
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (!ModelState.IsValid)
            {
                ErrorMessage = "D? li?u nh?p không h?p l?.";
                return Page();
            }

            // Ki?m tra trùng l?p Identifier
            if (await _context.TtsModelSettings.AnyAsync(s => s.Identifier == InputModel.Identifier && s.Provider == InputModel.Provider))
            {
                ErrorMessage = $"C?u hình cho Identifier '{InputModel.Identifier}' ?ã t?n t?i.";
                return Page();
            }

            _context.TtsModelSettings.Add(InputModel);
            await _context.SaveChangesAsync();

            SuccessMessage = $"?ã thêm c?u hình model '{InputModel.ModelName}' thành công.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var setting = await _context.TtsModelSettings.FindAsync(id);
            if (setting != null)
            {
                _context.TtsModelSettings.Remove(setting);
                await _context.SaveChangesAsync();
                SuccessMessage = "?ã xóa c?u hình model.";
            }
            return RedirectToPage();
        }
    }
}