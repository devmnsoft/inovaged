using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class SecureDocumentLinkService : ISecureDocumentLinkService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureDocumentLinkService> _logger;

    public SecureDocumentLinkService(IDbConnectionFactory db, IAuditWriter audit, IHttpContextAccessor http, IConfiguration configuration, ILogger<SecureDocumentLinkService> logger)
    { _db = db; _audit = audit; _http = http; _configuration = configuration; _logger = logger; }

    public async Task<SecureDocumentLinkResult> CreateAsync(Guid tenantId, Guid userId, CreateSecureDocumentLinkRequest request, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new InvalidOperationException("Tenant obrigatório.");
        if (request.DocumentId == Guid.Empty) throw new InvalidOperationException("Documento obrigatório.");

        DateTimeOffset? expiresAtUtc = null;
        if (request.IsPermanent)
        {
            expiresAtUtc = null;
        }
        else
        {
            if (request.ExpiresAtUtc is null) throw new InvalidOperationException("Informe a data/hora de expiração para link temporário.");
            expiresAtUtc = PostgresDateTimeHelper.ToUtc(request.ExpiresAtUtc.Value);
            if (expiresAtUtc <= PostgresDateTimeHelper.UtcNow()) throw new InvalidOperationException("A expiração do link deve ser uma data/hora futura.");
        }

        await using var conn = await _db.OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.document where tenant_id=@tenantId and id=@documentId and coalesce(reg_status,'A')='A' and status <> 'DELETED'::ged.document_status_enum)", new { tenantId, request.DocumentId }, cancellationToken: ct));
        if (!exists) throw new InvalidOperationException("Documento inexistente, inativo ou fora do tenant.");
        if (request.VersionId.HasValue)
        {
            var versionOk = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.document_version where tenant_id=@tenantId and id=@versionId and document_id=@documentId and nullif(storage_path,'') is not null)", new { tenantId, request.DocumentId, versionId = request.VersionId }, cancellationToken: ct));
            if (!versionOk) throw new InvalidOperationException("Versão não pertence ao documento informado ou ainda não possui arquivo digital para compartilhamento.");
        }
        else
        {
            request.VersionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("select id from ged.document_version where tenant_id=@tenantId and document_id=@documentId and nullif(storage_path,'') is not null order by coalesce(version_no,0) desc, created_at desc limit 1", new { tenantId, request.DocumentId }, cancellationToken: ct));
        }

        if (!request.VersionId.HasValue) throw new InvalidOperationException("Este documento ainda não possui arquivo digital para compartilhamento.");

        var token = CreateToken();
        var hash = Sha256Token(token);
        var publicUrl = BuildPublicUrl(token);
        var maxAccess = request.MaxAccessCount.GetValueOrDefault() <= 0 ? null : request.MaxAccessCount;
        var linkId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.secure_document_link(tenant_id, loan_request_id, document_id, version_id, token_hash, public_url, title, description, recipient_name, recipient_contact, is_permanent, expires_at, max_access_count, allow_preview, allow_download, allow_smart_search, created_by)
