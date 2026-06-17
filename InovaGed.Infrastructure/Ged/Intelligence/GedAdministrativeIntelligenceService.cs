using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Intelligence;

namespace InovaGed.Infrastructure.Ged.Intelligence;

public sealed class GedAdministrativeIntelligenceService : IGedAdministrativeIntelligenceService
{
    private readonly IDbConnectionFactory _db;

    public GedAdministrativeIntelligenceService(IDbConnectionFactory db) => _db = db;

    public async Task<GedIntelligenceVm> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var vm = new GedIntelligenceVm();
        var warnings = new List<string>();
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var hasSearch = await ExistsAsync(conn, "ged.document_search", ct);
            var hasIndex = await ExistsAsync(conn, "ged.document_search_index", ct);
            var hasUpload = await ExistsAsync(conn, "ged.upload_batch", ct);
            var hasSearchLog = await ExistsAsync(conn, "ged.search_query_log", ct);

            var total = await ScalarAsync<int>(conn, "select count(*)::int from ged.document where tenant_id=@tenantId and coalesce(reg_status,'A')='A'", tenantId, ct);
            var today = await ScalarAsync<int>(conn, "select count(*)::int from ged.document where tenant_id=@tenantId and coalesce(reg_status,'A')='A' and created_at::date=current_date", tenantId, ct);
            var month = await ScalarAsync<int>(conn, "select count(*)::int from ged.document where tenant_id=@tenantId and coalesce(reg_status,'A')='A' and date_trunc('month', created_at)=date_trunc('month', now())", tenantId, ct);
            var withOcr = hasSearch ? await ScalarAsync<int>(conn, "select count(distinct document_id)::int from ged.document_search where tenant_id=@tenantId and nullif(ocr_text,'') is not null", tenantId, ct) : 0;
            var withoutOcr = Math.Max(0, total - withOcr);
            var ocrErrors = await ScalarAsync<int>(conn, "select count(*)::int from ged.document_version where tenant_id=@tenantId and upper(coalesce(ocr_status,'')) in ('ERROR','FAILED')", tenantId, ct);
            var incomplete = await ScalarAsync<int>(conn, "select count(*)::int from ged.document_version where tenant_id=@tenantId and (is_document_incomplete=true or upper(coalesce(partial_status,''))='INCOMPLETE')", tenantId, ct);
            var ready = await ScalarAsync<int>(conn, "select count(*)::int from ged.document_version where tenant_id=@tenantId and upper(coalesce(partial_status,''))='COMPLETE'", tenantId, ct);
            var confidential = await ScalarAsync<int>(conn, "select count(*)::int from ged.document where tenant_id=@tenantId and coalesce(is_confidential,false)=true and coalesce(reg_status,'A')='A'", tenantId, ct);
            var failedUploads = hasUpload ? await ScalarAsync<int>(conn, "select count(*)::int from ged.upload_batch where tenant_id=@tenantId and created_at >= now()-interval '24 hours' and upper(coalesce(status,'')) in ('FAILED','ERROR')", tenantId, ct) : 0;
            var missingIndex = hasIndex ? await ScalarAsync<int>(conn, "select count(*)::int from ged.document d left join ged.document_search_index i on i.tenant_id=d.tenant_id and i.document_id=d.id where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and i.document_id is null", tenantId, ct) : total;

            vm.Kpis = new[]
            {
                Kpi("Total de documentos ativos", total), Kpi("Enviados hoje", today), Kpi("Enviados no mês", month), Kpi("Sem OCR", withoutOcr, "warning"),
                Kpi("OCR com erro", ocrErrors, "danger"), Kpi("Incompletos", incomplete, "warning"), Kpi("Prontos para consolidar", ready, "success"), Kpi("Confidenciais", confidential, "dark"),
                Kpi("Uploads com falha 24h", failedUploads, "danger"), Kpi("Tempo médio de upload", "módulo upload"), Kpi("Tempo médio de OCR", "módulo OCR"),
                Kpi("Pastas com mais documentos", "Top 10"), Kpi("Usuários que mais enviaram", "Top 10"), Kpi("Tipos mais usados", "Top 10"), Kpi("Qualidade documental", Percent(total - incomplete - withoutOcr, total))
            };

