using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Sectors;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Sectors;

public sealed class SectorOpeningService : ISectorOpeningService
{
    public const string CookieName = "ds_abertura";
    public static string CookieNameFor(Guid publicId) => "ds_ctx_" + publicId.ToString("N");
    private readonly IDbConnectionFactory _db;
    private readonly IDemaisSetoresService _sectors;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SectorOpeningService> _logger;

    public SectorOpeningService(IDbConnectionFactory db, IDemaisSetoresService sectors, IAuditWriter audit, ILogger<SectorOpeningService> logger)
    {
        _db = db;
        _sectors = sectors;
        _audit = audit;
        _logger = logger;
    }

    public async Task<SectorOpeningResponse> OpenAsync(SectorOpeningRequest request, CancellationToken ct)
    {
        var correlation = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.Secret))
            return Refuse(SectorOpeningCodes.Unauthorized, "A integração não foi autorizada.", correlation);

        await using var conn = await _db.OpenAsync(ct);
        var integration = await conn.QuerySingleOrDefaultAsync<IntegrationRow>(new CommandDefinition("""
select id as Id, tenant_id as TenantId, secret_hash as SecretHash, source_system as SourceSystem, source_unit as SourceUnit,
       origins as Origins, catalog_codes as CatalogCodes, allow_encounter_list as AllowEncounterList,
       allow_encounter_switch as AllowEncounterSwitch, max_minutes as MaxMinutes, active as Active
from ged.sector_integration
where client_id=@clientId
""", new { clientId = request.ClientId.Trim() }, cancellationToken: ct));
        if (integration is null || !integration.Active || !SecretMatches(integration.SecretHash, request.Secret))
        {
            _logger.LogInformation("Abertura recusada. Correlacao={Correlation} Codigo={Code}", correlation, SectorOpeningCodes.Unauthorized);
            if (integration is not null)
                await Audit(integration.TenantId, integration.Id, "ACCESS_DENIED", "Abertura recusada para a integração.", correlation, ct);
            return Refuse(SectorOpeningCodes.Unauthorized, "A integração não foi autorizada.", correlation);
        }

        if (!OriginAllowed(integration.Origins, request.Origin, out var origin))
            return await Fail(integration, SectorOpeningCodes.Denied, "A origem não está autorizada para esta integração.", correlation, ct);
        var system = string.IsNullOrWhiteSpace(request.SourceSystem) ? integration.SourceSystem : request.SourceSystem.Trim();
        var unit = request.SourceUnit?.Trim() ?? "";
        if (!string.Equals(system, integration.SourceSystem, StringComparison.Ordinal) || !string.Equals(unit, integration.SourceUnit ?? "", StringComparison.Ordinal))
            return await Fail(integration, SectorOpeningCodes.Invalid, "O sistema ou a unidade não correspondem ao cadastro da integração.", correlation, ct);
        var encounterCode = request.EncounterCode?.Trim();
        if (string.IsNullOrWhiteSpace(encounterCode))
            return await Fail(integration, SectorOpeningCodes.Invalid, "Informe o código do atendimento.", correlation, ct);

        var kind = string.Equals(request.IdentifierKind, "CPF", StringComparison.OrdinalIgnoreCase) ? "CPF" : "ID";
        if (kind == "CPF" && SectorCpf.Normalize(request.Identifier) is null)
            return await Fail(integration, SectorOpeningCodes.Invalid, "O CPF não tem formato válido. Ele não foi tratado como código do paciente.", correlation, ct);
        if (kind == "ID" && SectorPatientCode.Normalize(request.Identifier) is null)
            return await Fail(integration, SectorOpeningCodes.Invalid, "Informe o identificador do paciente preservando zeros à esquerda.", correlation, ct);

        var found = await _sectors.FindPatientAsync(integration.TenantId, request.Identifier, system, unit, kind, ct);
        if (found.Status == ClinicalSourceStatus.Unavailable)
            return await Fail(integration, SectorOpeningCodes.Unavailable, found.Message, correlation, ct);
        if (found.Status == ClinicalSourceStatus.Failed)
            return await Fail(integration, SectorOpeningCodes.Invalid, found.Message, correlation, ct);
        if (found.Status != ClinicalSourceStatus.Found || found.Patient is null)
            return await Fail(integration, SectorOpeningCodes.PatientMissing, "O paciente não foi localizado nessa origem e unidade.", correlation, ct);

        var patientId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