values(@tenantId,@loanRequestId,@documentId,@versionId,@hash,@publicUrl,@title,@description,@recipientName,@recipientContact,@isPermanent,@expiresAt,@maxAccess,@allowPreview,@allowDownload,@allowSmartSearch,@userId) returning id
""", new { tenantId, loanRequestId = request.LoanRequestId, request.DocumentId, request.VersionId, hash, publicUrl, request.Title, request.Description, request.RecipientName, request.RecipientContact, request.IsPermanent, expiresAt = expiresAtUtc, maxAccess, request.AllowPreview, request.AllowDownload, request.AllowSmartSearch, userId }, cancellationToken: ct));

        if (request.LoanRequestId.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.loan_request set secure_link_id=@linkId, digital_delivery_enabled=true, status='DIGITAL_LINK_SENT', last_message_at=now() where tenant_id=@tenantId and id=@loanId;
insert into ged.loan_request_message(tenant_id, loan_request_id, sender_user_id, sender_name, sender_role, message, message_type, is_internal)
values(@tenantId,@loanId,@userId,'Gestor','ADMIN',coalesce(nullif(@msg,''),'O documento digital foi disponibilizado por link seguro.'),'DIGITAL_LINK',false);
""", new { tenantId, loanId = request.LoanRequestId, linkId, userId, msg = request.MessageToRequester }, cancellationToken: ct));
        }

        await _audit.WriteAsync(tenantId, userId, request.LoanRequestId.HasValue ? "LOAN_REQUEST_DIGITAL_LINK_CREATED" : "GED_DOCUMENT_SECURE_LINK_CREATED", "secure_document_link", linkId, "Link seguro criado sem registrar token puro.", null, null, new { request.DocumentId, request.VersionId, request.LoanRequestId }, ct);
        return new SecureDocumentLinkResult { LinkId = linkId, PublicUrl = publicUrl, ExpiresAtUtc = expiresAtUtc, IsPermanent = request.IsPermanent, MaxAccessCount = maxAccess };
    }

    public async Task<Result> RevokeAsync(Guid tenantId, Guid linkId, Guid userId, string? reason, CancellationToken ct)
    { await using var conn = await _db.OpenAsync(ct); var n = await conn.ExecuteAsync(new CommandDefinition("update ged.secure_document_link set revoked_at=now(), revoked_by=@userId, revoke_reason=@reason where tenant_id=@tenantId and id=@linkId and revoked_at is null", new { tenantId, linkId, userId, reason }, cancellationToken: ct)); return n > 0 ? Result.Ok() : Result.Fail("NOT_FOUND", "Link não encontrado ou já revogado."); }

    public async Task<SecureDocumentLinkValidationResult> ValidateTokenAsync(string token, string? ip, string? userAgent, bool countAccess, CancellationToken ct)
    {
        var hash = Sha256Token(token ?? string.Empty); await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SecureDocumentLinkValidationResult>(new CommandDefinition("select id as LinkId, tenant_id as TenantId, loan_request_id as LoanRequestId, document_id as DocumentId, version_id as VersionId, title as Title, description as Description, expires_at as ExpiresAtUtc, is_permanent as IsPermanent, max_access_count as MaxAccessCount, access_count as AccessCount, allow_preview as AllowPreview, allow_download as AllowDownload, allow_smart_search as AllowSmartSearch, revoked_at as RevokedAt from ged.secure_document_link where token_hash=@hash and reg_status='A'", new { hash }, cancellationToken: ct));
        if (row is null) return new() { IsValid = false, DeniedReason = "INVALID_TOKEN" };
        string? denied = row.RevokedAt is not null ? "REVOKED" : (!row.IsPermanent && row.ExpiresAtUtc is null ? "INVALID_EXPIRATION" : (!row.IsPermanent && row.ExpiresAtUtc <= PostgresDateTimeHelper.UtcNow() ? "EXPIRED" : (row.MaxAccessCount.HasValue && row.AccessCount >= row.MaxAccessCount ? "ACCESS_LIMIT" : null)));
        if (denied is null && countAccess) await conn.ExecuteAsync(new CommandDefinition("update ged.secure_document_link set access_count=access_count+1, last_access_at=now() where id=@id", new { id = row.LinkId }, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into ged.secure_document_link_access(tenant_id, secure_link_id, ip_address, user_agent, success, reason) values(@tenantId,@id,@ip,@ua,@success,@reason)", new { tenantId = row.TenantId, id = row.LinkId, ip, ua = userAgent, success = denied is null, reason = denied ?? "OK" }, cancellationToken: ct));
        row.IsValid = denied is null; row.DeniedReason = denied; if (row.IsValid && countAccess) row.AccessCount++;
        return row;
    }

    public Task<IReadOnlyList<SecureDocumentLinkRow>> ListByLoanAsync(Guid tenantId, Guid loanRequestId, CancellationToken ct) => ListAsync("loan_request_id=@id", tenantId, loanRequestId, ct);
    public Task<IReadOnlyList<SecureDocumentLinkRow>> ListByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct) => ListAsync("document_id=@id", tenantId, documentId, ct);
    private async Task<IReadOnlyList<SecureDocumentLinkRow>> ListAsync(string predicate, Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<SecureDocumentLinkRow>(new CommandDefinition($"""
select
    id as "Id",
    tenant_id as "TenantId",
    loan_request_id as "LoanRequestId",
    document_id as "DocumentId",
    version_id as "VersionId",
    public_url as "PublicUrl",
    title as "Title",
    description as "Description",
    recipient_name as "RecipientName",
    recipient_contact as "RecipientContact",
    is_permanent as "IsPermanent",
    expires_at as "ExpiresAtUtc",
    max_access_count as "MaxAccessCount",
    access_count as "AccessCount",
    allow_preview as "AllowPreview",
    allow_download as "AllowDownload",
    allow_smart_search as "AllowSmartSearch",
    created_by as "CreatedBy",
    created_at as "CreatedAt",
    revoked_at as "RevokedAt",
    revoked_by as "RevokedBy",
    revoke_reason as "RevokeReason"
from ged.secure_document_link
where tenant_id=@tenantId and {predicate}
order by created_at desc
""", new { tenantId, id }, cancellationToken: ct))).AsList();
    }
    private string BuildPublicUrl(string token) { var baseUrl = _configuration["SecureDocumentLinks:PublicBaseUrl"]?.TrimEnd('/'); if (string.IsNullOrWhiteSpace(baseUrl)) { var r = _http.HttpContext?.Request; baseUrl = r is null ? string.Empty : $"{r.Scheme}://{r.Host}"; } return $"{baseUrl}/SharedDocument/{token}"; }
    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string Sha256Token(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
