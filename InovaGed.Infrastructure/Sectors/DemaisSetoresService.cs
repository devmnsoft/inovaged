using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using Microsoft.Extensions.Logging;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using InovaGed.Application.Security;
using InovaGed.Application.Sectors;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Sectors;

public sealed class UnconfiguredClinicalSource : IClinicalSource
{
    public Task<ClinicalSourceResult> FindAsync(Guid tenantId, string externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct)
        => Task.FromResult(new ClinicalSourceResult(ClinicalSourceStatus.Unavailable, ClinicalSourceMessages.Unavailable, null, []));
}

public sealed class DemaisSetoresService : IDemaisSetoresService
{
    private const int MaxDirectBytes = 20 * 1024 * 1024;
    private readonly IDbConnectionFactory _db;
    private readonly DocumentAppService _documents;
    private readonly IFolderCommands _folders;
    private readonly IOcrJobRepository _ocr;
    private readonly IPreviewJobQueue _previewQueue;
    private readonly IPreviewStatusRepository _previewStatus;
    private readonly IAbacAuthorizationService _authorization;
    private readonly IAuditWriter _audit;
    private readonly IOptions<DocumentUploadOptions> _uploadOptions;
    private readonly IClinicalSource _source;
    private readonly ILogger<DemaisSetoresService> _logger;

    public DemaisSetoresService(
        IDbConnectionFactory db,
        DocumentAppService documents,
        IFolderCommands folders,
        IOcrJobRepository ocr,
        IPreviewJobQueue previewQueue,
        IPreviewStatusRepository previewStatus,
        IAbacAuthorizationService authorization,
        IAuditWriter audit,
        IOptions<DocumentUploadOptions> uploadOptions,
        IClinicalSource source,
        ILogger<DemaisSetoresService> logger)
    {
        _db = db;
        _documents = documents;
        _folders = folders;
        _ocr = ocr;
        _previewQueue = previewQueue;
        _previewStatus = previewStatus;
        _authorization = authorization;
        _audit = audit;
        _uploadOptions = uploadOptions;
        _source = source;
        _logger = logger;
    }

    public async Task<bool> CanEnterAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct)
    {
        if (isFullAdmin) return true;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.sector_access where tenant_id=@tenantId and user_id=@userId and reg_status='A')",
            new { tenantId, userId }, cancellationToken: ct));
    }

    public async Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, bool isFullAdmin, string permission, CancellationToken ct)
    {
        if (isFullAdmin) return true;
        if (!SectorPermissions.All.Contains(permission, StringComparer.Ordinal)) return false;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1 from ged.sector_access
  where tenant_id=@tenantId and user_id=@userId and reg_status='A'
    and permission_code in (@permission, 'ADMINISTRAR'))
""", new { tenantId, userId, permission }, cancellationToken: ct));
    }

    public async Task EnsureTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        foreach (var item in SectorCatalog.All)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_document_type
    (tenant_id, catalog_code, catalog_order, name, applicability, multiple_files, physical_original, view_sectors)
select @tenantId, @code, @ord, @name, @applicability, @multiple, @physical, @views
where not exists (
    select 1 from ged.sector_document_type where tenant_id=@tenantId and catalog_code=@code)
""", new { tenantId, code = item.Code, ord = item.Order, name = item.Name, applicability = item.Applicability, multiple = item.Multiple, physical = item.PhysicalOriginal, views = item.ViewSectors.ToArray() }, cancellationToken: ct));
        }

        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_type_mapping (tenant_id, catalog_code, mapping_status, note)
select tenant_id, catalog_code, 'PENDENTE',
       case when catalog_code='DS19'
            then 'O anexo menciona avaliar a criação do Laudo de Internação no MV. Isso não confirma existência no MV nem exclusividade do GED.'
            else 'Mapeamento pendente. A origem no MV não foi confirmada.' end
from ged.sector_document_type c
where c.tenant_id=@tenantId
  and not exists (select 1 from ged.sector_type_mapping m where m.tenant_id=c.tenant_id and m.catalog_code=c.catalog_code)
""", new { tenantId }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_profile (id, tenant_id, code, version_no, name, enabled)
select gen_random_uuid(), @tenantId, p.code, 1, p.name, p.enabled
from (values
    ('ANEXO_I', 'Organização do prontuário hospitalar — Anexo I', true),
    ('ANEXO_II', 'Composição física — Anexo II', false)
) as p(code, name, enabled)
where not exists (
    select 1 from ged.sector_profile x where x.tenant_id=@tenantId and x.code=p.code and x.version_no=1)
""", new { tenantId }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_profile_item (profile_id, catalog_code, position)
select p.id, c.catalog_code, c.catalog_order
from ged.sector_profile p
join ged.sector_document_type c on c.tenant_id=p.tenant_id
where p.tenant_id=@tenantId and p.code='ANEXO_I' and p.version_no=1
  and not exists (select 1 from ged.sector_profile_item i where i.profile_id=p.id and i.catalog_code=c.catalog_code)
