using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SubPhim.Server.Pages.Admin
{
    public class SaOcrModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;

        public SaOcrModel(AppDbContext context, IEncryptionService encryptionService)
        {
            _context = context;
            _encryptionService = encryptionService;
        }

        public List<SaOcrServiceAccount> ServiceAccounts { get; set; } = new();

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Nội dung JSON không được để trống.")]
            [Display(Name = "Nội dung file JSON")]
            public string JsonKey { get; set; }

            [Required(ErrorMessage = "ID Thư mục Drive là bắt buộc.")]
            [Display(Name = "ID Thư mục Google Drive")]
            public string DriveFolderId { get; set; }
        }


        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            ServiceAccounts = await _context.SaOcrServiceAccounts.OrderBy(sa => sa.ProjectId).ThenBy(sa => sa.ClientEmail).ToListAsync();
        }

        public async Task<IActionResult> OnPostAddKeyAsync()
        {
            if (!ModelState.IsValid)
            {
                // Tải lại danh sách để hiển thị bên cạnh form
                await OnGetAsync();
                return Page();
            }

            try
            {
                using var doc = JsonDocument.Parse(Input.JsonKey);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString();
                var email = root.GetProperty("client_email").GetString();
                var projectId = root.GetProperty("project_id").GetString();

                if (type != "service_account" || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(projectId))
                {
                    ErrorMessage = "File JSON không hợp lệ hoặc thiếu các trường cần thiết (type, client_email, project_id).";
                    return RedirectToPage();
                }

                if (await _context.SaOcrServiceAccounts.AnyAsync(sa => sa.ClientEmail == email))
                {
                    ErrorMessage = $"Service Account '{email}' đã tồn tại trong hệ thống.";
                    return RedirectToPage();
                }

                var (encryptedKey, iv) = _encryptionService.Encrypt(Input.JsonKey);

                var newSa = new SaOcrServiceAccount
                {
                    ClientEmail = email,
                    ProjectId = projectId,
                    DriveFolderId = Input.DriveFolderId.Trim(), // Loại bỏ khoảng trắng thừa
                    EncryptedJsonKey = encryptedKey,
                    Iv = iv
                };

                _context.SaOcrServiceAccounts.Add(newSa);
                await _context.SaveChangesAsync();

                SuccessMessage = $"Đã thêm thành công Service Account OCR '{email}'.";
            }
            catch (JsonException)
            {
                ErrorMessage = "Nội dung nhập vào không phải là một chuỗi JSON hợp lệ.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Đã xảy ra lỗi không mong muốn: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var saToDelete = await _context.SaOcrServiceAccounts.FindAsync(id);
            if (saToDelete != null)
            {
                _context.SaOcrServiceAccounts.Remove(saToDelete);
                await _context.SaveChangesAsync();
                SuccessMessage = $"Đã xóa thành công SA OCR '{saToDelete.ClientEmail}'.";
            }
            return RedirectToPage();
        }
    }
}