select id from ged.sector_patient
where tenant_id=@tenantId and source_system=@system and source_unit=@unit and identifier_kind=@kind and external_code=@code
""", new { tenantId = integration.TenantId, system = found.Patient.SourceSystem, unit = found.Patient.SourceUnit ?? "", kind, code = found.Patient.ExternalCode }, cancellationToken: ct));
        if (patientId == Guid.Empty)
            return await Fail(integration, SectorOpeningCodes.PatientMissing, "O paciente não foi localizado nessa origem e unidade.", correlation, ct);
        var encounterId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
select id from ged.sector_encounter
where tenant_id=@tenantId and patient_id=@patientId and external_code=@code
""", new { tenantId = integration.TenantId, patientId, code = encounterCode }, cancellationToken: ct));
        if (encounterId == Guid.Empty)
            return await Fail(integration, SectorOpeningCodes.EncounterMismatch, "O atendimento não pertence ao paciente informado.", correlation, ct);

        var wantsDocument = request.DocumentId.HasValue || request.VersionId.HasValue || !string.IsNullOrWhiteSpace(request.DocumentName) || !string.IsNullOrWhiteSpace(request.DocumentCode);
        if (!wantsDocument && !integration.AllowEncounterList)
            return await Fail(integration, SectorOpeningCodes.Invalid, "Informe o tipo ou o documento. A consulta livre do atendimento não está habilitada nesta integração.", correlation, ct);

        string? catalog = null;
        if (!string.IsNullOrWhiteSpace(request.DocumentCode) || !string.IsNullOrWhiteSpace(request.DocumentName))
        {
            catalog = await ResolveCatalogAsync(conn, integration.TenantId, request.DocumentCode, request.DocumentName, ct);
            if (catalog is null)
                return await Fail(integration, SectorOpeningCodes.Invalid, "O tipo documental não foi encontrado no catálogo ativo.", correlation, ct);
        }
        if (catalog is not null && integration.CatalogCodes.Length > 0 && !integration.CatalogCodes.Contains(catalog, StringComparer.Ordinal))
            return await Fail(integration, SectorOpeningCodes.Denied, "A integração não alcança este tipo documental.", correlation, ct);

        var rows = (await conn.QueryAsync<OccurrenceRow>(new CommandDefinition("""
select l.document_id as DocumentId, l.version_id as VersionId, l.catalog_code as CatalogCode
from ged.sector_document_link l
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A'
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.patient_id=@patientId and l.reg_status='A'
  and (@catalog is null or l.catalog_code=@catalog)
  and (@documentId is null or l.document_id=@documentId)
  and (@versionId is null or l.version_id=@versionId)
  and (cardinality(@allowed)=0 or l.catalog_code = any(@allowed))
""", new
        {
            tenantId = integration.TenantId, encounterId, patientId, catalog,
            documentId = request.DocumentId, versionId = request.VersionId,
            allowed = integration.CatalogCodes ?? []
        }, cancellationToken: ct))).ToArray();

        if ((request.DocumentId.HasValue || request.VersionId.HasValue) && rows.Length == 0)
            return await Fail(integration, SectorOpeningCodes.DocumentMissing, "Não há documento elegível neste atendimento.", correlation, ct);

        var mode = string.Equals(request.Mode, "documento", StringComparison.OrdinalIgnoreCase) ? "documento" : "consulta";
        Guid? documentId = null;
        Guid? versionId = null;
        var code = SectorOpeningCodes.Opened;
        var message = "Abertura concedida para o atendimento informado.";
        if (mode == "documento" && rows.Length == 1)
        {
            documentId = rows[0].DocumentId;
            versionId = rows[0].VersionId;
            catalog ??= rows[0].CatalogCode;
            message = "Abertura concedida para o documento encontrado.";
        }
        else if (mode == "documento" && rows.Length == 0)
        {
            code = SectorOpeningCodes.DocumentMissing;
            message = "Não há documento deste tipo no atendimento. O contexto foi mantido.";
            mode = "consulta";
        }
        else if (mode == "documento" && rows.Length > 1)
        {
            code = SectorOpeningCodes.Several;
            message = "Há mais de uma ocorrência. Nenhuma foi escolhida automaticamente.";
            mode = "consulta";
            catalog ??= rows[0].CatalogCode;
        }
        var storedMode = mode == "documento" && documentId.HasValue ? "documento" : "consulta";
        var scopeKind = storedMode == "documento" ? "documento" : catalog is not null ? "tipo" : "atendimento";
        var allowSwitch = integration.AllowEncounterSwitch && scopeKind == "atendimento";
        var minutes = Math.Clamp(integration.MaxMinutes <= 0 ? 15 : integration.MaxMinutes, 1, 60);
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var pageCode = NewSecret();
        var frameCode = NewSecret();
        await InsertCodeAsync(conn, integration, pageCode, patientId, encounterId, catalog, documentId, versionId, storedMode, scopeKind, allowSwitch, origin, correlation, expires, ct);
        await InsertCodeAsync(conn, integration, frameCode, patientId, encounterId, catalog, documentId, versionId, storedMode, scopeKind, allowSwitch, origin, correlation, expires, ct);
        var scope = documentId.HasValue ? "documento" : catalog is null ? "atendimento" : "tipo";
        await Audit(integration.TenantId, integration.Id, "FILE_PREVIEW", "Abertura temporária concedida. Correlacao " + correlation + ".", correlation, ct);
        _logger.LogInformation("Abertura concedida. Tenant={TenantId} Integracao={IntegrationId} Correlacao={Correlation} Escopo={Scope} Ocorrencias={Count}", integration.TenantId, integration.Id, correlation, scope, rows.Length);
        return new SectorOpeningResponse(true, code, message, SectorOpeningLink.Path(pageCode), SectorOpeningLink.Path(frameCode), expires, scope, correlation, rows.Length);
    }

    public async Task<SectorConsumedOpening?> ConsumeAsync(string code, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        try
        {
            var opened = await SectorOpeningExchange.ConsumeAsync(conn, code, ct);
            if (opened is not null)
                _logger.LogInformation("Codigo de abertura trocado por sessao. Correlacao={Correlation} Contexto={PublicId}", opened.CorrelationId, opened.PublicId);
            return opened;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Troca da abertura cancelada antes da confirmacao.");
            throw;
        }
    }

    public async Task<SectorSessionAccess?> ReadSessionAsync(string? cookieValue, Guid publicId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await SectorOpeningExchange.ReadAsync(conn, cookieValue, publicId, ct);
    }

    public async Task<bool> SwitchEncounterAsync(string? cookieValue, Guid publicId, string encounterCode, CancellationToken ct)
    {
        var before = await ReadSessionAsync(cookieValue, publicId, ct);
        if (before is null || !before.AllowEncounterSwitch) return false;
        await using var conn = await _db.OpenAsync(ct);
        var changed = await SectorOpeningExchange.SwitchAsync(conn, cookieValue, publicId, encounterCode, ct);
        if (changed)
            await Audit(before.TenantId, before.PatientId, "FILE_PREVIEW", "Atendimento da abertura alterado com permissão explícita. Correlacao " + before.CorrelationId + ".", before.CorrelationId, ct);
        return changed;
    }

    public async Task<bool> AuthorizeCommandAsync(string? cookieValue, Guid publicId, string? protectionToken, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await SectorOpeningExchange.AuthorizeAsync(conn, cookieValue, publicId, protectionToken, ct);
    }

    public async Task<string?> RegisterIntegrationAsync(SectorIntegrationRegistration registration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(registration.ClientId) || string.IsNullOrWhiteSpace(registration.Secret) || registration.Secret.Length < 16)
            return "Informe um identificador e uma chave com pelo menos 16 caracteres. A chave não será mostrada de novo.";
        if (string.IsNullOrWhiteSpace(registration.SourceSystem) || registration.Origins.Length == 0)
            return "Informe o sistema e ao menos uma origem http ou https.";
        var origins = new List<string>();
        foreach (var item in registration.Origins)
        {
            if (!Uri.TryCreate(item?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http")
                return "Cada origem precisa ser http ou https, sem caminho.";
            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_integration
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, origins, catalog_codes, allow_encounter_list, allow_encounter_switch, max_minutes, active)
values
 (gen_random_uuid(), @tenantId, @clientId, @hash, @system, @unit, @origins, @catalogs, @allow, @allowSwitch, @minutes, true)
on conflict (tenant_id, client_id) do update
set secret_hash=excluded.secret_hash, source_system=excluded.source_system, source_unit=excluded.source_unit,
    origins=excluded.origins, catalog_codes=excluded.catalog_codes, allow_encounter_list=excluded.allow_encounter_list,
    allow_encounter_switch=excluded.allow_encounter_switch, max_minutes=excluded.max_minutes, active=true
""", new
        {
            tenantId = registration.TenantId, clientId = registration.ClientId.Trim(), hash = Hash(registration.Secret),
            system = registration.SourceSystem.Trim(), unit = registration.SourceUnit?.Trim() ?? "",
            origins = origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            catalogs = registration.CatalogCodes.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
            allow = registration.AllowEncounterList, allowSwitch = registration.AllowEncounterSwitch, minutes = Math.Clamp(registration.MaxMinutes, 1, 60)
        }, cancellationToken: ct));
        await Audit(registration.TenantId, registration.ActorId, "PERMISSION_CHANGE", "Integração cadastrada ou atualizada, sem registrar a chave.", null, ct);
        return null;
    }

    public async Task<string?> DeactivateIntegrationAsync(Guid tenantId, Guid actorId, string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return "Informe a integração.";
        await using var conn = await _db.OpenAsync(ct);
        await SectorOpeningExchange.DeactivateAsync(conn, tenantId, clientId.Trim(), ct);
        await Audit(tenantId, actorId, "PERMISSION_CHANGE", "Integração desativada. Códigos e sessões pendentes foram revogados.", null, ct);
        return null;
    }

    private static async Task InsertCodeAsync(Npgsql.NpgsqlConnection conn, IntegrationRow integration, string code, Guid patientId, Guid encounterId, string? catalog, Guid? documentId, Guid? versionId, string mode, string scopeKind, bool allowSwitch, string origin, string correlation, DateTimeOffset expires, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_open_code
 (id, tenant_id, code_hash, patient_id, encounter_id, catalog_code, document_id, version_id, mode, scope_kind, allow_encounter_switch, origin, integration_id, correlation_id, expires_at)
values
 (gen_random_uuid(), @tenantId, @hash, @patientId, @encounterId, @catalog, @documentId, @versionId, @mode, @scopeKind, @allowSwitch, @origin, @integrationId, @correlation, @expires)
""", new
        {
            tenantId = integration.TenantId, hash = Hash(code), patientId, encounterId, catalog, documentId, versionId, mode, scopeKind, allowSwitch, origin,
            integrationId = integration.Id, correlation, expires
        }, cancellationToken: ct));
    }

    private static async Task<string?> ResolveCatalogAsync(Npgsql.NpgsqlConnection conn, Guid tenantId, string? code, string? name, CancellationToken ct)
    {
        var wantedCode = code?.Trim();
        var wantedName = name?.Trim();
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select t.catalog_code
from ged.sector_document_type t
left join ged.sector_document_synonym s on s.tenant_id=t.tenant_id and s.catalog_code=t.catalog_code
where t.tenant_id=@tenantId and t.active
  and (
    (@code is not null and t.catalog_code=@code)
    or (@name is not null and (lower(t.name)=lower(@name) or lower(s.synonym)=lower(@name)))
  )
order by case when @code is not null and t.catalog_code=@code then 0 else 1 end
limit 1
""", new { tenantId, code = string.IsNullOrWhiteSpace(wantedCode) ? null : wantedCode, name = string.IsNullOrWhiteSpace(wantedName) ? null : wantedName }, cancellationToken: ct));
    }

    private async Task<SectorOpeningResponse> Fail(IntegrationRow integration, string code, string message, string correlation, CancellationToken ct)
    {
        await Audit(integration.TenantId, integration.Id, "ACCESS_DENIED", "Abertura não concedida. Codigo " + code + ". Correlacao " + correlation + ".", correlation, ct);
        _logger.LogInformation("Abertura não concedida. Tenant={TenantId} Correlacao={Correlation} Codigo={Code}", integration.TenantId, correlation, code);
        return Refuse(code, message, correlation);
    }

    private static SectorOpeningResponse Refuse(string code, string message, string correlation)
        => new(false, code, message, null, null, null, null, correlation, 0);

    private async Task Audit(Guid tenantId, Guid entityId, string action, string summary, string? correlation, CancellationToken ct)
        => await _audit.WriteAsync(new AuditWriteCommand(tenantId, null, action, "sector_document", entityId, summary, null, null, correlation, null), ct);

    private static bool OriginAllowed(string[] origins, string? presented, out string origin)
    {
        origin = "";
        if (!Uri.TryCreate(presented, UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http") return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        var granted = origin;
        return origins.Any(item => string.Equals(item, granted, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SecretMatches(string storedHex, string secret)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        try
        {
            var expected = Convert.FromHexString(storedHex.Trim());
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NewSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private sealed class IntegrationRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string SecretHash { get; set; } = "";
        public string SourceSystem { get; set; } = "";
        public string SourceUnit { get; set; } = "";
        public string[] Origins { get; set; } = [];
        public string[] CatalogCodes { get; set; } = [];
        public bool AllowEncounterList { get; set; }
        public bool AllowEncounterSwitch { get; set; }
        public int MaxMinutes { get; set; }
        public bool Active { get; set; }
    }

    private sealed class OccurrenceRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string CatalogCode { get; set; } = "";
    }

    private sealed class CodeRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PatientId { get; set; }
        public Guid EncounterId { get; set; }
        public string? CatalogCode { get; set; }
        public Guid? DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string Mode { get; set; } = "consulta";
        public string Origin { get; set; } = "";
        public Guid IntegrationId { get; set; }
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