""", new { tenantId }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_profile_item (profile_id, catalog_code, position)
select p.id, v.code, v.pos
from ged.sector_profile p
cross join (values ('DS01',1),('DS11',2),('DS12',3),('DS13',4),('DS14',5),('DS15',6),('DS16',7),('DS17',8)) as v(code, pos)
where p.tenant_id=@tenantId and p.code='ANEXO_II' and p.version_no=1
  and not exists (select 1 from ged.sector_profile_item i where i.profile_id=p.id and i.catalog_code=v.code)
""", new { tenantId }, cancellationToken: ct));
    }

    public async Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct)
    {
        var code = SectorPatientCode.Normalize(externalCode);
        if (code is null)
            return new(ClinicalSourceStatus.Failed, "Informe o código do paciente preservando letras e zeros à esquerda.", null, []);
        var source = await ResolveSource().FindAsync(tenantId, code, sourceSystem, sourceUnit, ct);
        if (source.Status != ClinicalSourceStatus.Found || source.Patient is null) return source;

        await using var conn = await _db.OpenAsync(ct);
        var patientId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.sector_patient (id, tenant_id, source_system, source_unit, external_code, display_name)
values (gen_random_uuid(), @tenantId, @system, @unit, @code, @name)
on conflict (tenant_id, source_system, source_unit, external_code)
do update set display_name=excluded.display_name, updated_at=now()
returning id
""", new { tenantId, system = source.Patient.SourceSystem, unit = source.Patient.SourceUnit ?? "", code = source.Patient.ExternalCode, name = source.Patient.DisplayName }, cancellationToken: ct));

        var contract = (await conn.QueryAsync<(string Raw, string Kind)>(new CommandDefinition(
            "select raw_code as Raw, kind as Kind from ged.sector_kind_contract where tenant_id=@tenantId and source_system=@system",
            new { tenantId, system = source.Patient.SourceSystem }, cancellationToken: ct))).ToDictionary(x => x.Raw, x => x.Kind, StringComparer.Ordinal);

        foreach (var encounter in source.Encounters)
        {
            var kind = SectorKindMap.Resolve(encounter.RawKind, contract);
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_encounter (id, tenant_id, patient_id, external_code, raw_kind, kind, admitted_at, discharged_at, status)
values (gen_random_uuid(), @tenantId, @patientId, @code, @raw, @kind, @admitted, @discharged, @status)
on conflict (tenant_id, patient_id, external_code)
do update set raw_kind=excluded.raw_kind, kind=excluded.kind, admitted_at=excluded.admitted_at,
              discharged_at=excluded.discharged_at, status=excluded.status, updated_at=now()
""", new { tenantId, patientId, code = encounter.ExternalCode, raw = encounter.RawKind, kind, admitted = encounter.AdmittedAt, discharged = encounter.DischargedAt, status = encounter.Status }, cancellationToken: ct));
        }

        return source;
    }

    public async Task<IReadOnlyList<string>> VisibleCatalogCodesAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        if (isFullAdmin || await HasSectorAsync(conn, tenantId, userId, SectorCatalog.Faturamento, ct))
            return (await conn.QueryAsync<string>(new CommandDefinition("select catalog_code from ged.sector_document_type where tenant_id=@tenantId and active order by catalog_order", new { tenantId }, cancellationToken: ct))).ToArray();

        var sectors = (await conn.QueryAsync<string>(new CommandDefinition(
            "select distinct sector_code from ged.sector_access where tenant_id=@tenantId and user_id=@userId and reg_status='A' and permission_code in ('CONSULTAR','ADMINISTRAR','CADASTRAR','CONFERIR')",
            new { tenantId, userId }, cancellationToken: ct))).ToArray();
        var codes = (await conn.QueryAsync<string>(new CommandDefinition("""
select catalog_code from ged.sector_document_type
where tenant_id=@tenantId and active and view_sectors && @sectors::text[]
order by catalog_order
""", new { tenantId, sectors }, cancellationToken: ct))).ToList();
        if (sectors.Contains(SectorCatalog.Selecionado, StringComparer.Ordinal))
        {
            codes.AddRange(await conn.QueryAsync<string>(new CommandDefinition(
                "select catalog_code from ged.sector_type_scope where tenant_id=@tenantId and user_id=@userId",
                new { tenantId, userId }, cancellationToken: ct)));
        }

        return codes.Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task<SectorRegistrationResult> RegisterAsync(SectorRegistrationRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Cadastrar, ct))
            return new(false, "AUTORIZACAO", "Seu acesso não permite cadastrar documentos neste módulo.", null, null, null);
        if (request.Content.Length == 0)
            return new(false, "ARQUIVO", "O arquivo está vazio.", null, null, null);
        if (request.Content.Length > MaxDirectBytes)
            return new(false, "UPLOAD_PARTES", "Este arquivo passa do envio direto. Use o upload em partes do GED e depois vincule o documento confirmado.", null, null, null);

        var type = SectorCatalog.Find(request.CatalogCode);
        if (type is null)
            return new(false, "TIPO", "O nome documental precisa ser escolhido no catálogo.", null, null, null);
        var visible = await VisibleCatalogCodesAsync(request.TenantId, request.UserId, request.IsFullAdmin, ct);
        if (!visible.Contains(type.Code, StringComparer.Ordinal))
            return new(false, "AUTORIZACAO", "Seu setor não cadastra este tipo.", null, null, null);

        var extension = Path.GetExtension(request.OriginalFileName);
        if (!_uploadOptions.Value.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return new(false, "ARQUIVO", "A extensão não é permitida pelo GED.", null, null, null);
        if (!SectorFileRules.ContentMatches(extension, request.Content))
            return new(false, "ARQUIVO", "O conteúdo não corresponde à extensão informada.", null, null, null);

        await using var conn = await _db.OpenAsync(ct);
        var encounterOk = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.sector_encounter where tenant_id=@TenantId and id=@EncounterId and patient_id=@PatientId)",
            request, cancellationToken: ct));
        if (!encounterOk)
            return new(false, "ATENDIMENTO", ClinicalSourceMessages.EncounterMissing, null, null, null);
        var active = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select active from ged.sector_document_type where tenant_id=@TenantId and catalog_code=@CatalogCode",
            request, cancellationToken: ct));
        if (!active)
            return new(false, "TIPO", "Este tipo está inativo.", null, null, null);

        var hash = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant();
        var duplicate = await DuplicateAsync(conn, request.TenantId, request.UserId, request.EncounterId, hash, ct);
        if (duplicate is not null)
            return new(false, "DUPLICIDADE", duplicate, null, null, null);
        if (!type.Multiple && !request.ConfirmAnotherOccurrence)
        {
            var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from ged.sector_document_link where tenant_id=@TenantId and encounter_id=@EncounterId and catalog_code=@CatalogCode and reg_status='A')",
                request, cancellationToken: ct));
            if (exists)
                return new(false, "CONFIRMACAO", "Este tipo não é múltiplo por padrão. Confirme se esta é outra ocorrência e não uma nova versão.", null, null, null);
        }

        var folderId = await EnsureFolderAsync(conn, request.TenantId, request.UserId, ct);
        if (folderId is null)
            return new(false, "PASTA", "Não há pasta do GED para este módulo. Quem administra pode prepará-la na parametrização.", null, null, null);

        await using var stream = new MemoryStream(request.Content, writable: false);
        var uploaded = await _documents.UploadAsync(new UploadDocumentCommand
        {
            FolderId = folderId.Value,
            Title = type.Name,
            Description = $"Nome original do arquivo: {request.OriginalFileName}. Emissor: {request.IssuerName}. Responsável pela informação: {request.InformationOwner}. O usuário do upload não é autor clínico.",
            FileName = request.OriginalFileName,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
            Content = stream,
            IsConfidential = true,
            Visibility = "PRIVATE"
        }, request.Ip, request.UserAgent, ct);
        if (!uploaded.IsSuccess || uploaded.Value == Guid.Empty)
            return new(false, "PERSISTENCIA", uploaded.ErrorMessage, null, null, null);

        var version = await conn.QuerySingleAsync<(Guid VersionId, string StoragePath, string FileName, string? Hash)>(new CommandDefinition("""
select current_version_id as VersionId, v.storage_path as StoragePath, v.file_name as FileName, v.checksum_sha256 as Hash
from ged.document d
join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
where d.tenant_id=@tenantId and d.id=@documentId
""", new { tenantId = request.TenantId, documentId = uploaded.Value }, cancellationToken: ct));

        var linkId = Guid.NewGuid();
        var ocrApplicable = SectorFileRules.OcrExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_document_link
 (id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code, origin, original_file_name, content_sha256,
  document_date, sector_code, notes, issuer_name, information_owner, uploaded_by, review_status, transfer_status, preview_status, ocr_status, extraction_status)
