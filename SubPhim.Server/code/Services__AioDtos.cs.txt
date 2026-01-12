namespace SubPhim.Server.Services.Aio
{
    // DTO client gửi lên
    public record AioTranslationRequest(string SystemInstruction, string Content, string TargetLanguage = "Vietnamese");

    // DTO server trả về khi tạo job
    public record CreateJobResult(bool IsSuccess, string Message, string SessionId = null);

    // DTO server trả về khi client hỏi kết quả
    public record JobResult(string Status, string TranslatedContent, string ErrorMessage);
}