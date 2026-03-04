using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace InovaGed.Web.Common;

public sealed class CertificateValidationStub : ICertificateValidationService
{
    public Task<CertLoginResult> ValidateForLoginAsync(Guid tenantId, X509Certificate2 cert, CancellationToken ct)
    {
        // PoC: aceita qualquer certificado e tenta extrair CPF do subject.
        // Se não achar CPF, deixa como "não autenticado" (ou você pode aceitar mesmo assim).
        var cpf = ExtractCpf(cert);

        // Para não travar o login PoC, você pode "forçar" sucesso:
        // return Task.FromResult(new CertLoginResult(true, null, "Usuário PoC", cpf, null));

        if (string.IsNullOrWhiteSpace(cpf))
            return Task.FromResult(new CertLoginResult(false, null, null, null, "CPF não identificado no certificado (PoC stub)."));

        return Task.FromResult(new CertLoginResult(true, null, "Usuário PoC", cpf, null));
    }

    public Task<SignatureValidationResult> ValidateSignatureAsync(Guid tenantId, byte[] signatureBytes, CancellationToken ct)
    {
        if (signatureBytes == null || signatureBytes.Length == 0)
            return Task.FromResult(new SignatureValidationResult("UNVERIFIABLE", "Sem bytes de assinatura."));

        // PoC: só gera hash para rastreabilidade
        var hash = Convert.ToHexString(SHA256.HashData(signatureBytes)).ToLowerInvariant();
        return Task.FromResult(new SignatureValidationResult("VALID", $"PoC stub (sem OCSP/CRL). sha256={hash[..12]}..."));
    }

    private static string? ExtractCpf(X509Certificate2 cert)
    {
        var subject = cert.Subject ?? "";
        var digits = new string(subject.Where(char.IsDigit).ToArray());
        if (digits.Length >= 11)
            return digits.Substring(digits.Length - 11, 11);
        return null;
    }
}