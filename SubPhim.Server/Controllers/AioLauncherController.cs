using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubPhim.Server.Services;
using SubPhim.Server.Services.Aio;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SubPhim.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] 
    public class AioLauncherController : ControllerBase
    {
        private readonly IAioLauncherService _aioLauncherService;
        private readonly ILogger<AioLauncherController> _logger;

        public AioLauncherController(IAioLauncherService aioLauncherService, ILogger<AioLauncherController> logger)
        {
            _aioLauncherService = aioLauncherService;
            _logger = logger;
        }
        [HttpPost("start-translation")]
        public async Task<IActionResult> StartTranslation([FromBody] AioTranslationRequest request)
        {
            var userIdString = User.FindFirstValue("id");
            if (!int.TryParse(userIdString, out int userId))
            {
                return Unauthorized("Token không hợp lệ.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest("Nội dung dịch không được để trống.");
            }
            
            if (string.IsNullOrWhiteSpace(request.SystemInstruction))
            {
                return BadRequest("System Instruction không được để trống.");
            }

            _logger.LogInformation("Received AIO translation request from User ID {UserId}", userId);

            var result = await _aioLauncherService.CreateJobAsync(userId, request);

            if (result.IsSuccess)
            {
                return Accepted(new { result.SessionId, result.Message });
            }
            else
            {

                return StatusCode(429, new { result.Message });
            }
        }
        [HttpGet("get-result/{sessionId}")]
        public async Task<IActionResult> GetResult(string sessionId)
        {
            var userIdString = User.FindFirstValue("id");
            if (!int.TryParse(userIdString, out int userId))
            {
                return Unauthorized("Token không hợp lệ.");
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                return BadRequest("Session ID không được để trống.");
            }

            var result = await _aioLauncherService.GetJobResultAsync(sessionId, userId);

            return Ok(result);
        }
    }
}