namespace SubPhim.Server.Services
{
    public class InsufficientQuotaException : Exception
    {
        public InsufficientQuotaException(string message) : base(message) { }
    }
}