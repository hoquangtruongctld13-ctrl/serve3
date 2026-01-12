// TẠO FILE MỚI: Pages/Admin/AioLauncherTts.cshtml.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubPhim.Server.Pages.Admin
{
    public class AioLauncherTtsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly AioTtsSaStore _saStore;
        private readonly AioTtsDispatcherService _dispatcher;

        public AioLauncherTtsModel(AppDbContext context, IEncryptionService encryptionService, AioTtsSaStore saStore, AioTtsDispatcherService dispatcher)
        {
            _context = context;
            _encryptionService = encryptionService;
            _saStore = saStore;
            _dispatcher = dispatcher;
        }

        public List<AioTtsServiceAccount> ServiceAccounts { get; set; } = new();

        [BindProperty]
        [Display(Name = "Dán nội dung các file JSON vào đây")]
        public string JsonKeysInput { get; set; }

        [BindProperty]
        [Display(Name = "Loại Model TTS")]
        public GoogleTtsModelType SelectedModelType { get; set; } = GoogleTtsModelType.Chirp3HD;

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            ServiceAccounts = await _context.AioTtsServiceAccounts.OrderBy(sa => sa.ProjectId).ThenBy(sa => sa.ClientEmail).ToListAsync();
        }

        public long GetModelLimit(GoogleTtsModelType modelType)
        {
            var config = _context.GoogleTtsModelConfigs.AsNoTracking().FirstOrDefault(c => c.ModelType == modelType);
            return config?.MonthlyFreeLimit ?? 1_000_000;
        }

        public async Task<IActionResult> OnPostAddKeysAsync()
        {
            if (string.IsNullOrWhiteSpace(JsonKeysInput))
            {
                ErrorMessage = "Chưa nhập nội dung JSON.";
                return RedirectToPage();
            }

            var jsonObjects = ExtractJsonObjects(JsonKeysInput);
            if (!jsonObjects.Any())
            {
                ErrorMessage = "Không tìm thấy đối tượng JSON hợp lệ trong nội dung đã nhập.";
                return RedirectToPage();
            }

            int successCount = 0;
            var errors = new List<string>();

            foreach (var jsonStr in jsonObjects)
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString();
                    var email = root.GetProperty("client_email").GetString();
                    var projectId = root.GetProperty("project_id").GetString();

                    if (type != "service_account" || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(projectId))
                    {
                        errors.Add($"Một JSON không hợp lệ (thiếu thông tin cần thiết).");
                        continue;
                    }

                    if (await _context.AioTtsServiceAccounts.AnyAsync(sa => sa.ClientEmail == email && sa.ModelType == SelectedModelType))
                    {
                        errors.Add($"Bỏ qua: Service Account '{email}' đã tồn tại cho model {SelectedModelType}.");
                        continue;
                    }

                    var (encryptedKey, iv) = _encryptionService.Encrypt(jsonStr);

                    var newSa = new AioTtsServiceAccount
                    {
                        ClientEmail = email,
                        ProjectId = projectId,
                        EncryptedJsonKey = encryptedKey,
                        Iv = iv,
                        ModelType = SelectedModelType,
                        UsageMonth = DateTime.UtcNow.ToString("yyyy-MM")
                    };

                    _context.AioTtsServiceAccounts.Add(newSa);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Lỗi xử lý một JSON: {ex.Message}");
                }
            }

            if (successCount > 0)
            {
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã thêm thành công {successCount} Service Account mới.";
                // Yêu cầu các service tải lại cache
                await _saStore.RefreshCacheAsync();
                _dispatcher.InitializeRouter();
            }
            if (errors.Any())
            {
                ErrorMessage = string.Join("\n", errors);
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var saToDelete = await _context.AioTtsServiceAccounts.FindAsync(id);
            if (saToDelete != null)
            {
                _context.AioTtsServiceAccounts.Remove(saToDelete);
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã xóa thành công SA '{saToDelete.ClientEmail}'.";
                await _saStore.RefreshCacheAsync();
                _dispatcher.InitializeRouter();
            }
            return RedirectToPage();
        }

        private List<string> ExtractJsonObjects(string text)
        {
            var results = new List<string>();
            var regex = new Regex(@"\{[\s\S]*?\}", RegexOptions.Multiline);
            foreach (Match match in regex.Matches(text))
            {
                // Kiểm tra sơ bộ xem có phải là JSON hợp lệ không
                if (match.Value.Contains("\"type\"") && match.Value.Contains("\"client_email\""))
                {
                    results.Add(match.Value);
                }
            }
            return results;
        }
    }
}