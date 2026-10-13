using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Application.Sectors;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.DemaisSetores)]
public sealed class DemaisSetoresController : Controller
{
    private readonly IDemaisSetoresService _sectors;
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IFileStorage _storage;
    private readonly IAbacAuthorizationService _authorization;

    public DemaisSetoresController(IDemaisSetoresService sectors, ICurrentUser currentUser, IDbConnectionFactory db, IFileStorage storage, IAbacAuthorizationService authorization)
    {
        _sectors = sectors;
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
        _authorization = authorization;
    }

    private bool Admin => User.IsFullAdmin();

    [HttpGet]
    public async Task<IActionResult> Cadastrar(CancellationToken ct)
    {
        await _sectors.EnsureTenantAsync(_currentUser.TenantId, ct);
        ViewBag.Codes = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        ViewBag.CanCreate = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Cadastrar, ct);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Consultar(Guid? patientId, Guid? encounterId, CancellationToken ct)
    {
        await _sectors.EnsureTenantAsync(_currentUser.TenantId, ct);
        ViewBag.CanReview = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Conferir, ct);
        ViewBag.CanPrint = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Imprimir, ct);
        ViewBag.CanShare = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Compartilhar, ct);
        ViewBag.CanCorrect = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.CorrigirVinculo, ct);
        ViewBag.CanExtract = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.RevisarExtracao, ct);
        if (encounterId.HasValue)
            await _sectors.RefreshExtractionAsync(_currentUser.TenantId, encounterId.Value, ct);
        ViewBag.PatientId = patientId;
        ViewBag.EncounterId = encounterId;
        ViewBag.Context = encounterId.HasValue ? await ContextAsync(encounterId.Value, ct) : null;
        ViewBag.Siblings = encounterId.HasValue ? await SiblingsAsync(encounterId.Value, ct) : Array.Empty<SectorEncounterOption>();
        ViewBag.Documents = encounterId.HasValue ? await DocumentsAsync(encounterId.Value, ct) : Array.Empty<SectorDocumentRow>();
        ViewBag.Fields = encounterId.HasValue ? await FieldsAsync(encounterId.Value, ct) : Array.Empty<SectorFieldRow>();
        ViewBag.Findings = encounterId.HasValue ? await FindingsAsync(encounterId.Value, ct) : Array.Empty<SectorFindingRow>();
        ViewBag.Checklist = encounterId.HasValue ? await ChecklistAsync(encounterId.Value, ct) : Array.Empty<SectorChecklistRow>();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Parametrizar(CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Parametrizar, ct))
            return Forbid();
        await _sectors.EnsureTenantAsync(_currentUser.TenantId, ct);
        await using var conn = await _db.OpenAsync(ct);
        ViewBag.Kinds = await conn.QueryAsync("select source_system, raw_code, kind from ged.sector_kind_contract where tenant_id=@tenantId order by source_system, raw_code", new { tenantId = _currentUser.TenantId });
        ViewBag.Mappings = await conn.QueryAsync("select catalog_code, mapping_status, note from ged.sector_type_mapping where tenant_id=@tenantId order by catalog_code", new { tenantId = _currentUser.TenantId });
        ViewBag.Profiles = await conn.QueryAsync("select code, version_no, enabled from ged.sector_profile where tenant_id=@tenantId order by code, version_no", new { tenantId = _currentUser.TenantId });
        ViewBag.Origins = await conn.QueryAsync("select id, origin from ged.sector_embed_origin where tenant_id=@tenantId and reg_status='A' order by origin", new { tenantId = _currentUser.TenantId });
        ViewBag.Departments = await conn.QueryAsync("select code, name from ged.department where tenant_id=@tenantId and is_active and upper(code) in ('FATURAMENTO','FARMACIA','FARMÁCIA','CME','DDI') order by code", new { tenantId = _currentUser.TenantId });
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BuscarPaciente(string? externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct)
    {
        var result = await _sectors.FindPatientAsync(_currentUser.TenantId, externalCode, sourceSystem, sourceUnit, ct);
        if (result.Status != ClinicalSourceStatus.Found || result.Patient is null)
            return Json(new { status = result.Status.ToString(), message = result.Message, patient = (object?)null, encounters = Array.Empty<object>() });
        await using var conn = await _db.OpenAsync(ct);
        var patient = await conn.QuerySingleOrDefaultAsync("""
select id, external_code, display_name, source_system, source_unit
from ged.sector_patient
where tenant_id=@tenantId and source_system=@system and source_unit=@unit and external_code=@code
""", new { tenantId = _currentUser.TenantId, system = result.Patient.SourceSystem, unit = result.Patient.SourceUnit ?? "", code = result.Patient.ExternalCode });
        var encounters = await conn.QueryAsync("""
select e.id, e.external_code, e.raw_kind, e.kind, e.admitted_at, e.discharged_at, e.status
from ged.sector_encounter e
join ged.sector_patient p on p.id=e.patient_id
where e.tenant_id=@tenantId and p.external_code=@code and p.source_system=@system and p.source_unit=@unit
order by e.admitted_at desc nulls last
""", new { tenantId = _currentUser.TenantId, system = result.Patient.SourceSystem, unit = result.Patient.SourceUnit ?? "", code = result.Patient.ExternalCode });
        return Json(new { status = "Found", message = "Paciente localizado na origem.", patient, encounters });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Enviar(Guid patientId, Guid encounterId, string catalogCode, string? sectorCode, DateOnly? documentDate, string? notes, string? issuerName, string? informationOwner, bool confirmAnotherOccurrence, IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null)
            return BadRequest(new { stage = "ARQUIVO", message = "Selecione um arquivo." });
        await using var buffer = new MemoryStream();
        await arquivo.CopyToAsync(buffer, ct);
        var result = await _sectors.RegisterAsync(new SectorRegistrationRequest(
            _currentUser.TenantId, _currentUser.UserId, Admin, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", Request.Headers.UserAgent.ToString(),
            patientId, encounterId, catalogCode, sectorCode, documentDate, notes, issuerName, informationOwner,
            arquivo.FileName, arquivo.ContentType, buffer.ToArray(), confirmAnotherOccurrence), ct);
        return Json(new { stored = result.Stored, stage = result.Stage, message = result.Message, linkId = result.LinkId, documentId = result.DocumentId, versionId = result.VersionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reenfileirar(Guid linkId, CancellationToken ct)
    {
        var result = await _sectors.RetryProcessingAsync(_currentUser.TenantId, _currentUser.UserId, Admin, linkId, Ip(), Ua(), ct);
        return Json(new { stored = result.Stored, stage = result.Stage, message = result.Message, linkId = result.LinkId, documentId = result.DocumentId, versionId = result.VersionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VincularExistente(Guid patientId, Guid encounterId, Guid documentId, string catalogCode, bool confirmAnotherOccurrence, CancellationToken ct)
    {
        var result = await _sectors.LinkExistingAsync(new SectorLinkExistingRequest(
            _currentUser.TenantId, _currentUser.UserId, Admin, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", Request.Headers.UserAgent.ToString(),
            patientId, encounterId, documentId, catalogCode, confirmAnotherOccurrence), ct);
        return Json(new { stored = result.Stored, stage = result.Stage, message = result.Message, linkId = result.LinkId, documentId = result.DocumentId, versionId = result.VersionId });
    }

    [HttpGet]
    public async Task<IActionResult> Arquivo(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Html(401, "Sua sessão expirou. Entre novamente no InovaGed para ver o documento.");
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Consultar, ct))
            return Html(403, "Seu acesso não permite consultar este arquivo.");
        return await StreamVersionAsync(_currentUser.TenantId, versionId, requireGrant: false, ct);
    }

    [HttpGet]
    public async Task<IActionResult> Texto(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Html(401, "Sua sessão expirou. Entre novamente para ler o texto.");
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid DocumentId, string Catalog, string? Text)>(new CommandDefinition($"""
select l.document_id as DocumentId, l.catalog_code as Catalog, coalesce(v.ocr_text, s.ocr_text, v.content_text, '') as Text
from ged.sector_document_link l
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
left join ged.document_search s on s.tenant_id=l.tenant_id and s.version_id=l.version_id
where l.tenant_id=@tenantId and l.version_id=@versionId and l.reg_status='A'
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, versionId }, cancellationToken: ct));
        if (row.DocumentId == Guid.Empty) return Html(404, "O texto deste documento não está disponível para o seu acesso.");
        var visible = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        if (!visible.Contains(row.Catalog, StringComparer.Ordinal)) return Html(403, "Seu setor não consulta este tipo.");
        if (string.IsNullOrWhiteSpace(row.Text))
            return Html(200, "Ainda não há texto desta versão. A leitura pode estar na fila, ter falhado ou não se aplicar a este arquivo. Nada foi inventado.");
        return Html(200, row.Text);
    }

    [HttpGet("/DemaisSetores/Incorporar/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Incorporar(string token, Guid versionId, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!await _sectors.GrantIsActiveAsync(token, versionId, ct))
            return Html(401, "A concessão expirou ou foi revogada. Abra um novo link pelo InovaGed.");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await using var conn = await _db.OpenAsync(ct);
        var tenantId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "select tenant_id from ged.sector_embed_grant where token_hash=@hash and version_id=@versionId and revoked_at is null and expires_at>now()",
            new { hash, versionId }, cancellationToken: ct));
        if (tenantId == Guid.Empty) return Html(401, "A concessão expirou ou foi revogada. Abra um novo link pelo InovaGed.");
        var origins = (await conn.QueryAsync<string>(new CommandDefinition(
            "select origin from ged.sector_embed_origin where tenant_id=@tenantId and reg_status='A'",
            new { tenantId }, cancellationToken: ct))).ToArray();
        Response.Headers.ContentSecurityPolicy = origins.Length == 0 ? "frame-ancestors 'none'" : "frame-ancestors " + string.Join(' ', origins);
        var referer = Request.Headers.Referer.ToString();
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) || origins.All(origin => !string.Equals(origin, refererUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
            return Html(403, "A origem da incorporação não está autorizada.");
        return await StreamVersionAsync(tenantId, versionId, requireGrant: true, ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conferir(Guid linkId, string toStatus, string? justification, CancellationToken ct)
    {
        var error = await _sectors.ReviewAsync(new SectorReviewRequest(_currentUser.TenantId, _currentUser.UserId, Admin, linkId, toStatus, justification, Ip(), Ua()), ct);
        return Page(error is null ? 200 : 400, error ?? "Conferência registrada. A presença do arquivo não comprova assinatura nem aptidão para faturamento.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrigirVinculo(Guid linkId, Guid toEncounterId, string reason, CancellationToken ct)
    {
        var error = await _sectors.CorrectLinkAsync(new SectorCorrectionRequest(_currentUser.TenantId, _currentUser.UserId, Admin, linkId, toEncounterId, reason, Ip(), Ua()), ct);
        return Page(error is null ? 200 : 400, error ?? "Vínculo corrigido e histórico preservado.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pesquisar(Guid encounterId, string term, bool includeOtherEncounters, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Consultar, ct))
            return Forbid();
        var hits = await _sectors.SearchAsync(new SectorSearchRequest(_currentUser.TenantId, _currentUser.UserId, Admin, encounterId, term, includeOtherEncounters), ct);
        return Json(new { mode = "literal", message = "A busca é literal no atendimento selecionado. Análise semântica não foi executada e nenhuma página foi estimada.", hits });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevisarCampo(Guid fieldId, string status, string? value, CancellationToken ct)
    {
        var error = await _sectors.ReviewFieldAsync(new SectorFieldReviewRequest(_currentUser.TenantId, _currentUser.UserId, Admin, fieldId, status, value, Ip(), Ua()), ct);
        return Page(error is null ? 200 : 400, error ?? "Revisão humana registrada. O PDF original não foi alterado.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DecidirAchado(Guid findingId, string status, string? justification, CancellationToken ct)
    {
        var error = await _sectors.ReviewFindingAsync(_currentUser.TenantId, _currentUser.UserId, Admin, findingId, status, justification, Ip(), Ua(), ct);
        return Page(error is null ? 200 : 400, error ?? "Decisão registrada. Nenhum código ou valor de cobrança foi gerado.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EvidenciaFisica(Guid linkId, string evidence, CancellationToken ct)
    {
        var error = await _sectors.RecordPhysicalEvidenceAsync(_currentUser.TenantId, _currentUser.UserId, Admin, linkId, evidence, Ip(), Ua(), ct);
        return Page(error is null ? 200 : 400, error ?? "Evidência registrada. A impressão da cópia continua sem substituir o original.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararEntrega(Guid encounterId, string profileCode, string? recipient, Guid[] linkIds, CancellationToken ct)
    {
        var result = await _sectors.PrepareDeliveryAsync(new SectorDeliveryRequest(_currentUser.TenantId, _currentUser.UserId, Admin, encounterId, profileCode, recipient, linkIds ?? [], Ip(), Ua()), ct);
        return Page(result.Recorded ? 200 : 400, result.Message + (result.Blockers.Count == 0 ? "" : " " + string.Join(" ", result.Blockers)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarIncorporacao(Guid versionId, string origin, CancellationToken ct)
    {
        var result = await _sectors.CreateEmbedAsync(new SectorEmbedRequest(_currentUser.TenantId, _currentUser.UserId, Admin, versionId, origin, Ip(), Ua()), ct);
        return Page(result.Created ? 200 : 400, result.Message + (result.Path is null ? "" : " " + result.Path));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarContrato(string sourceSystem, string rawCode, string kind, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Parametrizar, ct)) return Forbid();
        if (!SectorCatalog.EncounterKinds.Contains(kind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(sourceSystem))
            return Page(400, "Informe o sistema de origem, o código externo e um tipo já confirmado: internação, ambulatorial ou urgência.");
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_kind_contract (tenant_id, source_system, raw_code, kind)
values (@tenantId, @sourceSystem, @rawCode, @kind)
on conflict (tenant_id, source_system, raw_code) do update set kind=excluded.kind
""", new { tenantId = _currentUser.TenantId, sourceSystem = sourceSystem.Trim(), rawCode = rawCode.Trim(), kind }, cancellationToken: ct));
        return Page(200, "Contrato registrado. Códigos externos sem esta linha continuam sem tipo interpretado.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conceder(Guid userId, string sectorCode, string permissionCode, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Administrar, ct)) return Forbid();
        if (!SectorCatalog.SectorCodes.Contains(sectorCode, StringComparer.Ordinal) || !SectorPermissions.All.Contains(permissionCode, StringComparer.Ordinal))
            return Page(400, "Setor ou permissão inválidos. DDI não recebe atribuição automática.");
        await using var conn = await _db.OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.app_user where tenant_id=@tenantId and id=@userId)",
            new { tenantId = _currentUser.TenantId, userId }, cancellationToken: ct));
        if (!exists) return Page(400, "Usuário não encontrado neste tenant.");
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_access (tenant_id, user_id, sector_code, permission_code)
values (@tenantId, @userId, @sectorCode, @permissionCode)
on conflict (tenant_id, user_id, sector_code, permission_code) do update set reg_status='A'
""", new { tenantId = _currentUser.TenantId, userId, sectorCode, permissionCode }, cancellationToken: ct));
        return Page(200, "Acesso concedido só para este módulo e este setor. Ele não abre todos os prontuários.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> HabilitarAnexoIi(bool enabled, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Parametrizar, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_profile set enabled=@enabled, customized=true
where tenant_id=@tenantId and code='ANEXO_II' and version_no = (
    select max(version_no) from ged.sector_profile where tenant_id=@tenantId and code='ANEXO_II')
""", new { tenantId = _currentUser.TenantId, enabled }, cancellationToken: ct));
        return Page(200, enabled ? "Anexo II habilitado como perfil próprio. O Anexo I permanece disponível." : "Anexo II desabilitado.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Origem(string origin, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Parametrizar, ct)) return Forbid();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http")
            return Page(400, "Informe a origem completa, por exemplo https://aplicacao.exemplo.");
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_embed_origin (id, tenant_id, origin)
values (gen_random_uuid(), @tenantId, @origin)
on conflict (tenant_id, origin) do update set reg_status='A'
""", new { tenantId = _currentUser.TenantId, origin = uri.GetLeftPart(UriPartial.Authority) }, cancellationToken: ct));
        return Page(200, "Origem cadastrada. A política global de frame não foi alterada.");
    }

    private async Task<IActionResult> StreamVersionAsync(Guid tenantId, Guid versionId, bool requireGrant, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid DocumentId, string Path, string ContentType, string Catalog)>(new CommandDefinition("""
