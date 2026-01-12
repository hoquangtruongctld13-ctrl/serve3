namespace SubPhim.Server.Models
{
    public class LocalApiSettings
    {
        public const string SectionName = "LocalApiSettings";

        public string EncryptionKey { get; set; }
        public int Rpm { get; set; } = 50; // Giá trị mặc định
        public int BatchSize { get; set; } = 100; // Giá trị mặc định
    }
}