values
 (@linkId, @tenantId, @patientId, @encounterId, @documentId, @versionId, @catalog, 'GED_UPLOAD', @fileName, @hash,
  @documentDate, @sector, @notes, @issuer, @owner, @userId, 'RECEBIDO', 'GRAVADO', 'AGUARDANDO', @ocr, 'AGUARDANDO')
""", new
            {
                linkId, tenantId = request.TenantId, patientId = request.PatientId, encounterId = request.EncounterId, documentId = uploaded.Value,
                versionId = version.VersionId, catalog = type.Code, fileName = request.OriginalFileName, hash = version.Hash ?? hash,
                documentDate = request.DocumentDate, sector = request.SectorCode, notes = request.Notes, issuer = request.IssuerName,
                owner = request.InformationOwner, userId = request.UserId, ocr = ocrApplicable ? "AGUARDANDO" : "NAO_APLICAVEL"
            }, cancellationToken: ct));
            await GrantAclAsync(conn, request.TenantId, uploaded.Value, request.UserId, type, ct);
            await conn.ExecuteAsync(new CommandDefinition(
                "update ged.document set is_confidential=false, updated_at=now(), updated_by=@userId where tenant_id=@tenantId and id=@documentId",
                new { tenantId = request.TenantId, documentId = uploaded.Value, userId = request.UserId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vínculo do módulo não confirmado após a gravação no GED. Tenant={TenantId} Document={DocumentId}", request.TenantId, uploaded.Value);
            await AuditAsync(request.TenantId, request.UserId, "UPDATE", uploaded.Value, "Vínculo do módulo não confirmado após o GED gravar o arquivo.", request.Ip, request.UserAgent, ct);
            return new(false, "VINCULO", "O arquivo foi gravado no GED, mas o vínculo com o atendimento não foi confirmado. Não repita o envio; use o vínculo do documento existente.", linkId, uploaded.Value, version.VersionId);
        }

        var preview = "AGUARDANDO";
        var ocr = ocrApplicable ? "AGUARDANDO" : "NAO_APLICAVEL";
        try
        {
            await _previewStatus.UpsertAsync(request.TenantId, version.VersionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct);
            await _previewQueue.EnqueueAsync(request.TenantId, uploaded.Value, version.VersionId, version.StoragePath, version.FileName, ct);
            preview = "ENFILEIRADO";
        }
        catch
        {
            preview = "FALHOU_AO_ENFILEIRAR";
        }

        if (ocrApplicable)
        {
            try
            {
                await _ocr.EnqueueAsync(request.TenantId, version.VersionId, request.UserId, false, ct);
                ocr = "ENFILEIRADO";
            }
            catch
            {
                ocr = "FALHOU_AO_ENFILEIRAR";
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "update ged.sector_document_link set preview_status=@preview, ocr_status=@ocr where tenant_id=@tenantId and id=@linkId",
            new { preview, ocr, tenantId = request.TenantId, linkId }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "UPLOAD", uploaded.Value, "Documento do módulo vinculado ao atendimento.", request.Ip, request.UserAgent, ct);
        var queueFailed = preview == "FALHOU_AO_ENFILEIRAR" || ocr == "FALHOU_AO_ENFILEIRAR";
        var message = queueFailed
            ? "Documento gravado e vinculado. A fila de pré-visualização ou OCR não foi confirmada. Repita só a fila, sem novo envio."
            : "Documento gravado e vinculado. Pré-visualização e leitura seguem na fila existente; isso não encerra a extração nem a conferência.";
        return new(true, queueFailed ? "FALHOU_AO_ENFILEIRAR" : "GRAVADO", message, linkId, uploaded.Value, version.VersionId);
    }

    public async Task<SectorRegistrationResult> LinkExistingAsync(SectorLinkExistingRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Cadastrar, ct))
            return new(false, "AUTORIZACAO", "Seu acesso não permite vincular documentos.", null, null, null);
        if (!await _authorization.CanAccessDocumentAsync(request.TenantId, request.UserId, request.DocumentId, "VIEW", new Dictionary<string, string>(), ct))
            return new(false, "AUTORIZACAO", "O documento do GED não está disponível para o seu usuário.", null, null, null);
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid VersionId, string FileName, string? Hash, Guid? PatientId)>(new CommandDefinition("""
select d.current_version_id as VersionId, v.file_name as FileName, v.checksum_sha256 as Hash, e.patient_id as PatientId
from ged.document d
join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
left join ged.sector_encounter e on e.tenant_id=@tenantId and e.id=@encounterId
where d.tenant_id=@tenantId and d.id=@documentId and coalesce(d.reg_status,'A')='A'
""", new { tenantId = request.TenantId, documentId = request.DocumentId, encounterId = request.EncounterId }, cancellationToken: ct));
        if (row.VersionId == Guid.Empty || row.PatientId != request.PatientId)
            return new(false, "ATENDIMENTO", ClinicalSourceMessages.EncounterMissing, null, null, null);
        if (string.IsNullOrWhiteSpace(row.Hash))
            return new(false, "ARQUIVO", "A versão do GED não tem hash confirmado. O vínculo não foi criado.", null, null, null);
        var duplicate = await DuplicateAsync(conn, request.TenantId, request.UserId, request.EncounterId, row.Hash, ct);
        if (duplicate is not null)
            return new(false, "DUPLICIDADE", duplicate, null, null, null);
        var type = SectorCatalog.Find(request.CatalogCode);
        if (type is null)
            return new(false, "TIPO", "Escolha um nome do catálogo.", null, null, null);
        var linkId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_document_link
 (id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code, origin, original_file_name, content_sha256,
  uploaded_by, transfer_status, preview_status, ocr_status, extraction_status)
values
 (@linkId, @tenantId, @patientId, @encounterId, @documentId, @versionId, @catalog, 'GED_UPLOAD', @fileName, @hash,
  @userId, 'GRAVADO', 'JA_EXISTENTE', 'AGUARDANDO', 'AGUARDANDO')
""", new { linkId, tenantId = request.TenantId, patientId = request.PatientId, encounterId = request.EncounterId, documentId = request.DocumentId, versionId = row.VersionId, catalog = type.Code, fileName = row.FileName, hash = row.Hash, userId = request.UserId }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "UPDATE", request.DocumentId, "Documento já existente no GED vinculado ao atendimento.", request.Ip, request.UserAgent, ct);
        return new(true, "VINCULADO", "O documento já gravado foi vinculado. A leitura continua na fila existente do GED.", linkId, request.DocumentId, row.VersionId);
    }

    public async Task<string?> ReviewAsync(SectorReviewRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Conferir, ct)
            && !await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Cadastrar, ct))
            return "Seu acesso não permite conferir.";
        await using var conn = await _db.OpenAsync(ct);
        var current = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select review_status from ged.sector_document_link where tenant_id=@TenantId and id=@LinkId and reg_status='A'", request, cancellationToken: ct));
        if (current is null) return "Documento do atendimento não encontrado.";
        var permission = await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Conferir, ct)
            ? SectorPermissions.Conferir
            : SectorPermissions.Cadastrar;
        if (!SectorReview.CanTransition(current, request.ToStatus, request.IsFullAdmin ? SectorPermissions.Administrar : permission, request.Justification, out var impediment))
            return impediment;
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_document_link set review_status=@ToStatus where tenant_id=@TenantId and id=@LinkId;
insert into ged.sector_review_event (id, tenant_id, link_id, from_status, to_status, justification, actor_id)
values (gen_random_uuid(), @TenantId, @LinkId, @current, @ToStatus, @Justification, @UserId);
""", new { request.TenantId, request.LinkId, request.ToStatus, request.Justification, request.UserId, current }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "UPDATE", request.LinkId, "Conferência documental alterada.", request.Ip, request.UserAgent, ct);
        return null;
    }

    public async Task<string?> CorrectLinkAsync(SectorCorrectionRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.CorrigirVinculo, ct))
            return "Seu acesso não permite corrigir o vínculo.";
        if (string.IsNullOrWhiteSpace(request.Reason)) return "A correção exige justificativa.";
        await using var conn = await _db.OpenAsync(ct);
        var owner = await conn.QuerySingleOrDefaultAsync<(Guid EncounterId, Guid PatientId)>(new CommandDefinition("""
