using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Application.Sectors;
using InovaGed.Infrastructure.Sectors;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.DemaisSetores)]
public sealed class DemaisSetoresController : Controller
{
    private readonly IDemaisSetoresService _sectors;
    private readonly ISectorOpeningService _opening;
    private readonly IUploadChunkService _chunks;
    private readonly IOptions<DocumentUploadOptions> _uploadOptions;
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IFileStorage _storage;
    private readonly IAbacAuthorizationService _authorization;

    public DemaisSetoresController(IDemaisSetoresService sectors, ISectorOpeningService opening, IUploadChunkService chunks, IOptions<DocumentUploadOptions> uploadOptions, ICurrentUser currentUser, IDbConnectionFactory db, IFileStorage storage, IAbacAuthorizationService authorization)
    {
        _sectors = sectors;
        _opening = opening;
        _chunks = chunks;
        _uploadOptions = uploadOptions;
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
        var codes = await _sectors.VisibleCatalogCodesAsync(_currentUser.TenantId, _currentUser.UserId, Admin, ct);
        ViewBag.Codes = codes;
        ViewBag.Types = await CatalogTypesAsync(codes, ct);
        ViewBag.CanCreate = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Cadastrar, ct);
        ViewBag.MaxMb = _uploadOptions.Value.MaxFileSizeMb;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Consultar(Guid? patientId, Guid? encounterId, string? catalogCode, Guid? documentoId, Guid? versaoId, CancellationToken ct)
    {
        await _sectors.EnsureTenantAsync(_currentUser.TenantId, ct);
        ViewBag.CanReview = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Conferir, ct);
        ViewBag.CanPrint = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Imprimir, ct);
        ViewBag.CanShare = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Compartilhar, ct);
        ViewBag.CanCorrect = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.CorrigirVinculo, ct);
        ViewBag.CanExtract = await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.RevisarExtracao, ct);
        if (encounterId.HasValue)
            await _sectors.RefreshExtractionAsync(_currentUser.TenantId, encounterId.Value, ct);
        var context = encounterId.HasValue ? await ContextAsync(encounterId.Value, ct) : null;
        if (patientId.HasValue && context is not null && context.PatientId != patientId.Value)
            context = null;
        ViewBag.PatientId = patientId;
        ViewBag.EncounterId = context is null ? null : encounterId;
        ViewBag.Context = context;
        ViewBag.CatalogCode = catalogCode;
        ViewBag.Siblings = context is null ? Array.Empty<SectorEncounterOption>() : await SiblingsAsync(encounterId!.Value, ct);
        var documents = context is null ? Array.Empty<SectorDocumentRow>() : await DocumentsAsync(encounterId!.Value, catalogCode, documentoId, versaoId, ct);
        ViewBag.Documents = documents;
        ViewBag.Fields = context is null ? Array.Empty<SectorFieldRow>() : await FieldsAsync(encounterId!.Value, ct);
        ViewBag.Findings = context is null ? Array.Empty<SectorFindingRow>() : await FindingsAsync(encounterId!.Value, ct);
        ViewBag.Checklist = context is null ? Array.Empty<SectorChecklistRow>() : await ChecklistAsync(encounterId!.Value, ct);
        ViewBag.AutoVersion = documents.Length == 1 ? documents[0].VersionId : versaoId;
        ViewBag.Embedded = false;
        ViewBag.FileAction = "Arquivo";
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
    public async Task<IActionResult> BuscarPaciente(string? externalCode, string? sourceSystem, string? sourceUnit, string? identifierKind, CancellationToken ct)
    {
        var kind = string.Equals(identifierKind, "CPF", StringComparison.OrdinalIgnoreCase) ? "CPF" : "ID";
        var result = await _sectors.FindPatientAsync(_currentUser.TenantId, externalCode, sourceSystem, sourceUnit, kind, ct);
        if (result.Status != ClinicalSourceStatus.Found || result.Patient is null)
            return Json(new { status = result.Status.ToString(), message = result.Message, patient = (object?)null, encounters = Array.Empty<object>() });
        await using var conn = await _db.OpenAsync(ct);
        var patient = await conn.QuerySingleOrDefaultAsync("""
select id, external_code, display_name, source_system, source_unit
from ged.sector_patient
where tenant_id=@tenantId and source_system=@system and source_unit=@unit and external_code=@code and identifier_kind=@kind
""", new { tenantId = _currentUser.TenantId, system = result.Patient.SourceSystem, unit = result.Patient.SourceUnit ?? "", code = result.Patient.ExternalCode, kind });
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
    public async Task<IActionResult> IniciarEnvio(Guid patientId, Guid encounterId, string catalogCode, string fileName, string? contentType, long totalBytes, CancellationToken ct)
    {
        var ticket = await _sectors.PrepareUploadAsync(_currentUser.TenantId, _currentUser.UserId, Admin, patientId, encounterId, catalogCode, fileName, totalBytes, ct);
        if (!ticket.Allowed || ticket.FolderId is null)
            return Json(new { stored = false, stage = "AUTORIZACAO", message = ticket.Message, maxBytes = ticket.MaxBytes });
        var extension = Path.GetExtension(fileName);
        var ocrApplicable = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        var configured = Math.Max(1, _uploadOptions.Value.ChunkSizeMb) * 1024 * 1024;
        var chunk = Math.Min(8 * 1024 * 1024, configured);
        var started = await _chunks.StartAsync(_currentUser.TenantId, _currentUser.UserId, false, User.Identity?.Name, new StartUploadChunkRequestDto
        {
            FolderId = ticket.FolderId,
            OriginalFileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            TotalSizeBytes = totalBytes,
            ChunkSizeBytes = chunk,
            RunOcr = ocrApplicable,
            GeneratePreview = true
        }, ct);
        if (!started.Success || started.Value is null)
            return Json(new { stored = false, stage = "TRANSFERENCIA", message = started.Error?.Message ?? "A transferência em partes não começou." });
        return Json(new { stored = false, stage = "TRANSFERENCIA", message = "Envio em partes iniciado. O documento ainda não foi vinculado.", uploadId = started.Value.UploadId, chunkSize = started.Value.ChunkSizeBytes, totalChunks = started.Value.TotalChunks, ocrApplicable, maxBytes = ticket.MaxBytes });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 20_971_520)]
    [RequestSizeLimit(20_971_520)]
    public async Task<IActionResult> ParteEnvio(Guid uploadId, int chunkIndex, IFormFile? parte, CancellationToken ct)
    {
        if (parte is null || parte.Length <= 0 || parte.Length > 20 * 1024 * 1024)
            return Json(new { stored = false, stage = "TRANSFERENCIA", message = "A parte precisa ter até 20 MB." });
        await using var stream = parte.OpenReadStream();
        var saved = await _chunks.SavePartAsync(_currentUser.TenantId, _currentUser.UserId, new UploadChunkPartRequestDto { UploadId = uploadId, ChunkIndex = chunkIndex, Content = stream, SizeBytes = parte.Length }, ct);
        if (!saved.Success || saved.Value is null)
            return Json(new { stored = false, stage = "TRANSFERENCIA", message = saved.Error?.Message ?? "A parte não foi aceita." });
        return Json(new { stored = false, stage = "TRANSFERENCIA", message = "Parte recebida.", percent = saved.Value.Percent, received = saved.Value.ReceivedChunksCount, total = saved.Value.TotalChunks, missing = saved.Value.MissingChunks });
    }

    [HttpGet]
    public async Task<IActionResult> SituacaoEnvio(Guid uploadId, CancellationToken ct)
    {
        var status = await _chunks.GetStatusAsync(_currentUser.TenantId, _currentUser.UserId, uploadId, ct);
        if (!status.Success || status.Value is null)
            return Json(new { stored = false, stage = "TRANSFERENCIA", message = status.Error?.Message ?? "O envio não foi encontrado." });
        return Json(new { stored = status.Value.Status == "COMPLETED", stage = "TRANSFERENCIA", message = "Situação da transferência.", status = status.Value.Status, percent = status.Value.Percent, missing = status.Value.MissingChunks, documentId = status.Value.DocumentId, versionId = status.Value.VersionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarEnvio(Guid uploadId, CancellationToken ct)
    {
        var cancelled = await _chunks.CancelAsync(_currentUser.TenantId, _currentUser.UserId, uploadId, ct);
        return Json(new { stored = false, stage = "CANCELADO", message = cancelled.Success ? "A transferência foi cancelada. Nenhum vínculo foi criado." : cancelled.Error?.Message ?? "O cancelamento não foi confirmado." });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirEnvio(Guid patientId, Guid encounterId, string catalogCode, Guid uploadId, bool confirmAnotherOccurrence, CancellationToken ct)
    {
        var completed = await _chunks.CompleteAsync(_currentUser.TenantId, _currentUser.UserId, uploadId, ct);
        if (!completed.Success || completed.Value?.DocumentId is null || completed.Value.VersionId is null)
            return Json(new { stored = false, stage = "TRANSFERENCIA", message = completed.Error?.Message ?? "A transferência não foi concluída. O vínculo não foi criado." });
        var extension = Path.GetExtension(completed.Value.FileName ?? "");
        var ocrApplicable = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        var attached = await _sectors.AttachCompletedUploadAsync(new SectorAttachUploadRequest(
            _currentUser.TenantId, _currentUser.UserId, Admin, Ip(), Ua(), patientId, encounterId, catalogCode, uploadId,
            completed.Value.DocumentId.Value, completed.Value.VersionId.Value, confirmAnotherOccurrence,
            completed.Value.PreviewQueued, completed.Value.OcrQueued, ocrApplicable), ct);
        var detail = attached.Stored
            ? "Documento gravado. Pré-visualização: " + (completed.Value.PreviewQueued ? "na fila" : "não enfileirada") + ". Leitura: " + (!ocrApplicable ? "não se aplica" : completed.Value.OcrQueued ? "na fila" : "não enfileirada") + ". Extração: aguardando o texto."
            : attached.Message;
        return Json(new { stored = attached.Stored, stage = attached.Stage, message = detail, linkId = attached.LinkId, documentId = attached.DocumentId, versionId = attached.VersionId, previewQueued = completed.Value.PreviewQueued, ocrQueued = completed.Value.OcrQueued });
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
        Response.Headers.CacheControl = "private, no-store";
        if (versionId == Guid.Empty)
            return Html(400, "O link de incorporação está incompleto. Peça um novo link.");
        var grant = await _sectors.ReadGrantAsync(token, versionId, ct);
        if (grant is null || !grant.Active || grant.DocumentId == Guid.Empty)
            return Html(401, "A concessão expirou, foi revogada ou não corresponde a esta versão.");
        var presented = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(presented)) presented = Request.Headers.Referer.ToString();
        if (!SectorEmbedLink.OriginMatches(grant.Origin, presented))
            return Html(403, "A origem da incorporação não corresponde à concessão.");
        Response.Headers.ContentSecurityPolicy = "frame-ancestors " + (string.IsNullOrWhiteSpace(grant.Origin) ? "'none'" : grant.Origin);
        return await StreamVersionAsync(grant.TenantId, versionId, requireGrant: true, ct);
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
    public async Task<IActionResult> RevogarAcesso(Guid userId, string sectorCode, CancellationToken ct)
    {
        var error = await _sectors.RevokeAccessAsync(_currentUser.TenantId, _currentUser.UserId, Admin, userId, sectorCode, ct);
        return Page(error is null ? 200 : 400, error ?? "Acesso do setor revogado. Autorizações antigas desse setor deixam de valer.");
    }

    [HttpGet]
    public async Task<IActionResult> Parceiro(CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Administrar, ct))
            return Forbid();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Parceiro(string clientId, string secret, string identifierKind, string identifier, string? sourceSystem, string? sourceUnit, string encounterCode, string? documentName, string? documentCode, string mode, string origin, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Administrar, ct))
            return Forbid();
        var opened = await _opening.OpenAsync(new SectorOpeningRequest(clientId, secret, identifierKind, identifier, sourceSystem, sourceUnit, encounterCode, documentName, documentCode, null, null, mode, origin), ct);
        ViewBag.Code = opened.Code;
        ViewBag.Message = opened.Message;
        ViewBag.PageUrl = opened.PageUrl;
        ViewBag.FrameUrl = opened.FrameUrl;
        ViewBag.Scope = opened.Scope;
        ViewBag.Correlation = opened.CorrelationId;
        ViewBag.Expires = opened.ExpiresAt;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CadastrarIntegracao(string clientId, string secret, string sourceSystem, string? sourceUnit, string origins, string? catalogCodes, bool allowEncounterList, int maxMinutes, CancellationToken ct)
    {
        if (!await _sectors.HasPermissionAsync(_currentUser.TenantId, _currentUser.UserId, Admin, SectorPermissions.Administrar, ct))
            return Forbid();
        var error = await _opening.RegisterIntegrationAsync(new SectorIntegrationRegistration(
            _currentUser.TenantId, _currentUser.UserId, clientId, secret, sourceSystem, sourceUnit,
            SplitList(origins), SplitList(catalogCodes), allowEncounterList, maxMinutes), ct);
        return Page(error is null ? 200 : 400, error ?? "Integração gravada. A chave ficou só no cadastro protegido e não volta a ser exibida.");
    }

    [HttpGet("/DemaisSetores/Abrir/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> Abrir(string code, CancellationToken ct)
    {
        var consumed = await _opening.ConsumeAsync(code, ct);
        if (consumed is null)
            return Html(401, "A abertura expirou, já foi usada ou foi revogada. Solicite um novo link ao sistema de origem.");
        Response.Cookies.Append(SectorOpeningService.CookieName, consumed.CookieValue, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = Request.IsHttps,
            SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            MaxAge = consumed.ExpiresAt - DateTimeOffset.UtcNow
        });
        return RedirectToAction(nameof(ConsultaIntegrada));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ConsultaIntegrada(CancellationToken ct)
    {
        var session = await _opening.ReadSessionAsync(Request.Cookies[SectorOpeningService.CookieName], ct);
        if (session is null)
            return Html(401, "A sessão da consulta expirou. Abra novamente pelo sistema de origem.");
        Response.Headers.ContentSecurityPolicy = "frame-ancestors " + session.Origin;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "private, no-store";
        var documents = await SessionDocumentsAsync(session, ct);
        ViewBag.Embedded = true;
        ViewBag.PartnerOrigin = session.Origin;
        ViewBag.CanReview = false;
        ViewBag.CanPrint = false;
        ViewBag.CanShare = false;
        ViewBag.CanCorrect = false;
        ViewBag.CanExtract = false;
        ViewBag.PatientId = session.PatientId;
        ViewBag.EncounterId = session.EncounterId;
        ViewBag.Context = await SessionContextAsync(session, ct);
        ViewBag.Siblings = Array.Empty<SectorEncounterOption>();
        ViewBag.Documents = documents;
        ViewBag.Fields = Array.Empty<SectorFieldRow>();
        ViewBag.Findings = Array.Empty<SectorFindingRow>();
        ViewBag.Checklist = Array.Empty<SectorChecklistRow>();
        ViewBag.AutoVersion = session.VersionId ?? (documents.Length == 1 ? documents[0].VersionId : (Guid?)null);
        ViewBag.FileAction = "ArquivoIntegrado";
        ViewBag.CatalogCode = session.CatalogCode;
        return View("Consultar");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ArquivoIntegrado(Guid versionId, CancellationToken ct)
    {
        var session = await _opening.ReadSessionAsync(Request.Cookies[SectorOpeningService.CookieName], ct);
        if (session is null)
            return Html(401, "A sessão da consulta expirou. Abra novamente pelo sistema de origem.");
        Response.Headers.ContentSecurityPolicy = "frame-ancestors " + session.Origin;
        Response.Headers.CacheControl = "private, no-store";
        if (!await SessionAllowsVersionAsync(session, versionId, ct))
            return Html(403, "Esta sessão não inclui o documento pedido.");
        return await StreamVersionAsync(session.TenantId, versionId, requireGrant: true, ct);
    }

    [HttpPost, AllowAnonymous, IgnoreAntiforgeryToken]
    public async Task<IActionResult> TrocarAtendimento(string encounterCode, CancellationToken ct)
    {
        var cookie = Request.Cookies[SectorOpeningService.CookieName];
        var changed = await _opening.SwitchEncounterAsync(cookie, encounterCode, ct);
        if (!changed) return Html(403, "A troca de atendimento não foi autorizada.");
        return RedirectToAction(nameof(ConsultaIntegrada));
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
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A'
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

    private async Task<SectorDocumentRow[]> DocumentsAsync(Guid encounterId, string? catalogCode, Guid? documentId, Guid? versionId, CancellationToken ct)
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
  and (@catalogCode is null or l.catalog_code=@catalogCode)
  and (@documentId is null or l.document_id=@documentId)
  and (@versionId is null or l.version_id=@versionId)
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
order by t.catalog_order, l.document_date nulls last, l.created_at
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, encounterId, codes = codes.ToArray(), catalogCode = string.IsNullOrWhiteSpace(catalogCode) ? null : catalogCode, documentId, versionId }, cancellationToken: ct))).ToArray();
    }

    private async Task<IReadOnlyList<SectorCatalogChoice>> CatalogTypesAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<SectorCatalogChoice>(new CommandDefinition("""
select catalog_code as Code, name as Name, catalog_order as Order
from ged.sector_document_type
where tenant_id=@tenantId and active and catalog_code = any(@codes)
order by catalog_order
""", new { tenantId = _currentUser.TenantId, codes = codes.ToArray() }, cancellationToken: ct));
        return rows.ToArray();
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

    private async Task<SectorDocumentRow[]> SessionDocumentsAsync(SectorSessionAccess session, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SectorDocumentRow>(new CommandDefinition("""
select l.id as LinkId, l.document_id as DocumentId, l.version_id as VersionId, t.name as Name, t.catalog_code as CatalogCode,
       l.origin as Origin, l.original_file_name as OriginalFileName, l.document_date as DocumentDate, l.review_status as ReviewStatus,
       l.preview_status as PreviewStatus, l.ocr_status as OcrStatus, l.extraction_status as ExtractionStatus, l.issuer_name as IssuerName,
       l.information_owner as InformationOwner, l.physical_evidence as PhysicalEvidence, l.sector_code as SectorCode,
       t.physical_original as RequiresPhysical
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A'
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.patient_id=@patientId and l.reg_status='A'
  and (@catalog is null or l.catalog_code=@catalog)
  and (@documentId is null or l.document_id=@documentId)
  and (@versionId is null or l.version_id=@versionId)
order by t.catalog_order, l.created_at
""", new { tenantId = session.TenantId, encounterId = session.EncounterId, patientId = session.PatientId, catalog = session.CatalogCode, documentId = session.DocumentId, versionId = session.Mode == "documento" ? session.VersionId : null }, cancellationToken: ct))).ToArray();
    }

    private async Task<bool> SessionAllowsVersionAsync(SectorSessionAccess session, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1 from ged.sector_document_link l
  join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A'
  where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.patient_id=@patientId and l.version_id=@versionId and l.reg_status='A'
    and (@catalog is null or l.catalog_code=@catalog)
    and (@documentId is null or l.document_id=@documentId)
    and (@lockedVersion is null or l.version_id=@lockedVersion))
