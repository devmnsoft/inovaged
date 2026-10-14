using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Sectors;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Sectors;

public sealed class SectorOpeningService : ISectorOpeningService
{
    public const string CookieName = "ds_abertura";
    public const int RequestsPerMinute = 30;
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
        var integrations = (await conn.QueryAsync<IntegrationRow>(new CommandDefinition("""
select id as Id, tenant_id as TenantId, client_id as ClientId, secret_hash as SecretHash, source_system as SourceSystem, source_unit as SourceUnit,
       origins as Origins, catalog_codes as CatalogCodes, allow_encounter_list as AllowEncounterList,
       allow_encounter_switch as AllowEncounterSwitch, max_minutes as MaxMinutes, active as Active
from ged.sector_integration
where client_id=@clientId and active
""", new { clientId = request.ClientId.Trim() }, cancellationToken: ct))).ToArray();
        if (integrations.Length != 1 || !SecretMatches(integrations[0].SecretHash, request.Secret))
        {
            _logger.LogInformation("Abertura recusada. Correlacao={Correlation} Codigo={Code}", correlation, SectorOpeningCodes.Unauthorized);
            if (integrations.Length == 1)
                await Audit(integrations[0].TenantId, integrations[0].Id, "ACCESS_DENIED", "Abertura recusada para a integração.", correlation, ct);
            return Refuse(SectorOpeningCodes.Unauthorized, "A integração não foi autorizada.", correlation);
        }