select l.encounter_id as EncounterId, e.patient_id as PatientId
from ged.sector_document_link l
join ged.sector_encounter e on e.id=l.encounter_id and e.tenant_id=l.tenant_id
where l.tenant_id=@TenantId and l.id=@LinkId and l.reg_status='A'
""", request, cancellationToken: ct));
        var targetPatient = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select patient_id from ged.sector_encounter where tenant_id=@TenantId and id=@ToEncounterId", request, cancellationToken: ct));
        if (owner.EncounterId == Guid.Empty || targetPatient is null || targetPatient != owner.PatientId)
            return "O atendimento de destino não pertence ao mesmo paciente.";
        if (owner.EncounterId == request.ToEncounterId)
            return "O vínculo já aponta para este atendimento.";
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_document_link set encounter_id=@ToEncounterId where tenant_id=@TenantId and id=@LinkId;
insert into ged.sector_link_correction (id, tenant_id, link_id, from_encounter_id, to_encounter_id, reason, actor_id)
values (gen_random_uuid(), @TenantId, @LinkId, @fromId, @ToEncounterId, @Reason, @UserId);
""", new { request.TenantId, request.LinkId, request.ToEncounterId, request.Reason, request.UserId, fromId = owner.EncounterId }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "UPDATE", request.LinkId, "Vínculo de atendimento corrigido com justificativa.", request.Ip, request.UserAgent, ct);
        return null;
    }

    public async Task<IReadOnlyList<SectorSearchHit>> SearchAsync(SectorSearchRequest request, CancellationToken ct)
    {
        var term = request.Term?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2) return [];
        var codes = await VisibleCatalogCodesAsync(request.TenantId, request.UserId, request.IsFullAdmin, ct);
        await using var conn = await _db.OpenAsync(ct);
        var catalog = await conn.QueryAsync<(string Name, string[] Synonyms)>(new CommandDefinition(
            "select name as Name, synonyms as Synonyms from ged.sector_document_type where tenant_id=@tenantId and catalog_code = any(@codes)",
            new { tenantId = request.TenantId, codes = codes.ToArray() }, cancellationToken: ct));
        var patterns = SectorSearchTerms.Patterns(term, catalog).ToArray();
        var rows = await conn.QueryAsync<(Guid DocumentId, Guid VersionId, string Name, string Text, string EncounterCode, string? OcrStatus)>(new CommandDefinition($"""
select l.document_id as DocumentId, l.version_id as VersionId, t.name as Name,
       coalesce(v.ocr_text, s.ocr_text, v.content_text, '') as Text,
       e.external_code as EncounterCode, oj.status as OcrStatus
from ged.sector_document_link l
join ged.sector_encounter e on e.tenant_id=l.tenant_id and e.id=l.encounter_id
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
left join ged.document_search s on s.tenant_id=l.tenant_id and s.version_id=l.version_id
left join lateral (
    select j.status::text as status from ged.ocr_job j
    where j.tenant_id=l.tenant_id and j.document_version_id=l.version_id
    order by j.requested_at desc limit 1) oj on true
where l.tenant_id=@tenantId and l.reg_status='A' and t.catalog_code = any(@codes)
  and (l.encounter_id=@encounterId or (@include and e.patient_id = (select patient_id from ged.sector_encounter where tenant_id=@tenantId and id=@encounterId)))
  and {InovaGed.Infrastructure.Security.AbacAuthorizationService.DocumentAccessPredicate("d")}
  and coalesce(v.ocr_text, s.ocr_text, v.content_text, '') ilike any(@patterns)
limit 50
""", new { tenantId = request.TenantId, userId = request.UserId, permissionCode = "Documents.View", write = false, hour = DateTime.UtcNow.Hour, encounterId = request.EncounterId, include = request.IncludeOtherEncounters, codes = codes.ToArray(), patterns }, cancellationToken: ct));

        return rows.Select(row =>
        {
            var index = SectorSearchTerms.FirstIndex(row.Text, patterns);
            var start = Math.Max(0, index - 60);
            var excerpt = index < 0 ? "" : row.Text.Substring(start, Math.Min(180, row.Text.Length - start));
            var origin = string.Equals(row.OcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ? "OCR existente" : "texto da versão, origem não separada";
            return new SectorSearchHit(row.DocumentId, row.VersionId, row.Name, excerpt, SectorTextLocator.PageAt(row.Text, index), row.EncounterCode, origin);
        }).ToArray();
    }

    public async Task RefreshExtractionAsync(Guid tenantId, Guid encounterId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(Guid LinkId, Guid VersionId, string Code, string? Text, string? OcrStatus)>(new CommandDefinition("""
select l.id as LinkId, l.version_id as VersionId, l.catalog_code as Code,
       coalesce(v.ocr_text, s.ocr_text, v.content_text) as Text, oj.status as OcrStatus
from ged.sector_document_link l
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
left join ged.document_search s on s.tenant_id=l.tenant_id and s.version_id=l.version_id
left join lateral (
    select j.status::text as status from ged.ocr_job j
    where j.tenant_id=l.tenant_id and j.document_version_id=l.version_id
    order by j.requested_at desc limit 1) oj on true
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.reg_status='A'
""", new { tenantId, encounterId }, cancellationToken: ct));
        foreach (var row in rows)
        {
            var extraction = string.Equals(row.OcrStatus, "FAILED", StringComparison.OrdinalIgnoreCase) ? "FALHOU"
                : string.Equals(row.OcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(row.Text) ? "CONCLUIDO_COM_PENDENCIAS"
                : string.IsNullOrWhiteSpace(row.Text) ? "AGUARDANDO" : "CONCLUIDO";
            await conn.ExecuteAsync(new CommandDefinition(
                "update ged.sector_document_link set extraction_status=@extraction, ocr_status=coalesce(@ocr, ocr_status) where id=@id and tenant_id=@tenantId",
                new { extraction, ocr = row.OcrStatus, id = row.LinkId, tenantId }, cancellationToken: ct));
            foreach (var fact in SectorExtraction.Read(row.Text, row.Code))
            {
                var kept = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(select 1 from ged.sector_extracted_field
 where tenant_id=@tenantId and link_id=@linkId and evidence_excerpt=@excerpt and status in ('CONFIRMADO','CORRIGIDO','REJEITADO','INCONCLUSIVO'))
""", new { tenantId, linkId = row.LinkId, excerpt = fact.Excerpt }, cancellationToken: ct));
                if (kept) continue;
                await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_extracted_field
 (id, tenant_id, link_id, version_id, field_key, context_code, suggested_value, evidence_excerpt, page_number, status)
select gen_random_uuid(), @tenantId, @linkId, @versionId, @key, @context, @value, @excerpt, @page, 'SUGERIDO'
where not exists (
    select 1 from ged.sector_extracted_field
    where tenant_id=@tenantId and link_id=@linkId and evidence_excerpt=@excerpt and status='SUGERIDO')
""", new { tenantId, linkId = row.LinkId, versionId = row.VersionId, key = fact.Category, context = fact.Context, value = fact.Excerpt, excerpt = fact.Excerpt, page = fact.Page }, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_finding (id, tenant_id, encounter_id, link_id, description, category, evidence_excerpt, page_number)
select gen_random_uuid(), @tenantId, @encounterId, @linkId, @description, 'INFORMACAO_LOCALIZADA', @excerpt, @page
where not exists (
    select 1 from ged.sector_finding where tenant_id=@tenantId and link_id=@linkId and evidence_excerpt=@excerpt)
""", new { tenantId, encounterId, linkId = row.LinkId, description = SectorFindings.Describe(fact), excerpt = fact.Excerpt, page = fact.Page }, cancellationToken: ct));
            }
        }
    }

    public async Task<string?> ReviewFieldAsync(SectorFieldReviewRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.RevisarExtracao, ct))
            return "Seu acesso não permite revisar a extração.";
        var allowed = new[] { "CONFIRMADO", "CORRIGIDO", "REJEITADO", "INCONCLUSIVO" };
        if (!allowed.Contains(request.Status, StringComparer.Ordinal)) return "Situação de revisão inválida.";
        if (request.Status == "CORRIGIDO" && string.IsNullOrWhiteSpace(request.Value)) return "A correção precisa do valor revisado.";
        await using var conn = await _db.OpenAsync(ct);
        var current = await conn.QuerySingleOrDefaultAsync<(string Status, string? Confirmed)>(new CommandDefinition(
            "select status as Status, confirmed_value as Confirmed from ged.sector_extracted_field where tenant_id=@TenantId and id=@FieldId",
            request, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(current.Status)) return "Campo não encontrado.";
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_field_review (id, tenant_id, field_id, from_status, to_status, from_value, to_value, actor_id)
values (gen_random_uuid(), @TenantId, @FieldId, @fromStatus, @Status, @fromValue, @Value, @UserId);
update ged.sector_extracted_field
set status=@Status, confirmed_value=coalesce(@Value, confirmed_value), updated_by=@UserId, updated_at=now()
where tenant_id=@TenantId and id=@FieldId
""", new { request.TenantId, request.FieldId, request.Status, request.Value, request.UserId, fromStatus = current.Status, fromValue = current.Confirmed }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "UPDATE", request.FieldId, "Revisão humana de campo extraído.", request.Ip, request.UserAgent, ct);
        return null;
    }

    public async Task<string?> RecordPhysicalEvidenceAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string evidence, string ip, string userAgent, CancellationToken ct)
    {
        if (!await HasPermissionAsync(tenantId, userId, isFullAdmin, SectorPermissions.Conferir, ct))
            return "Seu acesso não permite registrar evidência física.";
        if (string.IsNullOrWhiteSpace(evidence)) return "Descreva a evidência do original físico.";
        await using var conn = await _db.OpenAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "update ged.sector_document_link set physical_evidence=@evidence where tenant_id=@tenantId and id=@linkId and reg_status='A'",
            new { tenantId, linkId, evidence = evidence.Trim() }, cancellationToken: ct));
        if (updated == 0) return "Documento não encontrado.";
        await AuditAsync(tenantId, userId, "UPDATE", linkId, "Evidência de original físico registrada. A cópia digital não substitui o original.", ip, userAgent, ct);
        return null;
    }

    public async Task<string?> ReviewFindingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid findingId, string status, string? justification, string ip, string userAgent, CancellationToken ct)
    {
        if (!await HasPermissionAsync(tenantId, userId, isFullAdmin, SectorPermissions.Conferir, ct))
            return "Seu acesso não permite decidir o achado.";
        var allowed = new[] { "CONFIRMADO", "REJEITADO", "COMPLEMENTACAO", "ENCAMINHADO" };
        if (!allowed.Contains(status, StringComparer.Ordinal)) return "Decisão inválida.";
        if (string.IsNullOrWhiteSpace(justification)) return "A decisão exige justificativa.";
        await using var conn = await _db.OpenAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_finding
set review_status=@status, justification=@justification, actor_id=@userId, updated_at=now()
where tenant_id=@tenantId and id=@findingId
""", new { tenantId, findingId, status, justification, userId }, cancellationToken: ct));
        if (updated == 0) return "Achado não encontrado.";
        await AuditAsync(tenantId, userId, "UPDATE", findingId, "Decisão humana sobre achado de conferência.", ip, userAgent, ct);
        return null;
    }

    public async Task<SectorDeliveryResult> PrepareDeliveryAsync(SectorDeliveryRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Imprimir, ct))
            return new(false, "NEGADO", "Seu acesso não permite preparar a entrega.", []);
        await using var conn = await _db.OpenAsync(ct);
        var profile = await conn.QuerySingleOrDefaultAsync<(Guid Id, bool Enabled)>(new CommandDefinition("""
select id as Id, enabled as Enabled from ged.sector_profile
where tenant_id=@TenantId and code=@ProfileCode
order by version_no desc limit 1
""", request, cancellationToken: ct));
        if (profile.Id == Guid.Empty) return new(false, "PERFIL", "Perfil de checklist não encontrado.", []);
        if (request.ProfileCode == "ANEXO_II" && !profile.Enabled)
            return new(false, "PERFIL", "A composição física do Anexo II não está habilitada e não substitui o prontuário.", []);
        var items = (await conn.QueryAsync<(Guid LinkId, Guid VersionId, string Name, bool Physical, string? Evidence)>(new CommandDefinition("""
select l.id as LinkId, l.version_id as VersionId, t.name as Name, t.physical_original as Physical, l.physical_evidence as Evidence
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.id = any(@ids) and l.reg_status='A'
""", new { tenantId = request.TenantId, encounterId = request.EncounterId, ids = request.LinkIds.ToArray() }, cancellationToken: ct))).ToArray();
        var blockers = SectorDeliveryRules.Blockers(items.Select(item => new SectorDeliveryCandidate(item.Name, item.Physical, !string.IsNullOrWhiteSpace(item.Evidence), false))).ToArray();
        var status = blockers.Length == 0 ? "REGISTRADO" : "AGUARDA_ORIGINAL";
        var deliveryId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_delivery (id, tenant_id, encounter_id, profile_id, status, assembled_by, recipient, notes)
values (@deliveryId, @tenantId, @encounterId, @profileId, @status, @userId, @recipient, @notes)
""", new { deliveryId, tenantId = request.TenantId, encounterId = request.EncounterId, profileId = profile.Id, status, userId = request.UserId, recipient = request.Recipient, notes = SectorFindings.AccountUnavailable }, cancellationToken: ct));
        foreach (var item in items)
        {
            var outcome = item.Physical && string.IsNullOrWhiteSpace(item.Evidence) ? "ORIGINAL_PENDENTE" : "COPIA_DIGITAL";
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_delivery_item (delivery_id, link_id, version_id, outcome, detail)
values (@deliveryId, @linkId, @versionId, @outcome, 'A impressão não afirma preservação de assinatura digital.')
""", new { deliveryId, linkId = item.LinkId, versionId = item.VersionId, outcome }, cancellationToken: ct));
        }

        await AuditAsync(request.TenantId, request.UserId, "REPORT_PRINT", deliveryId, "Preparação de entrega registrada sem encerrar o atendimento.", request.Ip, request.UserAgent, ct);
        var message = blockers.Length == 0
            ? "A preparação foi registrada. A entrega não encerra o atendimento nem altera a temporalidade. Assinatura digital não foi avaliada."
            : "A preparação foi registrada com pendências. O pacote não está completo.";
        return new(true, status, message, blockers);
    }

    public async Task<SectorEmbedResult> CreateEmbedAsync(SectorEmbedRequest request, CancellationToken ct)
    {
        if (!await HasPermissionAsync(request.TenantId, request.UserId, request.IsFullAdmin, SectorPermissions.Compartilhar, ct))
            return new(false, "Seu acesso não permite compartilhar.", null);
        if (!Uri.TryCreate(request.Origin, UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http" || !string.IsNullOrEmpty(uri.Query))
            return new(false, "Informe uma origem http ou https já cadastrada, sem parâmetros.", null);
        var origin = uri.GetLeftPart(UriPartial.Authority);
        await using var conn = await _db.OpenAsync(ct);
        var allowed = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.sector_embed_origin where tenant_id=@TenantId and origin=@origin and reg_status='A')",
            new { request.TenantId, origin }, cancellationToken: ct));
        if (!allowed) return new(false, "A origem não está cadastrada para incorporação.", null);
        if (!await CanReadVersionAsync(conn, request.TenantId, request.UserId, request.VersionId, ct))
            return new(false, "A versão não está disponível.", null);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_embed_grant (id, tenant_id, version_id, token_hash, expires_at, created_by)
values (gen_random_uuid(), @tenantId, @versionId, @hash, now() + interval '15 minutes', @userId)
""", new { tenantId = request.TenantId, versionId = request.VersionId, hash, userId = request.UserId }, cancellationToken: ct));
        await AuditAsync(request.TenantId, request.UserId, "PERMISSION_CHANGE", request.VersionId, "Concessão temporária de incorporação.", request.Ip, request.UserAgent, ct);
        return new(true, "Concessão válida por 15 minutos. A URL não contém paciente nem senha e pode ser revogada.", "/DemaisSetores/Incorporar/" + token);
    }

    public async Task<bool> GrantIsActiveAsync(string token, Guid versionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1 from ged.sector_embed_grant
  where token_hash=@hash and version_id=@versionId and revoked_at is null and expires_at > now())