""", new { tenantId = session.TenantId, encounterId = session.EncounterId, patientId = session.PatientId, versionId, catalog = session.CatalogCode, documentId = session.DocumentId, lockedVersion = session.Mode == "documento" ? session.VersionId : null }, cancellationToken: ct));
    }

    private async Task<SectorContextRow?> SessionContextAsync(SectorSessionAccess session, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SectorContextRow>(new CommandDefinition("""
select p.id as PatientId, p.external_code as PatientCode, p.display_name as DisplayName,
       e.external_code as EncounterCode, e.raw_kind as RawKind, e.kind as Kind,
       e.admitted_at as AdmittedAt, e.discharged_at as DischargedAt, e.status as Status
from ged.sector_encounter e
join ged.sector_patient p on p.id=e.patient_id and p.tenant_id=e.tenant_id
where e.tenant_id=@tenantId and e.id=@encounterId and e.patient_id=@patientId
""", new { tenantId = session.TenantId, encounterId = session.EncounterId, patientId = session.PatientId }, cancellationToken: ct));
    }

    private static string[] SplitList(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string Ip() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string Ua() => Request.Headers.UserAgent.ToString();
}

public sealed record SectorCatalogChoice(string Code, string Name, int Order);

public sealed record SectorDocumentRow(Guid LinkId, Guid DocumentId, Guid VersionId, string Name, string CatalogCode, string Origin, string OriginalFileName, DateOnly? DocumentDate, string ReviewStatus, string PreviewStatus, string OcrStatus, string ExtractionStatus, string? IssuerName, string? InformationOwner, string? PhysicalEvidence, string? SectorCode, bool RequiresPhysical);
public sealed record SectorFieldRow(Guid FieldId, Guid LinkId, Guid VersionId, string ContextCode, string SuggestedValue, string? ConfirmedValue, string Evidence, int? PageNumber, string Status);
public sealed record SectorFindingRow(Guid FindingId, string Description, string Category, string? Evidence, int? PageNumber, string Status, Guid? VersionId);
public sealed record SectorChecklistRow(int Order, string Name, string State, bool Required);
public sealed record SectorContextRow(Guid PatientId, string PatientCode, string DisplayName, string EncounterCode, string? RawKind, string? Kind, DateTimeOffset? AdmittedAt, DateTimeOffset? DischargedAt, string? Status);
public sealed record SectorEncounterOption(Guid Id, string Code, string? Kind, string? Status);
