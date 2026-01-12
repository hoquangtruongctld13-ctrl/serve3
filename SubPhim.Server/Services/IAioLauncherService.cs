
using SubPhim.Server.Services.Aio;
using System.Threading.Tasks;

namespace SubPhim.Server.Services
{
    public interface IAioLauncherService
    {
        Task<CreateJobResult> CreateJobAsync(int userId, AioTranslationRequest request);
        Task<JobResult> GetJobResultAsync(string sessionId, int userId);
    }
}