using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelPrintRegistrar(IDbConnectionFactory dbFactory) : ILabelPrintRegistrar
{
    public async Task RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SnapshotJson);
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
            throw new InvalidOperationException("Tenant e usuário autenticado são obrigatórios para registrar a impressão.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.SnapshotJson))).ToLowerInvariant();
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_history
 (id, tenant_id, label_subject_type, label_subject_id, template_code, snapshot_json,
  snapshot_sha256, printed_by, ip_address, user_agent, reprint_reason)
values
 (gen_random_uuid(), @TenantId, @SubjectType, @SubjectId, @TemplateCode, cast(@SnapshotJson as jsonb),
  @Hash, @UserId, cast(@IpAddress as inet), @UserAgent, nullif(@ReprintReason, ''));
""", new { request.TenantId, request.SubjectType, request.SubjectId, request.TemplateCode, request.SnapshotJson, Hash = hash, request.UserId, request.IpAddress, request.UserAgent, request.ReprintReason }, tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
    }
}
