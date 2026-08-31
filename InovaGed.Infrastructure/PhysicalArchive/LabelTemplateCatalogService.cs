using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive;
using InovaGed.Infrastructure.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelTemplateCatalogService(IDbConnectionFactory dbFactory, ILogger<LabelTemplateCatalogService> logger) : ILabelTemplateCatalogService
{
    private const string Migration = "database/migrations/2026_08_25_admin_labels_legacy_schema_compat.sql";
    private static readonly DatabaseSchemaReader Schema = new();
    private static readonly IReadOnlyList<LabelTemplateOption> MinimumCatalog =
    [
        new("FACTORY_BOX_V1", "Padrão do Sistema - Caixa", "FACTORY", "BOX", "Etiqueta padrão do InovaGED para caixas físicas.", "BoxLabel", "1", true, false, true, null, true),
        new("FACTORY_DOCUMENT_V1", "Padrão do Sistema - Documento/Pasta", "FACTORY", "DOCUMENT", "Etiqueta padrão do InovaGED para documentos e pastas.", "DocumentLabel", "1", true, false, true, null, true),
        new("LOCDESK_CAIXA_V1", "LocDesk - Caixa", "CUSTOM", "BOX", "Modelo personalizado LocDesk para identificação de caixas físicas.", "LocDeskBoxLabel", "1", true, true, false, null, true),
        new("LOCDESK_PASTA_V1", "LocDesk - Pasta", "CUSTOM", "DOCUMENT", "Modelo personalizado LocDesk para identificação de pastas/documentos.", "LocDeskFolderLabel", "1", true, true, false, null, true),
        new("LOCDESK_PASTA_HOL_V1", "LocDesk - Pasta HOL", "CUSTOM", "DOCUMENT", "Modelo LocDesk para pasta/documento do Hospital Ophir Loyola.", "LocDeskFolderHolLabel", "1", true, true, false, null, true)
    ];

    public bool IsTemporaryCatalog { get; private set; }

    public async Task<IReadOnlyList<LabelTemplateOption>> GetTemplatesAsync(Guid tenantId, string subjectType, string? mode, CancellationToken ct)
    {
        await using var db = await dbFactory.OpenAsync(ct);
        var source = await GetSourceAsync(db, ct);
        var normalized = string.IsNullOrWhiteSpace(mode) ? null : mode;
        IReadOnlyList<LabelTemplateOption> databaseTemplates = source is null ? [] : (await db.QueryAsync<LabelTemplateOption>(new CommandDefinition(BuildSelect(source, false) + BuildFilters(source, false) + BuildOrder(source), new { tenantId, subjectType, mode = normalized }, cancellationToken: ct))).AsList();
        var designerTemplates = await GetPublishedDesignerTemplatesAsync(db, tenantId, subjectType, normalized, ct);
        return FilterCatalog(MergeWithMinimumCatalog(databaseTemplates.Concat(designerTemplates)), subjectType, normalized);
    }

    public async Task<LabelTemplateOption?> TryGetTemplateAsync(Guid tenantId, string templateCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateCode)) return null;
        await using var db = await dbFactory.OpenAsync(ct);
        var designer = (await GetPublishedDesignerTemplatesAsync(db, tenantId, null, null, ct)).FirstOrDefault(x => string.Equals(x.Code, templateCode, StringComparison.OrdinalIgnoreCase));
        if (designer is not null) return designer;
        var source = await GetSourceAsync(db, ct);
        if (source is not null)
        {
            var sql = BuildSelect(source, true) + BuildFilters(source, true) + " limit 1";
            var fromDatabase = await db.QuerySingleOrDefaultAsync<LabelTemplateOption>(new CommandDefinition(sql,
                new { tenantId, templateCode }, cancellationToken: ct));
            if (fromDatabase is not null) return fromDatabase;
        }
        return MinimumCatalog.FirstOrDefault(x => string.Equals(x.Code, templateCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<LabelTemplateOption> GetTemplateAsync(Guid tenantId, string templateCode, CancellationToken ct) =>
        await TryGetTemplateAsync(tenantId, templateCode, ct) ?? throw NotFound();

    public async Task<bool> IsCompatibleAsync(Guid tenantId, string templateCode, string subjectType, CancellationToken ct) =>
        (await GetTemplatesAsync(tenantId, subjectType, null, ct)).Any(x => x.Code == templateCode);

    private async Task<CatalogSource?> GetSourceAsync(Npgsql.NpgsqlConnection db, CancellationToken ct)
    {
        foreach (var table in new[] { "label_template", "label_template_catalog" })
        {
            if (!await Schema.TableExistsAsync(db, "ged", table, ct)) continue;
            var columns = await Schema.GetColumnsAsync(db, "ged", table, ct);
            if (columns.Contains("code") || columns.Contains("template_code"))
            {
                IsTemporaryCatalog = table != "label_template";
                return new(table, columns);
            }
        }
        IsTemporaryCatalog = true;
        logger.LogWarning("A migration {Migration} ainda não foi aplicada; usando catálogo mínimo em memória.", Migration);
        return null;
    }

    private static string BuildSelect(CatalogSource source, bool single)
    {
        var c = source.Columns;
        var code = SchemaAwareSqlBuilder.CoalesceText(c, ("template_code", "template_code::text"), ("code", "code::text"));
        var name = SchemaAwareSqlBuilder.CoalesceText(c, ("name", "name::text"), ("title", "title::text"), ("description", "description::text"), ("code", "code::text"));
        var description = SchemaAwareSqlBuilder.CoalesceText(c, ("description", "description::text"), ("name", "name::text"), ("title", "title::text"), ("code", "code::text"));
        return $"select {code} Code,{name} Name,{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "print_mode", "print_mode::text", "'FACTORY'")} Mode," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "subject_type", "subject_type::text", "'BOX'")} SubjectType,{description} Description," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "view_name", "view_name::text", "'BoxLabel'")} ViewName,{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "version", "version::text", "'1'")} Version," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "supports_batch", "coalesce(supports_batch,false)", "false")} SupportsBatch," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "allows_manual_fields", "coalesce(allows_manual_fields,false)", "false")} AllowsManualFields," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "is_system_template", "coalesce(is_system_template,false)", "false")} IsSystemTemplate," +
               $"{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "id", "id", "null::uuid")} Id,{SchemaAwareSqlBuilder.ColumnOrLiteral(c, "is_default", "coalesce(is_default,false)", "false")} IsDefault from ged.{source.Table}";
    }

    private static string BuildFilters(CatalogSource source, bool single)
    {
        var c = source.Columns; var filters = new List<string> { "1=1" };
        if (c.Contains("tenant_id")) filters.Add("(tenant_id=@tenantId or tenant_id is null)");
        if (single) filters.Add(BuildTemplateCodePredicate(source));
        else
        {
            if (c.Contains("subject_type")) filters.Add("subject_type=@subjectType");
            if (c.Contains("print_mode")) filters.Add("(@mode is null or print_mode=@mode)");
        }
        if (c.Contains("is_active")) filters.Add("is_active");
        if (c.Contains("reg_status")) filters.Add("coalesce(reg_status,'A')='A'");
        return " where " + string.Join(" and ", filters);
    }

    private static string BuildTemplateCodePredicate(CatalogSource source)
    {
        var predicates = new List<string>();
        if (source.Columns.Contains("template_code")) predicates.Add("template_code::text = @templateCode");
        if (source.Columns.Contains("code")) predicates.Add("code::text = @templateCode");
        return predicates.Count == 0 ? "1 = 0" : "(" + string.Join(" or ", predicates) + ")";
    }

    private static IReadOnlyList<LabelTemplateOption> MergeWithMinimumCatalog(IEnumerable<LabelTemplateOption> databaseTemplates)
    {
        var result = new Dictionary<string, LabelTemplateOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in MinimumCatalog) result[item.Code] = item;
        foreach (var item in databaseTemplates)
            if (!string.IsNullOrWhiteSpace(item.Code)) result[item.Code] = item;
        return result.Values.ToList();
    }

    private static async Task<IReadOnlyList<LabelTemplateOption>> GetPublishedDesignerTemplatesAsync(Npgsql.NpgsqlConnection db, Guid tenantId, string? subjectType, string? mode, CancellationToken ct)
    {
        if (!await Schema.TableExistsAsync(db, "ged", "label_template_design", ct)) return [];
        const string sql = """
select distinct on (template_code) template_code Code,template_name Name,print_mode Mode,subject_type SubjectType,
 coalesce(description,template_name) Description,coalesce(view_name,'DocumentLabel') ViewName,current_version::text Version,
 false SupportsBatch,true AllowsManualFields,is_system_template IsSystemTemplate,id Id,false IsDefault
from ged.label_template_design
where (tenant_id=@tenantId or tenant_id is null) and status='PUBLISHED' and reg_status='A'
 and (@subjectType is null or subject_type=@subjectType) and (@mode is null or print_mode=@mode)
order by template_code,updated_at desc nulls last,tenant_id nulls last
""";
        return (await db.QueryAsync<LabelTemplateOption>(new CommandDefinition(sql,new{tenantId,subjectType,mode},cancellationToken:ct))).AsList();
    }

    private static IReadOnlyList<LabelTemplateOption> FilterCatalog(IEnumerable<LabelTemplateOption> templates, string subjectType, string? mode) =>
        templates.Where(x => string.Equals(x.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase)
            && (mode is null || string.Equals(x.Mode, mode, StringComparison.OrdinalIgnoreCase))).ToList();

    private static string BuildOrder(CatalogSource source)
    {
        var terms = new List<string>();
        if (source.Columns.Contains("is_default")) terms.Add("is_default desc");
        if (source.Columns.Contains("display_order")) terms.Add("display_order");
        terms.Add(source.Columns.Contains("name") ? "name" : source.Columns.Contains("title") ? "title" : source.Columns.Contains("template_code") ? "template_code" : "code");
        return " order by " + string.Join(",", terms);
    }

    private static KeyNotFoundException NotFound() => new("Modelo de etiqueta não encontrado.");
    private sealed record CatalogSource(string Table, IReadOnlySet<string> Columns);
}