""", new { hash, versionId }, cancellationToken: ct));
    }

    public async Task RevokeEmbedAsync(Guid tenantId, Guid grantId, Guid actorId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update ged.sector_embed_grant set revoked_at=now() where tenant_id=@tenantId and id=@grantId and revoked_at is null",
            new { tenantId, grantId }, cancellationToken: ct));
        await AuditAsync(tenantId, actorId, "PERMISSION_CHANGE", grantId, "Concessão de incorporação revogada.", null, null, ct);
    }

    private IClinicalSource ResolveSource() => _source;

    public async Task<SectorRegistrationResult> RetryProcessingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string ip, string userAgent, CancellationToken ct)
    {
        if (!await HasPermissionAsync(tenantId, userId, isFullAdmin, SectorPermissions.Cadastrar, ct))
            return new(false, "AUTORIZACAO", "Seu acesso não permite reenfileirar a leitura.", null, null, null);
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid DocumentId, Guid VersionId, string Preview, string Ocr, string Path, string FileName)>(new CommandDefinition("""
select l.document_id as DocumentId, l.version_id as VersionId, l.preview_status as Preview, l.ocr_status as Ocr,
       v.storage_path as Path, v.file_name as FileName
from ged.sector_document_link l
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
where l.tenant_id=@tenantId and l.id=@linkId and l.reg_status='A'
""", new { tenantId, linkId }, cancellationToken: ct));
        if (row.DocumentId == Guid.Empty)
            return new(false, "DOCUMENTO", "O vínculo não foi encontrado. O arquivo não será enviado de novo.", null, null, null);
        var previewFailed = row.Preview == "FALHOU_AO_ENFILEIRAR";
        var ocrFailed = row.Ocr == "FALHOU_AO_ENFILEIRAR";
        if (!previewFailed && !ocrFailed)
            return new(false, "NADA_A_REPETIR", "Não há falha segura de fila. O arquivo original não será reenviado.", row.DocumentId, row.DocumentId, row.VersionId);

        var preview = row.Preview;
        var ocr = row.Ocr;
        if (previewFailed)
        {
            try
            {
                await _previewStatus.UpsertAsync(tenantId, row.VersionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct);
                await _previewQueue.EnqueueAsync(tenantId, row.DocumentId, row.VersionId, row.Path, row.FileName, ct);
                preview = "ENFILEIRADO";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao reenfileirar preview. Tenant={TenantId} Version={VersionId}", tenantId, row.VersionId);
                preview = "FALHOU_AO_ENFILEIRAR";
            }
        }

        if (ocrFailed)
        {
            try
            {
                await _ocr.EnqueueAsync(tenantId, row.VersionId, userId, false, ct);
                ocr = "ENFILEIRADO";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao reenfileirar OCR. Tenant={TenantId} Version={VersionId}", tenantId, row.VersionId);
                ocr = "FALHOU_AO_ENFILEIRAR";
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "update ged.sector_document_link set preview_status=@preview, ocr_status=@ocr where tenant_id=@tenantId and id=@linkId",
            new { preview, ocr, tenantId, linkId }, cancellationToken: ct));
        await AuditAsync(tenantId, userId, "UPDATE", row.DocumentId, "Reenfileiramento de preview ou OCR, sem novo upload.", ip, userAgent, ct);
        var stored = preview != "FALHOU_AO_ENFILEIRAR" && ocr != "FALHOU_AO_ENFILEIRAR";
        return new(stored, stored ? "ENFILEIRADO" : "FALHOU_AO_ENFILEIRAR",
            "A repetição vale só para a fila. Gravação, preview, OCR e extração continuam etapas separadas.",
            linkId, row.DocumentId, row.VersionId);
    }

    private static async Task<bool> HasSectorAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, string sector, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.sector_access where tenant_id=@tenantId and user_id=@userId and sector_code=@sector and reg_status='A' and permission_code in ('CONSULTAR','ADMINISTRAR','CONFERIR'))",
            new { tenantId, userId, sector }, cancellationToken: ct));

    private async Task<string?> DuplicateAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, Guid encounterId, string hash, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<(Guid DocumentId, string Name)?>(new CommandDefinition("""
