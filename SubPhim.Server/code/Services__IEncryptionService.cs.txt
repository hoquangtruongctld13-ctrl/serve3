namespace SubPhim.Server.Services
{
    public interface IEncryptionService
    {
        (string encryptedText, string iv) Encrypt(string plainText);
        string Decrypt(string encryptedText, string iv);
    }
}