select l.document_id as DocumentId, v.storage_path as Path, coalesce(v.content_type,'application/octet-stream') as ContentType, l.catalog_code as Catalog
from ged.sector_document_link l
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
where l.tenant_id=@tenantId and l.version_id=@versionId and l.reg_status='A'
""", new { tenantId, versionId }, cancellationToken: ct));
        if (row.DocumentId == Guid.Empty) return Html(404, "O documento não está disponível neste atendimento.");
        if (!requireGrant)
        {
            var visible = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
            if (!visible.Contains(row.Catalog, StringComparer.Ordinal)) return Html(403, "Seu setor não consulta este tipo.");
            if (!await _authorization.CanAccessDocumentAsync(_currentUser.TenantId, _currentUser.UserId, row.DocumentId, "VIEW", new Dictionary<string, string>(), ct))
                return Html(403, "A autorização documental não permite este arquivo.");
        }

        if (string.IsNullOrWhiteSpace(row.Path) || !await _storage.ExistsAsync(row.Path, ct))
            return Html(404, "O arquivo ainda não está disponível para visualização.");
        var stream = await _storage.OpenReadAsync(row.Path, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = "inline";
        return File(stream, row.ContentType, enableRangeProcessing: true);
    }

    private async Task<IReadOnlyList<SectorDocumentRow>> DocumentsAsync(Guid encounterId, CancellationToken ct)
    {
        var codes = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SectorDocumentRow>(new CommandDefinition($"""
