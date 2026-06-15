using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Search;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class GedSmartSearchDiagnosticsService : IGedSmartSearchDiagnosticsService
{
    private readonly IDbConnectionFactory _db;
    private readonly IGedProcessingJobRepository _jobs;
    private readonly ILogger<GedSmartSearchDiagnosticsService> _logger;

    public GedSmartSearchDiagnosticsService(IDbConnectionFactory db, IGedProcessingJobRepository jobs, ILogger<GedSmartSearchDiagnosticsService> logger)
    { _db = db; _jobs = jobs; _logger = logger; }

    public async Task<GedSmartSearchDiagnosticsVm> GetAsync(Guid tenantId, CancellationToken ct)
    {
        await using var cn = await _db.OpenAsync(ct);
        var vm = await cn.QuerySingleAsync<GedSmartSearchDiagnosticsVm>(new CommandDefinition("""
select
  (select count(*)::int from ged.document d where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A') as "ActiveDocuments",
  (select count(distinct si.document_id)::int from ged.document_search_index si where si.tenant_id=@tenantId and coalesce(si.reg_status,'A')='A') as "IndexedDocuments",
  (select count(*)::int from ged.document d where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and not exists (select 1 from ged.document_search_index si where si.tenant_id=d.tenant_id and si.document_id=d.id and coalesce(si.reg_status,'A')='A')) as "MissingIndexDocuments",
  (select count(*)::int from ged.document_search_index si where si.tenant_id=@tenantId and nullif(btrim(coalesce(si.search_text,'')),'') is null) as "EmptySearchTextDocuments",
  (select count(*)::int from ged.document_search_index si where si.tenant_id=@tenantId and si.search_vector is null) as "NullSearchVectorDocuments",
  (select max(coalesce(si.last_indexed_at, si.updated_at)) from ged.document_search_index si where si.tenant_id=@tenantId) as "LastIndexing",
  (select count(distinct ds.document_id)::int from ged.document_search ds where ds.tenant_id=@tenantId and nullif(btrim(coalesce(ds.ocr_text,'')),'') is not null) as "OcrAvailable",
  exists(select 1 from pg_extension where extname='unaccent') as "HasUnaccent",
  exists(select 1 from pg_extension where extname='pg_trgm') as "HasPgTrgm",
  to_regclass('ged.processing_job') is not null as "HasProcessingJob"
""", new { tenantId }, cancellationToken: ct));

        vm.Tenants = (await cn.QueryAsync<GedSmartSearchTenantCountVm>(new CommandDefinition("""
select d.tenant_id as "TenantId", count(distinct d.id)::int as "ActiveDocuments", count(distinct si.document_id)::int as "IndexedDocuments"
from ged.document d left join ged.document_search_index si on si.tenant_id=d.tenant_id and si.document_id=d.id and coalesce(si.reg_status,'A')='A'
where coalesce(d.reg_status,'A')='A' group by d.tenant_id order by 1
""", cancellationToken: ct))).AsList();
        if (vm.HasProcessingJob)
            vm.SmartIndexJobs = (await cn.QueryAsync<GedSmartSearchJobStatusVm>(new CommandDefinition("select coalesce(status,'UNKNOWN') as \"Status\", count(*)::int as \"Total\" from ged.processing_job where tenant_id=@tenantId and job_type='SMART_INDEX' group by 1 order by 1", new { tenantId }, cancellationToken: ct))).AsList();
        else vm.SchemaWarning = "Tabela ged.processing_job ausente. Execute database/apply_all_required_migrations.sql.";
        vm.TopMissingDocuments = (await cn.QueryAsync<GedSmartSearchMissingDocumentVm>(new CommandDefinition("""
select d.id as "DocumentId", coalesce(d.title,'Documento sem título') as "Title", v.file_name as "FileName", f.name as "FolderName", d.created_at as "CreatedAt"
from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and not exists (select 1 from ged.document_search_index si where si.tenant_id=d.tenant_id and si.document_id=d.id and coalesce(si.reg_status,'A')='A')
order by d.created_at desc nulls last limit 20
""", new { tenantId }, cancellationToken: ct))).AsList();
        return vm;
    }

    public async Task<int> EnqueueReindexDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct) { await _jobs.EnqueueAsync(tenantId, documentId, null, null, null, "SMART_INDEX", 8, ct); return 1; }
    public Task<int> EnqueueReindexAllAsync(Guid tenantId, CancellationToken ct) => EnqueueBySqlAsync(tenantId, false, ct);
    public Task<int> EnqueueReindexMissingAsync(Guid tenantId, CancellationToken ct) => EnqueueBySqlAsync(tenantId, true, ct);

    public async Task<int> RebuildVectorsAsync(Guid tenantId, CancellationToken ct)
    {
        await using var cn = await _db.OpenAsync(ct);
        var hasUnaccent = await HasExtensionAsync(cn, "unaccent", ct);
        var expr = hasUnaccent ? "unaccent(coalesce(search_text,''))" : "coalesce(search_text,'')";
        return await cn.ExecuteAsync(new CommandDefinition($"update ged.document_search_index set search_vector=to_tsvector('portuguese', {expr}), updated_at=now() where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct, commandTimeout: 120));
    }

    private async Task<int> EnqueueBySqlAsync(Guid tenantId, bool missingOnly, CancellationToken ct)
    {
        await using var cn = await _db.OpenAsync(ct);
        if (await cn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass('ged.processing_job')::text", cancellationToken: ct)) is null) throw new InvalidOperationException("Tabela ged.processing_job ausente. Execute database/apply_all_required_migrations.sql.");
        var sql = """
insert into ged.processing_job (tenant_id, document_id, document_version_id, job_type, status, priority)
select d.tenant_id, d.id, d.current_version_id, 'SMART_INDEX', 'PENDING', 8
from ged.document d
where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A'
""" + (missingOnly ? " and not exists (select 1 from ged.document_search_index si where si.tenant_id=d.tenant_id and si.document_id=d.id and coalesce(si.reg_status,'A')='A')" : "") + ";";
        return await cn.ExecuteAsync(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
    }

    private static Task<bool> HasExtensionAsync(IDbConnection cn, string extensionName, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from pg_extension where extname=@extensionName)", new { extensionName }, cancellationToken: ct));
}