        var integration = integrations[0];
        if (!OriginAllowed(integration.Origins, request.Origin, out var origin))
            return await Fail(conn, integration, SectorOpeningCodes.Denied, "A origem não está autorizada para esta integração.", correlation, null, true, ct);
        var system = string.IsNullOrWhiteSpace(request.SourceSystem) ? integration.SourceSystem : request.SourceSystem.Trim();
        var unit = request.SourceUnit?.Trim() ?? "";
        if (!string.Equals(system, integration.SourceSystem, StringComparison.Ordinal) || !string.Equals(unit, integration.SourceUnit ?? "", StringComparison.Ordinal))
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "O sistema ou a unidade não correspondem ao cadastro da integração.", correlation, null, true, ct);
        return await GrantAsync(conn, integration, request, correlation, null, null, origin, ct);
    }

    public async Task<SectorEntryIssue> CreateEntryAsync(SectorEntryDraft draft, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var integrations = (await conn.QueryAsync<IntegrationRow>(new CommandDefinition("""
select id as Id, tenant_id as TenantId
from ged.sector_integration
where client_id=@clientId and active
""", new { clientId = draft.ClientId }, cancellationToken: ct))).ToArray();
        if (integrations.Length != 1)
        {
            _logger.LogInformation("Entrada por URL recusada antes da autorização.");
            return new SectorEntryIssue(false, null);
        }

        var ticketId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_entry_ticket
 (id, integration_id, tenant_id, identifier_kind, identifier_value, encounter_code, document_name, document_code, document_id, version_id, mode, expires_at)
values
 (@id, @integrationId, @tenantId, @kind, @identifier, @encounter, @name, @code, @documentId, @versionId, @mode, now() + interval '15 minutes')
""", new
        {
            id = ticketId, integrationId = integrations[0].Id, tenantId = integrations[0].TenantId,
            kind = draft.IdentifierKind, identifier = draft.Identifier, encounter = draft.EncounterCode,
            name = draft.DocumentName, code = draft.DocumentCode, documentId = draft.DocumentId, versionId = draft.VersionId, mode = draft.Mode
        }, cancellationToken: ct));
        _logger.LogInformation("Entrada por URL aceita para continuação. Ticket={TicketId} Integracao={IntegrationId}", ticketId, integrations[0].Id);
        return new SectorEntryIssue(true, ticketId);
    }

    public async Task<SectorEntryContinuation> ContinueEntryAsync(Guid ticketId, Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var ticket = await conn.QuerySingleOrDefaultAsync<TicketRow>(new CommandDefinition("""
select id as Id, integration_id as IntegrationId, tenant_id as TenantId, identifier_kind as IdentifierKind,
       identifier_value as IdentifierValue, encounter_code as EncounterCode, document_name as DocumentName,
       document_code as DocumentCode, document_id as DocumentId, version_id as VersionId, mode as Mode, expires_at as ExpiresAt
from ged.sector_entry_ticket
where id=@ticketId and consumed_at is null and expires_at>now()
""", new { ticketId }, cancellationToken: ct));
        if (ticket is null)
            return new SectorEntryContinuation(false, null, 400, "Este link não pode ser usado.");
        if (ticket.TenantId != tenantId)
            return new SectorEntryContinuation(false, null, 403, "A integração não permite consultar este documento.");
        if (!await _sectors.CanEnterAsync(tenantId, userId, isFullAdmin, ct))
            return new SectorEntryContinuation(false, null, 403, "Seu usuário não consulta o módulo Demais setores.");

        var integration = await conn.QuerySingleOrDefaultAsync<IntegrationRow>(new CommandDefinition("""
select id as Id, tenant_id as TenantId, client_id as ClientId, secret_hash as SecretHash, source_system as SourceSystem, source_unit as SourceUnit,
       origins as Origins, catalog_codes as CatalogCodes, allow_encounter_list as AllowEncounterList,
       allow_encounter_switch as AllowEncounterSwitch, max_minutes as MaxMinutes, active as Active
from ged.sector_integration
where id=@id and tenant_id=@tenantId and active
""", new { id = ticket.IntegrationId, tenantId }, cancellationToken: ct));
        if (integration is null || integration.Origins.Length == 0)
            return new SectorEntryContinuation(false, null, 403, "A integração não permite consultar este documento.");

        string[]? granted = null;
        if (!isFullAdmin)
        {
            var visible = await _sectors.VisibleCatalogCodesAsync(tenantId, userId, false, ct);
            granted = integration.CatalogCodes.Length == 0
                ? visible.ToArray()
                : visible.Where(code => integration.CatalogCodes.Contains(code, StringComparer.Ordinal)).ToArray();
            if (granted.Length == 0)
                return new SectorEntryContinuation(false, null, 403, "A integração não permite consultar este documento.");
        }

        var origin = integration.Origins.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).First();
        var request = new SectorOpeningRequest(integration.ClientId, "continuacao", ticket.IdentifierKind, ticket.IdentifierValue, integration.SourceSystem, integration.SourceUnit, ticket.EncounterCode, ticket.DocumentName, ticket.DocumentCode, ticket.DocumentId, ticket.VersionId, ticket.Mode, origin);
        var opened = await GrantAsync(conn, integration, request, Guid.NewGuid().ToString("N"), granted, ticket.Id, origin, ct);
        if (!string.IsNullOrWhiteSpace(opened.PageUrl))
            return new SectorEntryContinuation(true, opened.PageUrl, 200, opened.Message);
        var status = opened.Code == SectorOpeningCodes.Unauthorized ? 401 : 422;
        return new SectorEntryContinuation(false, null, status, opened.Message);
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

    private async Task<SectorOpeningResponse> GrantAsync(NpgsqlConnection conn, IntegrationRow integration, SectorOpeningRequest request, string correlation, string[]? grantedCatalog, Guid? ticketId, string origin, CancellationToken ct)
    {
        if (!SectorEntryContract.IdempotencyKey(request.IdempotencyKey, out var idempotency))
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "A chave de idempotência não é válida.", correlation, ticketId, true, ct);
        var encounterCode = request.EncounterCode?.Trim() ?? "";
        if (encounterCode.Length is 0 or > SectorEntryContract.EncounterMax)
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "Informe o código do atendimento.", correlation, ticketId, true, ct);

        string kind;
        string? identifier;
        if (string.Equals(request.IdentifierKind, "CPF", StringComparison.OrdinalIgnoreCase))
        {
            kind = "CPF";
            identifier = SectorCpf.Normalize(request.Identifier);
            if (identifier is null)
                return await Fail(conn, integration, SectorOpeningCodes.Invalid, "O CPF não tem formato válido. Ele não foi tratado como código do paciente.", correlation, ticketId, true, ct);
        }
        else if (string.Equals(request.IdentifierKind, "ID", StringComparison.OrdinalIgnoreCase))
        {
            kind = "ID";
            identifier = SectorPatientCode.Normalize(request.Identifier);
            if (identifier is null)
                return await Fail(conn, integration, SectorOpeningCodes.Invalid, "Informe o identificador do paciente preservando zeros à esquerda.", correlation, ticketId, true, ct);
        }
        else
        {
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "Informe o tipo do identificador: ID ou CPF.", correlation, ticketId, true, ct);
        }

        string mode;
        if (string.IsNullOrWhiteSpace(request.Mode) || string.Equals(request.Mode, "consulta", StringComparison.OrdinalIgnoreCase))
            mode = "consulta";
        else if (string.Equals(request.Mode, "documento", StringComparison.OrdinalIgnoreCase))
            mode = "documento";
        else
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "O modo precisa ser consulta ou documento.", correlation, ticketId, true, ct);

        var system = integration.SourceSystem;
        var unit = integration.SourceUnit ?? "";
        var located = await SectorIdentityStore.FindAsync(conn, integration.TenantId, system, unit, kind, identifier, encounterCode, ct);
        var localReady = located is not null && located.ConfirmedAt is not null && located.EncounterId is not null && located.EncounterConfirmedAt is not null;
        Guid patientId;
        Guid encounterId;
        var usedLocal = false;
        if (localReady)
        {
            patientId = located!.PatientId;
            encounterId = located.EncounterId!.Value;
            usedLocal = true;
        }
        else
        {
            var found = await _sectors.FindPatientAsync(integration.TenantId, identifier, system, unit, kind, ct);
            if (found.Status == ClinicalSourceStatus.Unavailable)
                return await Fail(conn, integration, SectorOpeningCodes.Unavailable, found.Message, correlation, ticketId, false, ct);
            if (found.Status == ClinicalSourceStatus.Failed)
                return await Fail(conn, integration, SectorOpeningCodes.Invalid, found.Message, correlation, ticketId, true, ct);
            if (found.Status != ClinicalSourceStatus.Found || found.Patient is null)
                return await Fail(conn, integration, SectorOpeningCodes.PatientMissing, "O paciente não foi localizado nessa origem e unidade.", correlation, ticketId, true, ct);
            located = await SectorIdentityStore.FindAsync(conn, integration.TenantId, system, unit, kind, identifier, encounterCode, ct);
            if (located is null)
                return await Fail(conn, integration, SectorOpeningCodes.PatientMissing, "O paciente não foi localizado nessa origem e unidade.", correlation, ticketId, true, ct);
            if (located.EncounterId is null)
                return await Fail(conn, integration, SectorOpeningCodes.EncounterMismatch, "Este atendimento não corresponde ao paciente informado.", correlation, ticketId, true, ct);
            patientId = located.PatientId;
            encounterId = located.EncounterId.Value;
        }

        var wantsDocument = request.DocumentId.HasValue || request.VersionId.HasValue || !string.IsNullOrWhiteSpace(request.DocumentName) || !string.IsNullOrWhiteSpace(request.DocumentCode);
        if (!wantsDocument && !integration.AllowEncounterList)
            return await Fail(conn, integration, SectorOpeningCodes.Invalid, "Informe o tipo ou o documento. A consulta livre do atendimento não está habilitada nesta integração.", correlation, ticketId, true, ct);

        var allowed = grantedCatalog ?? integration.CatalogCodes ?? [];
        string? catalog = null;
        if (!string.IsNullOrWhiteSpace(request.DocumentCode) || !string.IsNullOrWhiteSpace(request.DocumentName))
        {
            var resolved = await ResolveCatalogAsync(conn, integration.TenantId, request.DocumentCode, request.DocumentName, ct);
            if (resolved.Outcome == "ambiguo")
                return await Fail(conn, integration, SectorOpeningCodes.Ambiguous, "Há mais de um tipo com este nome. Informe o código do tipo.", correlation, ticketId, true, ct);
            if (resolved.Outcome == "contraditorio")
                return await Fail(conn, integration, SectorOpeningCodes.Contradictory, "O código e o nome do documento não apontam para o mesmo tipo.", correlation, ticketId, true, ct);
            if (resolved.Outcome != "unico" || resolved.CatalogCode is null)
                return await Fail(conn, integration, SectorOpeningCodes.Invalid, "O tipo documental não foi encontrado no catálogo ativo.", correlation, ticketId, true, ct);
            catalog = resolved.CatalogCode;
        }
        if (catalog is not null && allowed.Length > 0 && !allowed.Contains(catalog, StringComparer.Ordinal))
            return await Fail(conn, integration, SectorOpeningCodes.Denied, "A integração não permite consultar este documento.", correlation, ticketId, true, ct);

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
            documentId = request.DocumentId, versionId = request.VersionId, allowed
        }, cancellationToken: ct))).ToArray();

        var locksDocument = request.DocumentId.HasValue || request.VersionId.HasValue;
        Guid? documentId = null;
        Guid? versionId = null;
        var code = SectorOpeningCodes.Opened;
        var message = "Abertura concedida para o atendimento informado.";
        if (locksDocument && rows.Length == 0)
        {
            var outside = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1 from ged.sector_document_link l
  where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.patient_id=@patientId and l.reg_status='A'
    and (@documentId is null or l.document_id=@documentId)
    and (@versionId is null or l.version_id=@versionId)
)
""", new { tenantId = integration.TenantId, encounterId, patientId, documentId = request.DocumentId, versionId = request.VersionId }, cancellationToken: ct));
            var denied = outside
                ? SectorOpeningCodes.Denied
                : SectorOpeningCodes.DocumentMissing;
            var deniedMessage = outside
                ? "A integração não permite consultar este documento."
                : "Não há documento elegível neste atendimento.";
            return await Fail(conn, integration, denied, deniedMessage, correlation, ticketId, true, ct);
        }
        if (locksDocument && rows.Length > 1)
            return await Fail(conn, integration, SectorOpeningCodes.Several, "Há mais de um documento deste tipo.", correlation, ticketId, true, ct);
        if (locksDocument || (mode == "documento" && rows.Length == 1))
        {
            documentId = rows[0].DocumentId;
            versionId = rows[0].VersionId;
            catalog ??= rows[0].CatalogCode;
            if (allowed.Length > 0 && !allowed.Contains(catalog, StringComparer.Ordinal))
                return await Fail(conn, integration, SectorOpeningCodes.Denied, "A integração não permite consultar este documento.", correlation, ticketId, true, ct);
            message = "Abertura concedida para o documento encontrado.";
        }
        else if (rows.Length == 0 && (catalog is not null || mode == "documento"))
        {
            code = SectorOpeningCodes.DocumentMissing;
            message = "Não há documento deste tipo no atendimento. O contexto foi mantido.";
        }
        else if (rows.Length > 1)
        {
            if (catalog is null)
                return await Fail(conn, integration, SectorOpeningCodes.Several, "Há mais de um documento neste atendimento. Informe o nome do documento.", correlation, ticketId, true, ct);
            code = SectorOpeningCodes.Several;
            message = "Há mais de um documento deste tipo.";
        }

        var storedMode = documentId.HasValue && versionId.HasValue ? "documento" : "consulta";
        var scopeKind = storedMode == "documento" ? "documento" : catalog is not null ? "tipo" : "atendimento";
        if (usedLocal && located?.ConfirmedAt is DateTime confirmed)
            message += " Identidade confirmada em " + confirmed.ToString("yyyy-MM-dd") + ". A origem clínica não foi consultada nesta abertura.";
        var allowSwitch = integration.AllowEncounterSwitch && scopeKind == "atendimento";
        var minutes = Math.Clamp(integration.MaxMinutes <= 0 ? 15 : integration.MaxMinutes, 1, 60);
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var pageCode = NewSecret();
        var frameCode = NewSecret();
        var scope = documentId.HasValue ? "documento" : catalog is null ? "atendimento" : "tipo";
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("select id from ged.sector_integration where id=@id for update", new { id = integration.Id }, tx, cancellationToken: ct));
            if (idempotency is not null)
            {
                var previous = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select correlation_id from ged.sector_open_request
where integration_id=@id and idempotency_key=@key
""", new { id = integration.Id, key = idempotency }, tx, cancellationToken: ct));
                if (previous is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Refuse(SectorOpeningCodes.Repeated, "Esta chave de idempotência já foi usada. A primeira resposta não é reenviada porque os códigos opacos não ficam guardados. Use outra chave para uma nova abertura.", previous);
                }
            }
            var recent = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
select count(*) from ged.sector_open_request
where integration_id=@id and created_at > now() - interval '60 seconds'
""", new { id = integration.Id }, tx, cancellationToken: ct));
            if (recent >= RequestsPerMinute)
            {
                await tx.RollbackAsync(ct);
                return await Fail(conn, integration, SectorOpeningCodes.RateLimited, "O limite de aberturas desta integração foi atingido. Aguarde um minuto e tente de novo.", correlation, ticketId, false, ct);
            }
            await InsertCodeAsync(conn, tx, integration, pageCode, patientId, encounterId, catalog, documentId, versionId, storedMode, scopeKind, allowSwitch, origin, correlation, expires, grantedCatalog, ct);
            await InsertCodeAsync(conn, tx, integration, frameCode, patientId, encounterId, catalog, documentId, versionId, storedMode, scopeKind, allowSwitch, origin, correlation, expires, grantedCatalog, ct);
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_open_request
 (id, tenant_id, integration_id, idempotency_key, correlation_id, page_code_hash, frame_code_hash)
