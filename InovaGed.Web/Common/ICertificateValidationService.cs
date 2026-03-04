using Dapper;
using InovaGed.Application.Common.Database;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public interface ICertificateValidationService
{
    Task<CertLoginResult> ValidateForLoginAsync(Guid tenantId, X509Certificate2 cert, CancellationToken ct);
    Task<SignatureValidationResult> ValidateSignatureAsync(Guid tenantId, byte[] signatureBytes, CancellationToken ct);
}

public sealed record CertLoginResult(bool Success, Guid? UserId, string? UserName, string? Cpf, string? Error);
public sealed record SignatureValidationResult(string Status, string? Details); // VALID / INVALID / UNVERIFIABLE

public sealed class CertificateValidationService : ICertificateValidationService
{
    private readonly IDbConnectionFactory _db;
    public CertificateValidationService(IDbConnectionFactory db) => _db = db;

    public async Task<CertLoginResult> ValidateForLoginAsync(Guid tenantId, X509Certificate2 cert, CancellationToken ct)
    {
        // 1) Vigência
        var now = DateTimeOffset.UtcNow;
        if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime())
            return new(false, null, null, null, "Certificado fora da vigência.");

        // 2) Cadeia + revogação (quando possível)
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(4);
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        var chainOk = chain.Build(cert);

        // Se não conseguir revogar online (sem internet/OCSP/CRL), a cadeia pode falhar por motivo operacional.
        // Para PoC: se falhar por motivos de revogação/URL, marcamos como NÃO VERIFICÁVEL.
        if (!chainOk)
        {
            var msg = string.Join(" | ", chain.ChainStatus.Select(s => $"{s.Status}:{s.StatusInformation}".Trim()));
            var unverifiable = chain.ChainStatus.Any(s =>
                s.Status is X509ChainStatusFlags.RevocationStatusUnknown
                          or X509ChainStatusFlags.OfflineRevocation
                          or X509ChainStatusFlags.RevocationStatusUnknown);

            if (unverifiable)
                return new(false, null, null, null, "Não verificável (revogação/OCSP/CRL indisponível): " + msg);

            return new(false, null, null, null, "Cadeia inválida: " + msg);
        }

        // 3) Extrair CPF do certificado (ICP-Brasil normalmente vem em OID / subjectAlternativeName)
        var cpf = ExtractCpf(cert);
        if (string.IsNullOrWhiteSpace(cpf))
            return new(false, null, null, null, "CPF não identificado no certificado.");

        // 4) Resolver usuário (no teu banco app_user não tem CPF, então:
        // - estratégia PoC: mapear CPF em uma tabela de vínculo (criar) OU usar document_signature como referência
        // Eu recomendo criar tabela ged.user_cpf_map (abaixo no SQL de mock).
        using var conn = await _db.OpenAsync(ct);
        var user = await conn.QueryFirstOrDefaultAsync<(Guid Id, string Name)>(@"
select id, name
from ged.app_user
where tenant_id=@tenantId
  and exists (select 1 from ged.user_cpf_map m where m.tenant_id=@tenantId and m.user_id=app_user.id and m.cpf=@cpf and m.reg_status='A')
limit 1;", new { tenantId, cpf });

        if (user == default)
            return new(false, null, null, cpf, "CPF do certificado não corresponde ao CPF cadastrado do usuário.");

        return new(true, user.Id, user.Name, cpf, null);
    }

    public Task<SignatureValidationResult> ValidateSignatureAsync(Guid tenantId, byte[] signatureBytes, CancellationToken ct)
    {
        // PoC: você já armazena signature_bytes em document_signature.
        // Para validação real ICP-PAdES/CAdES, normalmente se usa biblioteca especializada.
        // Aqui eu deixo o “plug” do validador:
        // - Se tiver assinatura PAdES: iText + BouncyCastle / ou outro validador ICP.
        // - Resultado precisa ser: VALID/INVALID/UNVERIFIABLE :contentReference[oaicite:14]{index=14}
        if (signatureBytes == null || signatureBytes.Length == 0)
            return Task.FromResult(new SignatureValidationResult("UNVERIFIABLE", "Sem bytes de assinatura."));

        return Task.FromResult(new SignatureValidationResult("VALID", "Validação PoC (stub)."));
    }

    private static string? ExtractCpf(X509Certificate2 cert)
    {
        // PoC robusto: tenta achar 11 dígitos no Subject.
        var subject = cert.Subject ?? "";
        var digits = new string(subject.Where(char.IsDigit).ToArray());
        if (digits.Length >= 11)
            return digits.Substring(digits.Length - 11, 11);

        return null;
    }
}