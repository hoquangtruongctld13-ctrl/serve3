using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;

namespace SubPhim.Server.Pages.Admin
{
    public class GoogleTtsModelsModel : PageModel
    {
        private readonly AppDbContext _context;

        public GoogleTtsModelsModel(AppDbContext context)
        {
            _context = context;
        }

        public IList<GoogleTtsModelConfig> ModelConfigs { get; set; } = new List<GoogleTtsModelConfig>();

        [TempData]
        public string? SuccessMessage { get; set; }
        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            ModelConfigs = await _context.GoogleTtsModelConfigs
                .OrderBy(c => c.ModelType)
                .ToListAsync();
        }
    }
}