select l.id as LinkId, l.document_id as DocumentId, l.version_id as VersionId, t.name as Name, t.catalog_code as CatalogCode,
       l.origin as Origin, l.original_file_name as OriginalFileName, l.document_date as DocumentDate, l.review_status as ReviewStatus,
       l.preview_status as PreviewStatus, l.ocr_status as OcrStatus, l.extraction_status as ExtractionStatus, l.issuer_name as IssuerName,
       l.information_owner as InformationOwner, l.physical_evidence as PhysicalEvidence, l.sector_code as SectorCode,
       t.physical_original as RequiresPhysical
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.reg_status='A' and t.catalog_code = any(@codes)
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
order by t.catalog_order, l.document_date nulls last, l.created_at
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, encounterId, codes = codes.ToArray() }, cancellationToken: ct))).ToArray();
    }

    private async Task<IReadOnlyList<SectorFieldRow>> FieldsAsync(Guid encounterId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var codes = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        return (await conn.QueryAsync<SectorFieldRow>(new CommandDefinition($"""
select f.id as FieldId, f.link_id as LinkId, f.version_id as VersionId, f.context_code as ContextCode, f.suggested_value as SuggestedValue,
       f.confirmed_value as ConfirmedValue, f.evidence_excerpt as Evidence, f.page_number as PageNumber, f.status as Status
from ged.sector_extracted_field f
join ged.sector_document_link l on l.id=f.link_id and l.tenant_id=f.tenant_id
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
where f.tenant_id=@tenantId and l.encounter_id=@encounterId and l.reg_status='A'
  and l.catalog_code = any(@codes)
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
order by f.updated_at desc
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, encounterId, codes = codes.ToArray() }, cancellationToken: ct))).ToArray();
    }

    private async Task<IReadOnlyList<SectorFindingRow>> FindingsAsync(Guid encounterId, CancellationToken ct)
    {
        var codes = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SectorFindingRow>(new CommandDefinition($"""
select f.id as FindingId, f.description as Description, f.category as Category, f.evidence_excerpt as Evidence,
       f.page_number as PageNumber, f.review_status as Status, l.version_id as VersionId
from ged.sector_finding f
join ged.sector_document_link l on l.id=f.link_id and l.tenant_id=f.tenant_id
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
where f.tenant_id=@tenantId and f.encounter_id=@encounterId and l.reg_status='A'
  and l.catalog_code = any(@codes)
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
order by f.updated_at desc
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, encounterId, codes = codes.ToArray() }, cancellationToken: ct))).ToArray();
    }

    private async Task<SectorContextRow?> ContextAsync(Guid encounterId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SectorContextRow>(new CommandDefinition("""
select p.id as PatientId, p.external_code as PatientCode, p.display_name as DisplayName,
       e.external_code as EncounterCode, e.raw_kind as RawKind, e.kind as Kind,
       e.admitted_at as AdmittedAt, e.discharged_at as DischargedAt, e.status as Status
from ged.sector_encounter e
join ged.sector_patient p on p.id=e.patient_id and p.tenant_id=e.tenant_id
where e.tenant_id=@tenantId and e.id=@encounterId
""", new { tenantId = _currentUser.TenantId, encounterId }, cancellationToken: ct));
    }

    private async Task<IReadOnlyList<SectorEncounterOption>> SiblingsAsync(Guid encounterId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SectorEncounterOption>(new CommandDefinition("""
select s.id as Id, s.external_code as Code, s.kind as Kind, s.status as Status
from ged.sector_encounter s
join ged.sector_encounter current on current.tenant_id=s.tenant_id and current.patient_id=s.patient_id
where s.tenant_id=@tenantId and current.id=@encounterId and s.id<>current.id
order by s.admitted_at desc nulls last
""", new { tenantId = _currentUser.TenantId, encounterId }, cancellationToken: ct))).ToArray();
    }

    private async Task<IReadOnlyList<SectorChecklistRow>> ChecklistAsync(Guid encounterId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var encounter = await conn.QuerySingleOrDefaultAsync<(string? Kind, bool Surgery, bool Opme, bool HighCost, DateTimeOffset? Discharged)>(new CommandDefinition(
            "select kind as Kind, surgery as Surgery, opme as Opme, high_cost as HighCost, discharged_at as Discharged from ged.sector_encounter where tenant_id=@tenantId and id=@encounterId",
            new { tenantId = _currentUser.TenantId, encounterId }, cancellationToken: ct));
        var profile = await conn.QuerySingleOrDefaultAsync<(Guid Id, bool Enabled)>(new CommandDefinition(
            "select id as Id, enabled as Enabled from ged.sector_profile where tenant_id=@tenantId and code='ANEXO_I' order by version_no desc limit 1",
            new { tenantId = _currentUser.TenantId }, cancellationToken: ct));
        var types = await conn.QueryAsync<(string Code, string Name, string Applicability, int Order)>(new CommandDefinition(
            "select catalog_code as Code, name as Name, applicability as Applicability, catalog_order as Order from ged.sector_document_type where tenant_id=@tenantId and active order by catalog_order",
            new { tenantId = _currentUser.TenantId }, cancellationToken: ct));
        var received = (await conn.QueryAsync<string>(new CommandDefinition(
            "select distinct catalog_code from ged.sector_document_link where tenant_id=@tenantId and encounter_id=@encounterId and reg_status='A'",
            new { tenantId = _currentUser.TenantId, encounterId }, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
        var conditions = new SectorConditions(encounter.Surgery, encounter.Opme, encounter.HighCost, encounter.Discharged.HasValue);
        return types.Select(type =>
        {
            var required = profile.Id != Guid.Empty && SectorApplicability.IsRequired("ANEXO_I", profile.Enabled, encounter.Kind, type.Applicability, conditions);
            var state = received.Contains(type.Code) ? "recebido" : required ? "pendente" : "não exigido neste contexto";
            return new SectorChecklistRow(type.Order, type.Name, state, required);
        }).ToArray();
    }

    private ContentResult Page(int status, string message) => Html(status, message);

    private ContentResult Html(int status, string message)
    {
        Response.StatusCode = status;
        var text = System.Net.WebUtility.HtmlEncode(message);
        return Content($"<!doctype html><html lang=\"pt-BR\"><meta charset=\"utf-8\"><title>Documento</title><body style=\"font-family:system-ui;background:#f6f7f4;color:#1f2933;padding:24px\"><p>{text}</p></body></html>", "text/html", Encoding.UTF8);
    }

    private string Ip() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string Ua() => Request.Headers.UserAgent.ToString();
}

public sealed record SectorDocumentRow(Guid LinkId, Guid DocumentId, Guid VersionId, string Name, string CatalogCode, string Origin, string OriginalFileName, DateOnly? DocumentDate, string ReviewStatus, string PreviewStatus, string OcrStatus, string ExtractionStatus, string? IssuerName, string? InformationOwner, string? PhysicalEvidence, string? SectorCode, bool RequiresPhysical);
public sealed record SectorFieldRow(Guid FieldId, Guid LinkId, Guid VersionId, string ContextCode, string SuggestedValue, string? ConfirmedValue, string Evidence, int? PageNumber, string Status);
public sealed record SectorFindingRow(Guid FindingId, string Description, string Category, string? Evidence, int? PageNumber, string Status, Guid? VersionId);
public sealed record SectorChecklistRow(int Order, string Name, string State, bool Required);
public sealed record SectorContextRow(Guid PatientId, string PatientCode, string DisplayName, string EncounterCode, string? RawKind, string? Kind, DateTimeOffset? AdmittedAt, DateTimeOffset? DischargedAt, string? Status);
public sealed record SectorEncounterOption(Guid Id, string Code, string? Kind, string? Status);
