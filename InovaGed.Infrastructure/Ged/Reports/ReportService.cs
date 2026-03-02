using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Reports;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Reports;

public sealed class ReportService : IReportService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IDbConnectionFactory db, IAuditWriter audit, ILogger<ReportService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateReportRunWithSignatureSnapshotAsync(
        Guid tenantId,
        Guid? userId,
        ReportRunCreateVM vm,
        CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm.DocumentIds.Count == 0) return Result<Guid>.Fail("DOCS", "Selecione ao menos 1 documento.");

            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var parametersJson = vm.Parameters is null ? null : JsonSerializer.Serialize(vm.Parameters);

            const string insRun = @"
insert into ged.report_run(id, tenant_id, report_type, generated_at, generated_by, parameters, notes, reg_date, reg_status)
values(gen_random_uuid(), @tenant_id, @report_type, now(), @generated_by, @parameters::jsonb, @notes, now(), 'A')
returning id;
";
            var runId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(insRun, new
            {
                tenant_id = tenantId,
                report_type = vm.ReportType,
                generated_by = userId,
                parameters = parametersJson,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            // snapshot: pega última assinatura por documento
            const string insSig = @"
insert into ged.report_run_signature
(tenant_id, report_run_id, document_id, signature_id, signature_status, status_details, validated_at, reg_date, reg_status)
select
  @tenant_id,
  @run_id,
  d.id,
  s.signature_id,
  s.status,
  s.status_details,
  now(),
  now(),
  'A'
from unnest(@doc_ids::uuid[]) as x(id)
join ged.document d on d.tenant_id=@tenant_id and d.id=x.id
left join ged.vw_document_latest_signature s
  on s.tenant_id=d.tenant_id and s.document_id=d.id;
";
            await conn.ExecuteAsync(new CommandDefinition(insSig, new
            {
                tenant_id = tenantId,
                run_id = runId,
                doc_ids = vm.DocumentIds.Distinct().ToArray()
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "REPORT_PRINT", "report_run", runId,
                "Relatório gerado com validação de assinaturas (snapshot)", null, null,
                new { vm.ReportType, docs = vm.DocumentIds.Count }, ct);

            return Result<Guid>.Ok(runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportService.CreateReportRunWithSignatureSnapshotAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("REPORT", "Falha ao gerar registro do relatório.");
        }
    }
}