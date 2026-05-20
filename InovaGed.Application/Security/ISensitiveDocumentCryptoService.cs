namespace InovaGed.Application.Security;

public interface ISensitiveDocumentCryptoService
{
    byte[] Encrypt(byte[] clearBytes, string keyMaterial);
    byte[] Decrypt(byte[] cipherBytes, string keyMaterial);
}
