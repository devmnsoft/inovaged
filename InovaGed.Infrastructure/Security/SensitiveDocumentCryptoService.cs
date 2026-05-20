using System.Security.Cryptography;
using System.Text;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Security;

public sealed class SensitiveDocumentCryptoService : ISensitiveDocumentCryptoService
{
    public byte[] Encrypt(byte[] clearBytes, string keyMaterial)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(clearBytes, 0, clearBytes.Length);
        return aes.IV.Concat(cipher).ToArray();
    }

    public byte[] Decrypt(byte[] cipherBytes, string keyMaterial)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        var iv = cipherBytes.Take(16).ToArray();
        var data = cipherBytes.Skip(16).ToArray();
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }
}
