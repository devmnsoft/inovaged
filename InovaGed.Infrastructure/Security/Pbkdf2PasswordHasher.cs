using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace InovaGed.Infrastructure.Security;

public static class Pbkdf2PasswordHasher
{
    // Formato: PBKDF2$<iters>$<saltB64>$<hashB64>
    public static string Hash(string password, int iterations = 100_000)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password inválida.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(16);

        var hash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: iterations,
            numBytesRequested: 32);

        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
