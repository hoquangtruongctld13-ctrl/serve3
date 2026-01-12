namespace SubPhim.Server.Middleware;
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Log request đến
        _logger.LogWarning("====== INCOMING REQUEST ====== Method: {Method}, Path: {Path}, ContentType: {ContentType}",
            context.Request.Method,
            context.Request.Path,
            context.Request.ContentType);

        // Chuyyển request cho middleware tiếp theo trong pipeline
        await _next(context);

        // Log response đi
        _logger.LogWarning("====== OUTGOING RESPONSE ====== Status Code: {StatusCode}", context.Response.StatusCode);
    }
}