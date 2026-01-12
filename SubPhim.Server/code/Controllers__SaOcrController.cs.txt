using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubPhim.Server.Data;
using SubPhim.Server.Services;
using System.Security.Claims;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/sa-ocr")]
    [Authorize] 
    public class SaOcrController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<SaOcrController> _logger;

        public SaOcrController(AppDbContext context, IEncryptionService encryptionService, ILogger<SaOcrController> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _logger = logger;
        }
        /// <summary>
        /// Data Transfer Object cho một Service Account OCR.
        /// </summary>
        /// <param name="JsonKey">Nội dung JSON của service account.</param>
        /// <param name="FolderId">ID của thư mục Google Drive tương ứng.</param>
        public record SaOcrKeyDto(string JsonKey, string FolderId);

        /// <summary>
        /// Lấy danh sách các Service Account (dưới dạng JSON) và ID thư mục tương ứng.
        /// </summary>
        /// <returns>Một danh sách các đối tượng chứa JsonKey và FolderId.</returns>
        [HttpGet("keys")]
        public async Task<IActionResult> GetServiceAccountKeys()
        {
            var userId = User.FindFirstValue("id");
            _logger.LogInformation("User ID {UserId} is requesting SA OCR keys.", userId);

            var serviceAccounts = await _context.SaOcrServiceAccounts
                                                .AsNoTracking()
                                                .Where(sa => sa.IsEnabled)
                                                .ToListAsync();

            if (!serviceAccounts.Any())
            {
                _logger.LogWarning("No enabled SA OCR keys found to serve for user {UserId}.", userId);
                return Ok(new List<SaOcrKeyDto>());
            }
            var keysToReturn = new List<SaOcrKeyDto>();
            foreach (var sa in serviceAccounts)
            {
                try
                {
                    var decryptedJson = _encryptionService.Decrypt(sa.EncryptedJsonKey, sa.Iv);
                    keysToReturn.Add(new SaOcrKeyDto(decryptedJson, sa.DriveFolderId));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt SA OCR key for {ClientEmail}. Skipping this key.", sa.ClientEmail);
                }
            }

            _logger.LogInformation("Successfully served {KeyCount} SA OCR keys to user {UserId}.", keysToReturn.Count, userId);
            return Ok(keysToReturn);
        }
    }
}