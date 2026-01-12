namespace SubPhim.Server.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
        
        /// <summary>
        /// Gửi email với file đính kèm
        /// </summary>
        Task SendEmailWithAttachmentAsync(
            string toEmail, 
            string subject, 
            string body, 
            byte[] attachmentData, 
            string attachmentFileName,
            string attachmentMimeType = "application/octet-stream");
    }
}