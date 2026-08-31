using Dapper;
using InovaGed.Application.Administration;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Administration;

public sealed class ConsistencyAuditService(IDbConnectionFactory db) : IConsistencyAuditService
{
    public async Task<ConsistencyAuditResult> CheckAsync(Guid tenantId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var issues = new List<ConsistencyIssue>();
        issues.Add(new("DOCUMENT_NO_TENANT", "Documento sem tenant", "A contagem global é deliberadamente bloqueada para preservar o isolamento entre tenants; valide pelo Database Doctor.", 0, "/DatabaseReadiness", false));
        await AddCheck("DOCUMENT_NO_FOLDER", "Documento sem pasta", "Documento ativo sem pasta operacional.", "document", "tenant_id=@tenantId and folder_id is null", "/Ged", true, "folder_id");
        await AddCheck("DOCUMENT_NO_CLASSIFICATION", "Documento sem classificação", "Documento do tenant ainda não classificado.", "document", "tenant_id=@tenantId and classification_id is null", "/Ged", true, "classification_id");
        await AddCheck("LABEL_TEMPLATE_BROKEN", "Etiqueta com template inexistente", "Histórico de impressão aponta para template ausente.", "label_print_history", "h.tenant_id=@tenantId and not exists (select 1 from ged.label_template t where t.tenant_id=h.tenant_id and t.id=h.template_id)", "/Labels/History", true, "template_id", alias: "h", requiredTable: "label_template");
        await AddCheck("BOX_NO_LOCATION", "Caixa sem localização", "Caixa do tenant sem localização física definida.", "box", "tenant_id=@tenantId and location_id is null", "/Physical/Boxes", true, "location_id");
        await AddCheck("MEASUREMENT_NO_ITEMS", "Período de medição sem itens", "Período contratual sem itens vinculados.", "contract_fiscalization_period", "p.tenant_id=@tenantId and not exists (select 1 from ged.contract_fiscalization_item i where i.tenant_id=p.tenant_id and i.fiscalization_period_id=p.id)", "/ContractMeasurement", true, alias: "p", requiredTable: "contract_fiscalization_item");
        return new ConsistencyAuditResult(issues, DateTimeOffset.UtcNow);

        async Task AddCheck(string code, string title, string description, string table, string predicate, string url, bool tenantScoped, string? requiredColumn = null, string? alias = null, string? requiredTable = null)
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@name) is not null", new { name = $"ged.{table}" }, cancellationToken: ct));
            if (requiredTable is not null)
                exists &= await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@name) is not null", new { name = $"ged.{requiredTable}" }, cancellationToken: ct));
            if (exists && requiredColumn is not null)
                exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.columns where table_schema='ged' and table_name=@table and column_name=@column)", new { table, column = requiredColumn }, cancellationToken: ct));
            if (!exists) { issues.Add(new(code, title, description, 0, url, false)); return; }
            var from = $"ged.{table}" + (alias is null ? string.Empty : $" {alias}");
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*)::int from {from} where {predicate}", tenantScoped ? new { tenantId } : null, cancellationToken: ct));
            issues.Add(new(code, title, description, count, url, true));
        }
    }
}