            vm.HealthIndexes = new[]
            {
                Health("Índice de Digitalização", PercentValue(withOcr, total), "Priorize a fila OCR dos documentos digitalizáveis sem texto."),
                Health("Índice de Organização", PercentValue(total - missingIndex - incomplete, total), "Classifique documentos sem tipo, classificação ou pasta válida."),
                Health("Índice de Pendência", 100 - PercentValue(incomplete + ocrErrors + missingIndex, total), "Trate incompletos, erros de OCR e documentos sem índice."),
                Health("Índice de Risco LGPD", 100 - PercentValue(confidential + missingIndex, total), "Revise documentos confidenciais e sem classificação."),
                Health("Índice de Produtividade", hasUpload ? 100 - PercentValue(failedUploads, Math.Max(failedUploads, today)) : 0, "Acompanhe uploads com falha por período.")
            };

            vm.TopFolders = await TopAsync(conn, "select coalesce(f.name,'Sem pasta') label, '' detail, count(*)::int count from ged.document d left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' group by 1 order by 3 desc limit 10", tenantId, ct);
            vm.TopUsers = await TopAsync(conn, "select coalesce(u.name, d.created_by::text, 'Não informado') label, '' detail, count(*)::int count from ged.document d left join identity.users u on u.tenant_id=d.tenant_id and u.id=d.created_by where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' group by 1 order by 3 desc limit 10", tenantId, ct);
            vm.DocumentsWithoutOcr = hasSearch ? await TopAsync(conn, "select coalesce(d.title, v.file_name, d.id::text) label, coalesce(f.name,'Sem pasta') detail, 1 count from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id and nullif(ds.ocr_text,'') is not null where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and ds.document_id is null order by d.created_at desc limit 10", tenantId, ct) : [];
            vm.SmartSearchWithoutResult = hasSearchLog ? await TopAsync(conn, "select query_text label, created_at::text detail, results_count count from ged.search_query_log where tenant_id=@tenantId and results_count=0 order by created_at desc limit 10", tenantId, ct) : [];
            vm.Insights = new[] { $"Existem {withoutOcr} documentos sem OCR.", $"Há {incomplete} documentos incompletos.", $"A busca inteligente possui {missingIndex} documentos sem índice.", $"Há {failedUploads} uploads com falha nas últimas 24h." };
            if (!hasSearch) warnings.Add("Módulo OCR/document_search não configurado.");
            if (!hasUpload) warnings.Add("Módulo de uploads não configurado.");
            vm.Warnings = warnings;
        }
        catch (Exception ex)
        {
            vm.Warnings = [$"Indicadores parcialmente indisponíveis: {ex.Message}"];
        }
        return vm;
    }

    private static GedKpiVm Kpi(string label, int value, string css = "primary") => new() { Label = label, Value = value.ToString("N0"), Css = css };
    private static GedKpiVm Kpi(string label, string value, string css = "secondary") => new() { Label = label, Value = value, Css = css };
    private static string Percent(int value, int total) => $"{PercentValue(value, total):N1}%";
    private static GedHealthIndexVm Health(string name, decimal percent, string suggestion) => new() { Name = name, Percent = percent, Status = percent >= 80 ? "Bom" : percent >= 50 ? "Atenção" : "Crítico", Suggestion = suggestion };
    private static decimal PercentValue(int value, int total) => total <= 0 ? 0 : Math.Clamp(Math.Round(100m * value / total, 1), 0, 100);
    private static Task<T> ScalarAsync<T>(System.Data.IDbConnection conn, string sql, Guid tenantId, CancellationToken ct) => conn.ExecuteScalarAsync<T>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
    private static async Task<bool> ExistsAsync(System.Data.IDbConnection conn, string name, CancellationToken ct) => await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass(@name)::text", new { name }, cancellationToken: ct)) is not null;
    private static async Task<IReadOnlyList<GedRankVm>> TopAsync(System.Data.IDbConnection conn, string sql, Guid tenantId, CancellationToken ct) => (await conn.QueryAsync<GedRankVm>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).ToList();
}
