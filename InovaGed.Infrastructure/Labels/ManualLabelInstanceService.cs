using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class ManualLabelInstanceService(IDbConnectionFactory dbFactory, ILabelCanvasFieldCatalogService catalog) : IManualLabelInstanceService
{
    public IReadOnlyList<LabelCanvasFieldDto> EditableFields(LabelCanvasDesignDto design)
    {
        var allFields = catalog.GetFields(design.SubjectType);
        var editableKeys = allFields.Where(x => x.IsEditableInManualMode).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = Document(design).Elements
            .Where(x => x.Binding?.Field is { Length: > 0 })
            .Select(x => x.Binding!.Field!)
            .Where(editableKeys.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allFields.Where(x => used.Contains(x.Key)).ToArray();
    }

    public LabelCanvasValidationResult Validate(LabelCanvasDesignDto design, IReadOnlyDictionary<string, string?> values)
    {
        var result = new LabelCanvasValidationResult();
        var elements = Document(design).Elements.Where(x => x.Binding?.Field is { Length: > 0 }).ToArray();
        var allowed = EditableFields(design).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in values.Keys.Where(x => !allowed.Contains(x)))
            result.Issues.Add(new("MANUAL_UNKNOWN_FIELD", "ERROR", $"O campo '{key}' não pertence ao modelo."));

        foreach (var element in elements.Where(x => allowed.Contains(x.Binding!.Field!)))
        {
            var value = values.GetValueOrDefault(element.Binding!.Field!);
            if (element.Validation.Required && string.IsNullOrWhiteSpace(value))
                result.Issues.Add(new("MANUAL_REQUIRED", "ERROR", $"Preencha {element.Name}.", element.Id));
            if (element.Validation.MaxCharacters is int max && value?.Length > max)
                result.Issues.Add(new("MANUAL_MAX_LENGTH", "ERROR", $"{element.Name} aceita no máximo {max} caracteres.", element.Id));
        }

        return result;
    }

    public Task<ManualLabelInstance> SaveDraftAsync(
        Guid tenantId,
        Guid userId,
        LabelCanvasDesignDto design,
        IReadOnlyDictionary<string, string?> values,
        Guid? brandingProfileId,
        CancellationToken cancellationToken = default)
        => CreateDraftAsync(tenantId, userId, null, design, values, brandingProfileId, cancellationToken);

    public async Task<ManualLabelInstance> CreateDraftAsync(
        Guid tenantId,
        Guid userId,
        string? name,
        LabelCanvasDesignDto design,
        IReadOnlyDictionary<string, string?> values,
        Guid? brandingProfileId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Tenant e usuário são obrigatórios.");

        var validation = Validate(design, values);
        if (validation.HasErrors)
            throw new ArgumentException(string.Join(" ", validation.Issues.Select(x => x.Message)));

        var id = Guid.NewGuid();
        var resolvedName = ResolveName(name, values);
        var controlNumber = ResolveControlNumber(values);

        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
insert into ged.label_manual_instance(id, tenant_id, name, template_key, template_version, values_json, branding_profile_id, status, created_by, created_at, updated_by, updated_at, reg_status)
values(@id, @tenantId, @resolvedName, @key, @version, cast(@json as jsonb), @branding, 'DRAFT', @userId, now(), @userId, now(), 'ACTIVE')
""";

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            tenantId,
            resolvedName,
            key = design.TemplateKey,
            version = design.CurrentVersion,
            json = JsonSerializer.Serialize(values),
            branding = brandingProfileId,
            userId
        }, cancellationToken: cancellationToken));

        var now = DateTime.UtcNow;
        return new(id, tenantId, design.TemplateKey, design.CurrentVersion, values, brandingProfileId, "DRAFT",
            userId, now, userId, now, null, null, null, resolvedName, controlNumber);
    }

    public async Task<ManualLabelInstance> UpdateDraftAsync(
        Guid tenantId,
        Guid userId,
        Guid id,
        string? name,
        LabelCanvasDesignDto design,
        IReadOnlyDictionary<string, string?> values,
        Guid? brandingProfileId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || id == Guid.Empty)
            throw new ArgumentException("Tenant, usuário e identificador da etiqueta são obrigatórios.");

        var existing = await GetAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");

        if (existing.Status == "ARCHIVED")
            throw new InvalidOperationException("Etiquetas arquivadas não podem ser alteradas.");

        var validation = Validate(design, values);
        if (validation.HasErrors)
            throw new ArgumentException(string.Join(" ", validation.Issues.Select(x => x.Message)));

        var resolvedName = ResolveName(name ?? existing.Name, values);
        var controlNumber = ResolveControlNumber(values);

        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
update ged.label_manual_instance
set name = @resolvedName,
    template_key = @key,
    template_version = @version,
    values_json = cast(@json as jsonb),
    branding_profile_id = @branding,
    updated_by = @userId,
    updated_at = now()
where id = @id and tenant_id = @tenantId and reg_status = 'ACTIVE'
""";

        await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            tenantId,
            resolvedName,
            key = design.TemplateKey,
            version = design.CurrentVersion,
            json = JsonSerializer.Serialize(values),
            branding = brandingProfileId,
            userId
        }, cancellationToken: cancellationToken));

        var updated = await GetAsync(tenantId, id, cancellationToken);
        return updated!;
    }

    public async Task<IReadOnlyList<ManualLabelInstance>> ListAsync(
        Guid tenantId,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        var sql = """
select id, tenant_id TenantId, name, template_key TemplateKey, template_version TemplateVersion,
       values_json::text ValuesJson, branding_profile_id BrandingProfileId, status,
       created_by CreatedBy, created_at CreatedAt, updated_by UpdatedBy, updated_at UpdatedAt,
       printed_at PrintedAt, archived_at ArchivedAt, archived_by ArchivedBy
from ged.label_manual_instance
where tenant_id = @tenantId and reg_status = 'ACTIVE'
""" + (string.IsNullOrWhiteSpace(status) ? "" : " and status = @status") + " order by coalesce(updated_at, created_at) desc";

        var rows = await db.QueryAsync<Row>(new CommandDefinition(sql, new { tenantId, status }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToArray();
    }

    public async Task<ManualLabelInstance?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
select id, tenant_id TenantId, name, template_key TemplateKey, template_version TemplateVersion,
       values_json::text ValuesJson, branding_profile_id BrandingProfileId, status,
       created_by CreatedBy, created_at CreatedAt, updated_by UpdatedBy, updated_at UpdatedAt,
       printed_at PrintedAt, archived_at ArchivedAt, archived_by ArchivedBy
from ged.label_manual_instance
where id = @id and tenant_id = @tenantId and reg_status = 'ACTIVE'
""";
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(sql, new { id, tenantId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<ManualLabelInstance> DuplicateAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var source = await GetAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");

        await using var db = await dbFactory.OpenAsync(cancellationToken);
        var newId = Guid.NewGuid();
        var copyName = $"{source.Name ?? "Etiqueta avulsa"} (cópia)";

        const string sql = """
insert into ged.label_manual_instance(
    id, tenant_id, name, template_key, template_version, values_json,
    branding_profile_id, status, created_by, created_at, updated_by, updated_at, reg_status)
select @newId, tenant_id, @copyName, template_key, template_version, values_json,
       branding_profile_id, 'DRAFT', @userId, now(), @userId, now(), 'ACTIVE'
from ged.label_manual_instance
where id = @id and tenant_id = @tenantId and reg_status = 'ACTIVE'
""";

        await db.ExecuteAsync(new CommandDefinition(sql, new { newId, copyName, id, tenantId, userId }, cancellationToken: cancellationToken));
        return (await GetAsync(tenantId, newId, cancellationToken))!;
    }

    public async Task<ManualLabelInstance> ArchiveAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
update ged.label_manual_instance
set status = 'ARCHIVED',
    archived_at = now(),
    archived_by = @userId,
    updated_by = @userId,
    updated_at = now()
where id = @id and tenant_id = @tenantId and reg_status = 'ACTIVE'
""";
        var changed = await db.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, userId }, cancellationToken: cancellationToken));
        if (changed != 1) throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");
        return (await GetAsync(tenantId, id, cancellationToken))!;
    }

    public async Task MarkPrintedAsync(Guid tenantId, Guid userId, Guid id, string? reprintReason = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
update ged.label_manual_instance
set status = 'PRINTED',
    printed_at = coalesce(printed_at, now()),
    updated_by = @userId,
    updated_at = now()
where id = @id and tenant_id = @tenantId and reg_status = 'ACTIVE'
""";
        var changed = await db.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, userId }, cancellationToken: cancellationToken));
        if (changed != 1) throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");
    }

    private static LabelCanvasDocumentDto Document(LabelCanvasDesignDto design) =>
        JsonSerializer.Deserialize<LabelCanvasDocumentDto>(design.DesignJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? new();

    private static string ResolveName(string? name, IReadOnlyDictionary<string, string?> values)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        var ctrl = ResolveControlNumber(values);
        var title = values.GetValueOrDefault("title") ?? values.GetValueOrDefault("subject");
        if (!string.IsNullOrWhiteSpace(ctrl) && !string.IsNullOrWhiteSpace(title))
            return $"{ctrl} — {title}".Trim();
        if (!string.IsNullOrWhiteSpace(ctrl)) return ctrl.Trim();
        if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        return "Etiqueta avulsa";
    }

    private static string? ResolveControlNumber(IReadOnlyDictionary<string, string?> values)
    {
        var ctrl = values.GetValueOrDefault("controlNumber");
        if (!string.IsNullOrWhiteSpace(ctrl)) return ctrl.Trim();
        var refNo = values.GetValueOrDefault("referenceNumber");
        if (!string.IsNullOrWhiteSpace(refNo)) return refNo.Trim();
        return null;
    }

    private static ManualLabelInstance Map(Row x)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(x.ValuesJson) ?? new();
        var ctrl = ResolveControlNumber(values);
        return new(x.Id, x.TenantId, x.TemplateKey, x.TemplateVersion, values, x.BrandingProfileId, x.Status,
            x.CreatedBy, x.CreatedAt, x.UpdatedBy, x.UpdatedAt, x.PrintedAt, x.ArchivedAt, x.ArchivedBy, x.Name, ctrl);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
        public string? Name { get; init; }
        public string TemplateKey { get; init; } = "";
        public int TemplateVersion { get; init; }
        public string ValuesJson { get; init; } = "{}";
        public Guid? BrandingProfileId { get; init; }
        public string Status { get; init; } = "";
        public Guid? CreatedBy { get; init; }
        public DateTime? CreatedAt { get; init; }
        public Guid? UpdatedBy { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public DateTime? PrintedAt { get; init; }
        public DateTime? ArchivedAt { get; init; }
        public Guid? ArchivedBy { get; init; }
    }
}