select l.document_id as DocumentId, t.name as Name
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code
where l.tenant_id=@tenantId and l.encounter_id=@encounterId and l.content_sha256=@hash and l.reg_status='A'
limit 1
""", new { tenantId, encounterId, hash }, cancellationToken: ct));
        if (row is null) return null;
        var canRead = await _authorization.CanAccessDocumentAsync(tenantId, userId, row.Value.DocumentId, "VIEW", new Dictionary<string, string>(), ct);
        return SectorDuplicateNotice.Message(canRead, canRead ? row.Value.Name : null);
    }

    private async Task<Guid?> EnsureFolderAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var existing = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select folder_id from ged.sector_module_setting where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));
        if (existing.HasValue && existing.Value != Guid.Empty) return existing;
        var named = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select id from ged.folder where tenant_id=@tenantId and name='Demais setores' and coalesce(reg_status,'A')='A' limit 1",
            new { tenantId }, cancellationToken: ct));
        if (named.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_module_setting (tenant_id, folder_id) values (@tenantId, @folderId)
on conflict (tenant_id) do update set folder_id=excluded.folder_id, updated_at=now()
""", new { tenantId, folderId = named }, cancellationToken: ct));
            return named;
        }

        var created = await _folders.CreateAsync(tenantId, "Demais setores", null, null, userId, ct);
        if (!created.IsSuccess || created.Value == Guid.Empty) return null;
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_module_setting (tenant_id, folder_id) values (@tenantId, @folderId)
on conflict (tenant_id) do update set folder_id=excluded.folder_id, updated_at=now()
""", new { tenantId, folderId = created.Value }, cancellationToken: ct));
        return created.Value;
    }

    private static async Task GrantAclAsync(NpgsqlConnection conn, Guid tenantId, Guid documentId, Guid uploaderId, SectorCatalogItem type, CancellationToken ct)
    {
        var readers = (await conn.QueryAsync<Guid>(new CommandDefinition("""