values
 (@id, @tenantId, @integrationId, @key, @correlation, @pageHash, @frameHash)
""", new
            {
                id = Guid.NewGuid(), tenantId = integration.TenantId, integrationId = integration.Id, key = idempotency, correlation,
                pageHash = Hash(pageCode), frameHash = Hash(frameCode)
            }, tx, cancellationToken: ct));
            if (ticketId.HasValue)
            {
                var consumed = await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_entry_ticket set consumed_at=now()
where id=@ticketId and consumed_at is null and expires_at>now()
""", new { ticketId }, tx, cancellationToken: ct));
                if (consumed != 1)
                {
                    await tx.RollbackAsync(ct);
                    return Refuse(SectorOpeningCodes.Invalid, "Este link não pode ser usado.", correlation);
                }
            }
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            var previous = idempotency is null ? null : await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select correlation_id from ged.sector_open_request where integration_id=@id and idempotency_key=@key
""", new { id = integration.Id, key = idempotency }, cancellationToken: ct));
            return Refuse(SectorOpeningCodes.Repeated, "Esta chave de idempotência já foi usada. A primeira resposta não é reenviada porque os códigos opacos não ficam guardados. Use outra chave para uma nova abertura.", previous ?? correlation);
        }

        try
        {
            await Audit(integration.TenantId, integration.Id, "FILE_PREVIEW", "Abertura temporária concedida. Correlacao " + correlation + ".", correlation, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abertura persistida e auditoria não gravada. Correlacao={Correlation}", correlation);
        }
        _logger.LogInformation("Abertura concedida. Tenant={TenantId} Integracao={IntegrationId} Correlacao={Correlation} Escopo={Scope} Ocorrencias={Count} Local={Local}", integration.TenantId, integration.Id, correlation, scope, rows.Length, usedLocal);
        return new SectorOpeningResponse(true, code, message, SectorOpeningLink.Path(pageCode), SectorOpeningLink.Path(frameCode), expires, scope, correlation, rows.Length);
    }

    private static async Task InsertCodeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IntegrationRow integration, string code, Guid patientId, Guid encounterId, string? catalog, Guid? documentId, Guid? versionId, string mode, string scopeKind, bool allowSwitch, string origin, string correlation, DateTimeOffset expires, string[]? grantedCatalog, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_open_code
 (id, tenant_id, code_hash, patient_id, encounter_id, catalog_code, document_id, version_id, mode, scope_kind, allow_encounter_switch, origin, integration_id, correlation_id, expires_at, granted_catalog)
values
 (gen_random_uuid(), @tenantId, @hash, @patientId, @encounterId, @catalog, @documentId, @versionId, @mode, @scopeKind, @allowSwitch, @origin, @integrationId, @correlation, @expires, @grantedCatalog)
""", new
        {
            tenantId = integration.TenantId, hash = Hash(code), patientId, encounterId, catalog, documentId, versionId, mode, scopeKind, allowSwitch, origin,
            integrationId = integration.Id, correlation, expires, grantedCatalog
        }, tx, cancellationToken: ct));
    }

    private static async Task<CatalogHit> ResolveCatalogAsync(NpgsqlConnection conn, Guid tenantId, string? code, string? name, CancellationToken ct)
    {
        var wantedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var wantedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        string? byCode = null;
        if (wantedCode is not null)
        {
            byCode = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select catalog_code from ged.sector_document_type
where tenant_id=@tenantId and active and catalog_code=@code
""", new { tenantId, code = wantedCode }, cancellationToken: ct));
        }
        var byName = Array.Empty<string>();
        if (wantedName is not null)
        {
            byName = (await conn.QueryAsync<string>(new CommandDefinition("""
select distinct t.catalog_code
from ged.sector_document_type t
left join ged.sector_document_synonym s on s.tenant_id=t.tenant_id and s.catalog_code=t.catalog_code
where t.tenant_id=@tenantId and t.active
  and (
    lower(t.name)=lower(@name)
    or lower(s.synonym)=lower(@name)
    or exists (select 1 from unnest(coalesce(t.synonyms, '{}'::text[])) item where lower(item)=lower(@name))
  )
""", new { tenantId, name = wantedName }, cancellationToken: ct))).ToArray();
        }
        if (wantedCode is not null && wantedName is not null)
        {
            if (byCode is null || byName.Length != 1 || !string.Equals(byName[0], byCode, StringComparison.Ordinal))
                return new CatalogHit("contraditorio", null);
            return new CatalogHit("unico", byCode);
        }
        if (wantedCode is not null)
            return byCode is null ? new CatalogHit("ausente", null) : new CatalogHit("unico", byCode);
        if (byName.Length == 1) return new CatalogHit("unico", byName[0]);
        if (byName.Length > 1) return new CatalogHit("ambiguo", null);
        return new CatalogHit("ausente", null);
    }

    private async Task<SectorOpeningResponse> Fail(NpgsqlConnection conn, IntegrationRow integration, string code, string message, string correlation, Guid? ticketId, bool consumeTicket, CancellationToken ct)
    {
        if (consumeTicket && ticketId.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_entry_ticket set consumed_at=now()
where id=@ticketId and consumed_at is null
""", new { ticketId }, cancellationToken: ct));
        }
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
        public string ClientId { get; set; } = "";
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

    private sealed record CatalogHit(string Outcome, string? CatalogCode);

    private sealed class TicketRow
    {
        public Guid Id { get; set; }
        public Guid IntegrationId { get; set; }
        public Guid TenantId { get; set; }
        public string IdentifierKind { get; set; } = "";
        public string IdentifierValue { get; set; } = "";
        public string EncounterCode { get; set; } = "";
        public string? DocumentName { get; set; }
        public string? DocumentCode { get; set; }
        public Guid? DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string Mode { get; set; } = "consulta";
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