select distinct a.user_id
from ged.sector_access a
where a.tenant_id=@tenantId and a.reg_status='A' and a.permission_code in ('CONSULTAR','CADASTRAR','CONFERIR','ADMINISTRAR')
  and (a.sector_code='FATURAMENTO' or a.sector_code=any(@sectors) or a.sector_code='ADMIN')
""", new { tenantId, sectors = type.ViewSectors.ToArray() }, cancellationToken: ct))).ToHashSet();
        readers.Add(uploaderId);
        var admins = await conn.QueryAsync<Guid>(new CommandDefinition("""
select ur.user_id from ged.user_role ur
join ged.app_role r on r.id=ur.role_id and r.tenant_id=@tenantId
where upper(r.normalized_name) in ('ADMIN','ADMINISTRADOR')
""", new { tenantId }, cancellationToken: ct));
        foreach (var admin in admins) readers.Add(admin);
        foreach (var userId in readers)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.document_acl (document_id, user_id, can_read, can_write, created_by)
select @documentId, @userId, true, @write, @uploader
where not exists (select 1 from ged.document_acl where document_id=@documentId and user_id=@userId)
""", new { documentId, userId, write = userId == uploaderId, uploader = uploaderId }, cancellationToken: ct));
        }
    }

    private async Task<bool> CanReadVersionAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, Guid versionId, CancellationToken ct)
    {
        var documentId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select document_id from ged.document_version where tenant_id=@tenantId and id=@versionId",
            new { tenantId, versionId }, cancellationToken: ct));
        return documentId.HasValue && await _authorization.CanAccessDocumentAsync(tenantId, userId, documentId.Value, "VIEW", new Dictionary<string, string>(), ct);
    }

    private async Task AuditAsync(Guid tenantId, Guid userId, string action, Guid entityId, string summary, string? ip, string? userAgent, CancellationToken ct)
    {
        await _audit.WriteAsync(new AuditWriteCommand(tenantId, userId, action, "sector_document", entityId, summary, ip, userAgent, null, null), ct);
    }
}
