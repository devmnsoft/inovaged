using System.Data;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using InovaGed.Infrastructure.Sql;
using Npgsql;
using System.Text;

namespace InovaGed.Infrastructure.Ged.Protocols;

public sealed class ProtocolRequestService : IProtocolService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<ProtocolRequestService> _logger;
    private readonly IProtocolAccessService _access;

    public ProtocolRequestService(IDbConnectionFactory db, IAuditWriter audit, ILogger<ProtocolRequestService> logger, IProtocolAccessService access)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
        _access = access;
    }

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, ProtocolRequestCreateVm vm, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
        if (userId == Guid.Empty) return Result<Guid>.Fail("USER", "Usuário inválido.");
        if (string.IsNullOrWhiteSpace(vm.Title)) return Result<Guid>.Fail("TITLE", "Informe o título da solicitação.");

        var docIds = (vm.DocumentIds ?? new()).Where(x => x != Guid.Empty).Distinct().ToList();
        var manualItems = (vm.ManualItems ?? new()).Where(x => !string.IsNullOrWhiteSpace(x.Description) || !string.IsNullOrWhiteSpace(x.ReferenceCode)).ToList();
        if (docIds.Count == 0 && manualItems.Count == 0 && vm.PendingAttachmentsCount <= 0 && string.IsNullOrWhiteSpace(vm.Description))
            return Result<Guid>.Fail("ITEM", "Adicione um documento digitalizado, informe um documento físico/manual ou anexe um arquivo para abrir o protocolo.");

        try
        {
            var requesterSector = await _access.ResolveOperationalSectorAsync(tenantId, userId, ct);
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var correlationId = Guid.NewGuid().ToString("N");
            string? assignedName = null;
            if (vm.AssignedSectorId.HasValue && vm.AssignedSectorId != Guid.Empty)
            {
                assignedName = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select nome from ged.protocolo_setor
where tenant_id=@TenantId and id=@Id and reg_status='A' and ativo=true;
""", new { TenantId = tenantId, Id = vm.AssignedSectorId }, tx, cancellationToken: ct));
                if (assignedName is null)
                {
                    await tx.RollbackAsync(ct);
                    return Result<Guid>.Fail("SECTOR", "O setor informado não existe, está inativo ou pertence a outro tenant.");
                }
            }

            var protocolNo = await GenerateProtocolNoAsync(conn, tx, tenantId, ct);
            var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.protocol_request
(id, tenant_id, protocol_no, requester_user_id, requester_name, requester_sector_id, requester_sector_name, assigned_sector_id, assigned_sector_name, title, description, priority, status, due_at, requested_at, updated_at, correlation_id, reg_status)
select gen_random_uuid(), @TenantId, @ProtocolNo, @UserId,
       coalesce(u.name, u.email, @UserId::text), @RequesterSectorId, @RequesterSectorName,
       @AssignedSectorId, @AssignedSectorName, @Title, @Description, @Priority, 'REQUESTED', @DueAt, now(), now(), @CorrelationId, 'A'
from (select 1) seed
left join ged.app_user u on u.tenant_id=@TenantId and u.id=@UserId
returning id;
""", new { TenantId = tenantId, UserId = userId, ProtocolNo = protocolNo, RequesterSectorId = requesterSector.Id, RequesterSectorName = requesterSector.Name, AssignedSectorId = assignedName is null ? null : vm.AssignedSectorId, AssignedSectorName = assignedName, Title = vm.Title.Trim(), Description = Trim(vm.Description), Priority = NormalizePriority(vm.Priority), DueAt = vm.DueAt?.ToUniversalTime(), CorrelationId = correlationId }, tx, cancellationToken: ct));

            const string itemSql = """
insert into ged.protocol_request_item
(id, tenant_id, protocol_request_id, document_id, document_version_id, is_manual, reference_code, description, document_type, patient_name, medical_record_number, box_code, physical_location, notes, created_at, reg_status)
values (gen_random_uuid(), @TenantId, @ProtocolId, @DocumentId, @DocumentVersionId, @IsManual, @ReferenceCode, @Description, @DocumentType, @PatientName, @MedicalRecordNumber, @BoxCode, @PhysicalLocation, @Notes, now(), 'A');
""";
            foreach (var docId in docIds)
            {
                var versionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("select current_version_id from ged.document where tenant_id=@TenantId and id=@DocumentId and coalesce(reg_status,'A')='A'", new { TenantId = tenantId, DocumentId = docId }, tx, cancellationToken: ct));
                var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.document where tenant_id=@TenantId and id=@DocumentId and coalesce(reg_status,'A')='A')", new { TenantId = tenantId, DocumentId = docId }, tx, cancellationToken: ct));
                if (!exists)
                {
                    await tx.RollbackAsync(ct);
                    return Result<Guid>.Fail("DOCUMENT", "Um dos documentos não pertence a este tenant ou não está ativo.");
                }
                await conn.ExecuteAsync(new CommandDefinition(itemSql, new { TenantId = tenantId, ProtocolId = id, DocumentId = (Guid?)docId, DocumentVersionId = versionId, IsManual = false, ReferenceCode = (string?)null, Description = (string?)null, DocumentType = (string?)null, PatientName = (string?)null, MedicalRecordNumber = (string?)null, BoxCode = (string?)null, PhysicalLocation = (string?)null, Notes = (string?)null }, tx, cancellationToken: ct));
            }
            foreach (var item in manualItems)
            {
                await conn.ExecuteAsync(new CommandDefinition(itemSql, new { TenantId = tenantId, ProtocolId = id, DocumentId = (Guid?)null, DocumentVersionId = (Guid?)null, IsManual = true, ReferenceCode = Trim(item.ReferenceCode), Description = Trim(item.Description), DocumentType = Trim(item.DocumentType), PatientName = Trim(item.PatientName), MedicalRecordNumber = Trim(item.MedicalRecordNumber), BoxCode = Trim(item.BoxCode), PhysicalLocation = Trim(item.PhysicalLocation), Notes = Trim(item.Notes) }, tx, cancellationToken: ct));
            }
            await WriteHistoryAsync(conn, tx, tenantId, id, null, ProtocolStatuses.Requested, "PROTOCOL_CREATED", userId, Trim(vm.Description), null, correlationId, ct);
            await tx.CommitAsync(ct);

            await AuditAsync(tenantId, userId, "PROTOCOL_CREATED", id, "Solicitação de protocolo criada", new { protocolNo, id, correlationId }, ct);
            return Result<Guid>.Ok(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar protocolo. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("PROTOCOL_CREATE", "Erro ao criar solicitação de protocolo.");
        }
    }

    public Task<Result> AssumeAsync(Guid tenantId, Guid id, Guid userId, string? notes, CancellationToken ct) => AssignOrTransitionAsync(tenantId, id, userId, ProtocolStatuses.InReview, "PROTOCOL_ASSIGNED", notes, null, assign: true, ct);
    public Task<Result> ApproveAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct) => RequireReasonTransitionAsync(tenantId, id, userId, ProtocolStatuses.Approved, "PROTOCOL_APPROVED", reason, internalNotes, ct);
    public Task<Result> ReturnForAdjustmentAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct) => RequireReasonTransitionAsync(tenantId, id, userId, ProtocolStatuses.ReturnedForAdjustment, "PROTOCOL_RETURNED_FOR_ADJUSTMENT", reason, internalNotes, ct);
    public Task<Result> RejectAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct) => RequireReasonTransitionAsync(tenantId, id, userId, ProtocolStatuses.Rejected, "PROTOCOL_REJECTED", reason, internalNotes, ct);
    public Task<Result> FinishAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct) => RequireReasonTransitionAsync(tenantId, id, userId, ProtocolStatuses.Finished, "PROTOCOL_FINISHED", reason, internalNotes, ct, finished: true);
    public Task<Result> RespondAdjustmentAsync(Guid tenantId, Guid id, Guid userId, string response, CancellationToken ct) => RequireReasonTransitionAsync(tenantId, id, userId, ProtocolStatuses.AdjustmentAnswered, "PROTOCOL_ADJUSTMENT_ANSWERED", response, null, ct);

    public async Task<Result> ForwardAsync(Guid tenantId, Guid userId, ProtocolForwardCommand command, CancellationToken ct)
    {
        if (command.ProtocolId == Guid.Empty || command.DestinationSectorId == Guid.Empty)
            return Result.Fail("DESTINATION", "Informe o setor de destino.");
        if (string.IsNullOrWhiteSpace(command.Reason)) return Result.Fail("REASON", "Informe o motivo do encaminhamento.");

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var destinationName = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select nome from ged.protocolo_setor where tenant_id=@TenantId and id=@Id and reg_status='A' and ativo=true;
""", new { TenantId = tenantId, Id = command.DestinationSectorId }, tx, cancellationToken: ct));
            if (destinationName is null) { await tx.RollbackAsync(ct); return Result.Fail("SECTOR", "O setor de destino não existe, está inativo ou pertence a outro tenant."); }
            if (command.ResponsibleUserId.HasValue)
            {
                var responsibleOk = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1 from ged.protocolo_usuario_setor us
  join ged.app_user u on u.tenant_id=us.tenant_id and u.id=us.usuario_id
  where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.setor_id=@SectorId
    and us.reg_status='A' and us.ativo=true and u.is_active=true and u.deleted_at_utc is null);
""", new { TenantId = tenantId, UserId = command.ResponsibleUserId, SectorId = command.DestinationSectorId }, tx, cancellationToken: ct));
                if (!responsibleOk) { await tx.RollbackAsync(ct); return Result.Fail("USER", "O responsável não está ativo no setor de destino."); }
            }

            var current = await conn.QuerySingleOrDefaultAsync<(string Status, Guid? SectorId)>(new CommandDefinition("""
select status as Status, coalesce(assigned_sector_id, requester_sector_id) as SectorId
from ged.protocol_request where tenant_id=@TenantId and id=@Id and reg_status='A' for update;
""", new { TenantId = tenantId, Id = command.ProtocolId }, tx, cancellationToken: ct));
            if (current.Status is null) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "Protocolo não encontrado."); }
            if (ProtocolProcessTransitions.IsTerminal(current.Status)) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "Processo encerrado. Reabra com justificativa antes de encaminhar."); }

            var scope = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(select 1 from ged.protocolo_usuario_setor us
 where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.setor_id=@SectorId and us.reg_status='A' and us.ativo=true and us.pode_tramitar=true);
""", new { TenantId = tenantId, UserId = userId, SectorId = current.SectorId }, tx, cancellationToken: ct));
            var isRequester = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.protocol_request where tenant_id=@TenantId and id=@Id and requester_user_id=@UserId)", new { TenantId = tenantId, Id = command.ProtocolId, UserId = userId }, tx, cancellationToken: ct));
            if (!command.IsAdmin && !scope && !isRequester)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("AUTH", "Você não tem direito de encaminhar a custódia atual.");
            }

            var active = await conn.QuerySingleOrDefaultAsync<(Guid Id, Guid DestinationId, string Status)>(new CommandDefinition("""
select id as Id, destination_sector_id as DestinationId, status as Status
from ged.protocol_tramitation
where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' and item_id is null
  and status in ('PENDING_RECEIPT','RETURN_PENDING')
for update;
""", new { TenantId = tenantId, Id = command.ProtocolId }, tx, cancellationToken: ct));
            if (active.Id != Guid.Empty)
            {
                if (active.DestinationId == command.DestinationSectorId)
                {
                    await tx.CommitAsync(ct);
                    return Result.Ok();
                }
                await tx.RollbackAsync(ct);
                return Result.Fail("CONFLICT", "Já existe uma movimentação ativa para outra destinação. Receba, devolva ou estorne a pendência antes de um novo encaminhamento.");
            }

            var newStatus = string.Equals(current.Status, ProtocolStatuses.Requested, StringComparison.OrdinalIgnoreCase) ? ProtocolStatuses.InReview : current.Status;
            var movementId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.protocol_tramitation
 (id, tenant_id, protocol_request_id, origin_sector_id, origin_sector_name, destination_sector_id, destination_sector_name,
  responsible_user_id, forwarded_by, reason, status, forwarded_at, reg_status)
select gen_random_uuid(), @TenantId, p.id, p.assigned_sector_id, p.assigned_sector_name, @DestinationSectorId, @DestinationSectorName,
  @ResponsibleUserId, @UserId, @Reason, 'PENDING_RECEIPT', now(), 'A'
from ged.protocol_request p
where p.tenant_id=@TenantId and p.id=@ProtocolId
returning id;
""", new { TenantId = tenantId, command.ProtocolId, command.DestinationSectorId, DestinationSectorName = destinationName, command.ResponsibleUserId, UserId = userId, Reason = command.Reason.Trim() }, tx, cancellationToken: ct));
            if (!string.Equals(newStatus, current.Status, StringComparison.OrdinalIgnoreCase))
            {
                await conn.ExecuteAsync(new CommandDefinition("update ged.protocol_request set status=@Status, updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Old", new { TenantId = tenantId, Id = command.ProtocolId, Status = newStatus, Old = current.Status }, tx, cancellationToken: ct));
            }
            await WriteHistoryAsync(conn, tx, tenantId, command.ProtocolId, current.Status, newStatus, "PROTOCOL_FORWARDED", userId, command.Reason, $"Destino: {destinationName}", Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, "PROTOCOL_FORWARDED", command.ProtocolId, "Protocolo encaminhado sem transferência fictícia de custódia", new { movementId, command.DestinationSectorId }, ct);
            return Result.Ok();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result.Fail("CONFLICT", "Já existe uma movimentação ativa incompatível ou este comando já foi processado.");
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao encaminhar protocolo {Id}", command.ProtocolId); return Result.Fail("FORWARD", "Não foi possível encaminhar o protocolo."); }
    }

    public Task<Result> ReceiveAsync(Guid tenantId, Guid id, Guid userId, CancellationToken ct)
        => ReceiveMovementAsync(tenantId, id, null, userId, false, ct);

    public async Task<Result> ReceiveMovementAsync(Guid tenantId, Guid protocolId, Guid? movementId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var result = await ReceiveCoreAsync(conn, tx, tenantId, protocolId, movementId, userId, isAdmin, "PENDING_RECEIPT", "RECEIVED", false, ct);
            if (!result.IsSuccess) { await tx.RollbackAsync(ct); return result; }
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, "PROTOCOL_RECEIVED", protocolId, "Tramitação recebida", new { protocolId, movementId }, ct);
            return result;
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao receber protocolo {Id}", protocolId); return Result.Fail("RECEIVE", "Não foi possível receber a tramitação."); }
    }

    public async Task<Result> AddAttachmentAsync(Guid tenantId, Guid id, Guid userId, string fileName, string? contentType, long sizeBytes, string storagePath, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_request_attachment
(id, tenant_id, protocol_request_id, file_name, content_type, size_bytes, storage_path, uploaded_by, uploaded_by_name, uploaded_at, reg_status, storage_state)
select gen_random_uuid(), @TenantId, @Id, @FileName, @ContentType, @SizeBytes, @StoragePath, @UserId, coalesce(u.name, u.email, @UserId::text), now(), 'A', 'STORED'
from (select 1) seed left join ged.app_user u on u.tenant_id=@TenantId and u.id=@UserId
where exists(select 1 from ged.protocol_request p where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A');
""", new { TenantId = tenantId, Id = id, FileName = Path.GetFileName(fileName), ContentType = contentType, SizeBytes = sizeBytes, StoragePath = storagePath, UserId = userId }, tx, cancellationToken: ct));
            if (rows == 0) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
            await WriteHistoryAsync(conn, tx, tenantId, id, null, null, "PROTOCOL_ATTACHMENT_ADDED", userId, fileName, null, Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, "PROTOCOL_ATTACHMENT_ADDED", id, "Anexo adicionado ao protocolo", new { fileName, sizeBytes }, ct);
            return Result.Ok();
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao anexar protocolo {Id}", id); return Result.Fail("ATTACHMENT", "Erro ao anexar arquivo."); }
    }

    public async Task<Result<Guid>> CreateLoanAsync(Guid tenantId, Guid id, Guid userId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var loanId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
select id from ged.loan_request
where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A'
  and status::text in ('REQUESTED','APPROVED','DELIVERED','OVERDUE')
for update;
""", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (loanId.HasValue)
            {
                await tx.CommitAsync(ct);
                return Result<Guid>.Ok(loanId.Value);
            }
            var created = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
insert into ged.loan_request(id, tenant_id, protocol_no, status, requester_id, requester_name, requester_sector, requested_at, due_at, protocol_request_id, reg_status, request_no)
select gen_random_uuid(), p.tenant_id, n.protocol_no, 'REQUESTED', p.requester_user_id, p.requester_name, p.requester_sector_name, now(), coalesce(p.due_at, now() + interval '7 days'), p.id, 'A',
       'REQ-' || to_char(now(),'YYYYMMDD') || '-' || lpad(n.protocol_no::text, 6, '0')
from ged.protocol_request p
cross join lateral (select ged.next_loan_protocol_no(@TenantId) as protocol_no) n
where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A'
returning id;
""", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (created is null)
            {
                await tx.RollbackAsync(ct);
                return Result<Guid>.Fail("NOTFOUND", "Protocolo não encontrado para gerar o empréstimo.");
            }
            await WriteHistoryAsync(conn, tx, tenantId, id, null, null, "PROTOCOL_LOAN_CREATED", userId, "Solicitação de empréstimo/documento gerada", null, Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, "PROTOCOL_LOAN_CREATED", id, "Loan vinculado ao protocolo", new { loanId = created }, ct);
            return Result<Guid>.Ok(created.Value);
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao gerar loan do protocolo {Id}", id); return Result<Guid>.Fail("LOAN", "Erro ao gerar solicitação de empréstimo/documento."); }
    }

    public async Task<IReadOnlyList<ProtocolRequestRowVm>> ListMyAsync(Guid tenantId, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, CancellationToken ct)
    {
        filter ??= new();
        var finalSql = BuildListMySql(userId, scope, filter, out var parameters);
        parameters.Add("TenantId", tenantId, DbType.Guid);
        ValidateGeneratedSql(finalSql, parameters);

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<ProtocolRequestRowVm>(new CommandDefinition(finalSql, parameters, cancellationToken: ct));
            return rows.ToList();
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Protocol ListMyAsync query failed. Tenant={TenantId} User={UserId} Sql={Sql}", tenantId, userId, finalSql);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProtocolRequestRowVm>> ListWorkQueueAsync(Guid tenantId, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, CancellationToken ct)
    {
        filter ??= new();
        var finalSql = BuildListWorkQueueSql(userId, scope, filter, out var parameters);
        parameters.Add("TenantId", tenantId, DbType.Guid);
        ValidateGeneratedSql(finalSql, parameters);

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<ProtocolRequestRowVm>(new CommandDefinition(finalSql, parameters, cancellationToken: ct));
            return rows.ToList();
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Protocol ListWorkQueueAsync query failed. Tenant={TenantId} User={UserId} Sql={Sql}", tenantId, userId, finalSql);
            throw;
        }
    }

    public async Task<ProtocolRequestDetailsVm?> GetDetailsAsync(Guid tenantId, Guid id, Guid userId, ProtocolVisibilityScope scope, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var detailsSql = new StringBuilder(BaseProtocolRequestListSql);
        detailsSql.AppendLine("and p.id = @Id");
        var finalSql = detailsSql.ToString();
        ValidateGeneratedSql(finalSql);
        var header = await conn.QuerySingleOrDefaultAsync<ProtocolRequestRowVm>(new CommandDefinition(finalSql, new { TenantId = tenantId, Id = id }, cancellationToken: ct));
        if (header is null) return null;
        var desc = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select description from ged.protocol_request where tenant_id=@TenantId and id=@Id", new { TenantId = tenantId, Id = id }, cancellationToken: ct));
        var items = (await conn.QueryAsync<ProtocolItemVm>(new CommandDefinition("""
select i.id, i.document_id as DocumentId, i.document_version_id as DocumentVersionId, i.is_manual as IsManual, i.reference_code as ReferenceCode,
       coalesce(i.description, d.title, i.reference_code) as Description, coalesce(dt.name, i.document_type) as DocumentType,
       i.patient_name as PatientName, i.medical_record_number as MedicalRecordNumber, i.box_code as BoxCode, i.physical_location as PhysicalLocation, i.notes as Notes,
       d.code as DocumentCode, d.title as DocumentTitle, coalesce(oj.status::text, case when ds.ocr_text is not null and length(ds.ocr_text)>0 then 'DONE' end, 'PENDING') as OcrStatus,
       coalesce(dt.name, i.document_type) as Classification, dv.partial_part_number as PartialPartNumber, dv.partial_total_parts as PartialTotalParts,
       (ds.ocr_text is not null and length(ds.ocr_text)>0) as HasOcr
from ged.protocol_request_item i
left join ged.document d on d.tenant_id=i.tenant_id and d.id=i.document_id
left join ged.document_version dv on dv.tenant_id=i.tenant_id and dv.id=coalesce(i.document_version_id, d.current_version_id)
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
left join ged.document_search ds on ds.tenant_id=i.tenant_id and ds.document_id=i.document_id
left join lateral (select status from ged.ocr_job j where j.tenant_id=i.tenant_id and j.document_version_id=coalesce(i.document_version_id, d.current_version_id) order by requested_at desc limit 1) oj on true
where i.tenant_id=@TenantId and i.protocol_request_id=@Id and i.reg_status='A' order by i.created_at;
""", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        var attachments = (await conn.QueryAsync<ProtocolAttachmentVm>(new CommandDefinition("select id, file_name as FileName, content_type as ContentType, size_bytes as SizeBytes, uploaded_by_name as UploadedByName, uploaded_at as UploadedAt, storage_state as StorageState, failure_stage as FailureStage, failure_reason as FailureReason, correlation_id as CorrelationId, attempt_count as AttemptCount from ged.protocol_request_attachment where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by uploaded_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        var pendingMoves = (await conn.QueryAsync<(Guid Id, string Status)>(new CommandDefinition("select id as Id, status as Status from ged.protocol_tramitation where tenant_id=@TenantId and protocol_request_id=@ProtocolId and reg_status='A' and status in ('PENDING_RECEIPT','RETURN_PENDING') order by forwarded_at", new { TenantId = tenantId, ProtocolId = id }, cancellationToken: ct))).ToList();
        var history = (await conn.QueryAsync<ProtocolHistoryVm>(new CommandDefinition("select created_at as CreatedAt, action as Action, old_status as OldStatus, new_status as NewStatus, user_name as UserName, sector_name as SectorName, reason as Reason, internal_notes as InternalNotes from ged.protocol_request_history where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by created_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        var loans = (await conn.QueryAsync<ProtocolLoanVm>(new CommandDefinition("select id, protocol_no as ProtocolNo, status::text as Status from ged.loan_request where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by requested_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        return new ProtocolRequestDetailsVm
        {
            Header = header,
            Description = desc,
            Items = items,
            Attachments = attachments,
            History = history,
            Loans = loans,
            PendingMovementId = pendingMoves.Count == 1 ? pendingMoves[0].Id : null,
            PendingMovementStatus = pendingMoves.Count == 1 ? pendingMoves[0].Status : pendingMoves.Count > 1 ? "MULTIPLE" : null
        };
    }

    public async Task<IReadOnlyList<ProtocolDocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string q, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProtocolDocumentPickDto>(new CommandDefinition("""
select d.id, d.current_version_id as CurrentVersionId, coalesce(d.code,'') as Code, coalesce(d.title,'Documento') as Title, d.status::text as Status,
       coalesce(oj.status::text, case when ds.ocr_text is not null and length(ds.ocr_text)>0 then 'DONE' end, 'PENDING') as OcrStatus,
       dt.name as Classification, (ds.ocr_text is not null and length(ds.ocr_text)>0) as HasOcr
from ged.document d
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
left join lateral (select status from ged.ocr_job j where j.tenant_id=d.tenant_id and j.document_version_id=d.current_version_id order by requested_at desc limit 1) oj on true
where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A'
  and (@Q='' or d.title ilike '%'||@Q||'%' or coalesce(d.code,'') ilike '%'||@Q||'%')
order by d.created_at desc limit 20;
""", new { TenantId = tenantId, Q = (q ?? string.Empty).Trim() }, cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<Result> RequireReasonTransitionAsync(Guid tenantId, Guid id, Guid userId, string newStatus, string action, string reason, string? internalNotes, CancellationToken ct, bool finished = false)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("REASON", "Informe a justificativa/parecer obrigatório.");
        return await AssignOrTransitionAsync(tenantId, id, userId, newStatus, action, reason, internalNotes, assign: false, ct, finished);
    }

    private async Task<Result> AssignOrTransitionAsync(Guid tenantId, Guid id, Guid userId, string newStatus, string action, string? reason, string? internalNotes, bool assign, CancellationToken ct, bool finished = false)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            // for update serializa submissão dupla/concorrente sobre o mesmo protocolo.
            var oldStatus = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select status from ged.protocol_request where tenant_id=@TenantId and id=@Id and reg_status='A' for update", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (oldStatus is null) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
            // Idempotência: auto-transição (mesmo estado destino) é no-op — sem UPDATE e sem linha de histórico duplicada.
            if (string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                _logger.LogInformation("Transição de protocolo {Id} ignorada (já está em {Status}). Ação={Action}", id, oldStatus, action);
                return Result.Ok();
            }
            if (!ProtocolProcessTransitions.Can(oldStatus, newStatus))
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("STATE", $"A transição de {ProtocolStatuses.Label(oldStatus)} para {ProtocolStatuses.Label(newStatus)} não é permitida.");
            }
            var rows = await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocol_request p
set status=@NewStatus, updated_at=now(), finished_at=case when @Finished then now() else finished_at end,
    assigned_user_id=case when @Assign then @UserId else assigned_user_id end,
    assigned_user_name=case when @Assign then coalesce(u.name, u.email, @UserId::text) else assigned_user_name end
from ged.app_user u
where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A' and u.tenant_id=@TenantId and u.id=@UserId;
""", new { TenantId = tenantId, Id = id, UserId = userId, NewStatus = newStatus, Assign = assign, Finished = finished }, tx, cancellationToken: ct));
            if (rows == 0) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
            await WriteHistoryAsync(conn, tx, tenantId, id, oldStatus, newStatus, action, userId, reason, internalNotes, Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, action, id, "Ação operacional em protocolo", new { oldStatus, newStatus, reason }, ct);
            return Result.Ok();
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro em transição de protocolo {Id}", id); return Result.Fail("PROTOCOL_TRANSITION", "Erro ao atualizar protocolo."); }
    }

    public async Task<Result> ReturnCustodyAsync(Guid tenantId, Guid id, Guid userId, bool isAdmin, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("REASON", "Informe o motivo da devolução.");
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var current = await conn.QuerySingleOrDefaultAsync<(string Status, Guid? SectorId)>(new CommandDefinition("select status as Status, assigned_sector_id as SectorId from ged.protocol_request where tenant_id=@TenantId and id=@Id and reg_status='A' for update", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (current.Status is null) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
            if (ProtocolProcessTransitions.IsTerminal(current.Status)) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "Processo encerrado. A devolução física não se confunde com a devolução para ajuste."); }
            var pending = await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from ged.protocol_tramitation where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' and status in ('PENDING_RECEIPT','RETURN_PENDING')", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (pending > 0) { await tx.RollbackAsync(ct); return Result.Fail("CONFLICT", "Já existe movimentação pendente."); }
            var allowed = isAdmin || await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.protocolo_usuario_setor where tenant_id=@TenantId and usuario_id=@UserId and setor_id=@SectorId and reg_status='A' and ativo=true and pode_tramitar=true)", new { TenantId = tenantId, UserId = userId, SectorId = current.SectorId }, tx, cancellationToken: ct));
            if (!allowed) { await tx.RollbackAsync(ct); return Result.Fail("AUTH", "Sem direito de devolver esta custódia."); }
            var origin = await conn.QuerySingleOrDefaultAsync<(Guid? Id, string? Name)>(new CommandDefinition("select origin_sector_id as Id, origin_sector_name as Name from ged.protocol_tramitation where tenant_id=@TenantId and protocol_request_id=@ProtocolId and status='RECEIVED' and reg_status='A' order by received_at desc limit 1", new { TenantId = tenantId, ProtocolId = id }, tx, cancellationToken: ct));
            if (!origin.Id.HasValue) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "Não há recebimento anterior para devolver."); }
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_tramitation
 (id, tenant_id, protocol_request_id, origin_sector_id, destination_sector_id, destination_sector_name, forwarded_by, reason, status, forwarded_at, reg_status)
select gen_random_uuid(), @TenantId, @Id, assigned_sector_id, @DestinationId, @DestinationName, @UserId, @Reason, 'RETURN_PENDING', now(), 'A'
from ged.protocol_request where tenant_id=@TenantId and id=@Id;
""", new { TenantId = tenantId, Id = id, DestinationId = origin.Id, DestinationName = origin.Name, UserId = userId, Reason = reason.Trim() }, tx, cancellationToken: ct));
            await WriteHistoryAsync(conn, tx, tenantId, id, current.Status, current.Status, "PROTOCOL_RETURN_PENDING", userId, reason, "Devolução de custódia, distinta de devolução para ajuste.", Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao devolver protocolo {Id}", id); return Result.Fail("RETURN", "Não foi possível registrar a devolução."); }
    }

    public Task<Result> ConfirmReturnAsync(Guid tenantId, Guid id, Guid? movementId, Guid userId, bool isAdmin, CancellationToken ct)
        => ReceiveLikeAsync(tenantId, id, movementId, userId, isAdmin, "RETURN_PENDING", "RETURN_CONFIRMED", true, ct);

    public async Task<Result> ReverseLastAsync(Guid tenantId, Guid id, Guid userId, bool isAdmin, string justification, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(justification)) return Result.Fail("REASON", "Informe a justificativa do estorno.");
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var last = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Status, Guid? Origin, Guid? ReceivedBy)>(new CommandDefinition("""
select id as Id, status as Status, origin_sector_id as Origin, received_by as ReceivedBy
from ged.protocol_tramitation
where tenant_id=@TenantId and protocol_request_id=@ProtocolId and reg_status='A'
order by forwarded_at desc limit 1 for update;
""", new { TenantId = tenantId, ProtocolId = id }, tx, cancellationToken: ct));
            if (last.Id == Guid.Empty) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "Não há movimentação para estornar."); }
            if (last.Status is "RECEIVED" or "RETURN_CONFIRMED")
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("STATE", "A última movimentação já foi recebida. Use a devolução; a entrega anterior permanece no histórico.");
            }
            if (last.Status == "REVERSED") { await tx.CommitAsync(ct); return Result.Ok(); }
            if (last.Status is not ("PENDING_RECEIPT" or "RETURN_PENDING"))
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("STATE", "A última movimentação não pode ser estornada.");
            }
            var allowed = isAdmin || await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.protocolo_usuario_setor where tenant_id=@TenantId and usuario_id=@UserId and setor_id=@SectorId and reg_status='A' and ativo=true and pode_tramitar=true)", new { TenantId = tenantId, UserId = userId, SectorId = last.Origin }, tx, cancellationToken: ct));
            if (!allowed) { await tx.RollbackAsync(ct); return Result.Fail("AUTH", "Sem direito de estornar esta movimentação."); }
            await conn.ExecuteAsync(new CommandDefinition("update ged.protocol_tramitation set status='REVERSED', reversal_reason=@Reason, updated_at=now() where tenant_id=@TenantId and id=@Id", new { TenantId = tenantId, last.Id, Reason = justification.Trim() }, tx, cancellationToken: ct));
            await WriteHistoryAsync(conn, tx, tenantId, id, null, null, "PROTOCOL_REVERSED", userId, justification, $"Movimento {last.Id}", Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao estornar protocolo {Id}", id); return Result.Fail("REVERSE", "Não foi possível estornar a movimentação."); }
    }

    public async Task<Result> ReopenAsync(Guid tenantId, Guid id, Guid userId, bool isAdmin, string justification, CancellationToken ct)
    {
        if (!isAdmin) return Result.Fail("AUTH", "Reabertura exige autorização administrativa.");
        if (string.IsNullOrWhiteSpace(justification)) return Result.Fail("REASON", "Informe a justificativa da reabertura.");
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var status = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select status from ged.protocol_request where tenant_id=@TenantId and id=@Id and reg_status='A' for update", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (status is null) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
            if (!ProtocolProcessTransitions.CanReopen(status)) { await tx.RollbackAsync(ct); return Result.Fail("STATE", "O processo não está encerrado."); }
            await conn.ExecuteAsync(new CommandDefinition("update ged.protocol_request set status='IN_REVIEW', finished_at=null, updated_at=now() where tenant_id=@TenantId and id=@Id", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            await WriteHistoryAsync(conn, tx, tenantId, id, status, ProtocolStatuses.InReview, "PROTOCOL_REOPENED", userId, justification, null, Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao reabrir protocolo {Id}", id); return Result.Fail("REOPEN", "Não foi possível reabrir o protocolo."); }
    }

    public async Task<ProtocolBatchOutcome> ReceiveManyAsync(Guid tenantId, Guid userId, bool isAdmin, IReadOnlyList<Guid> protocolIds, CancellationToken ct)
    {
        var items = new List<ProtocolBatchItemOutcome>();
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var id in protocolIds.Where(x => x != Guid.Empty).Distinct().OrderBy(x => x))
        {
            var result = await ReceiveCoreAsync(conn, tx, tenantId, id, null, userId, isAdmin, "PENDING_RECEIPT", "RECEIVED", false, ct);
            items.Add(new ProtocolBatchItemOutcome(id, result.IsSuccess, result.IsSuccess ? "Recebido." : result.ErrorMessage));
        }
        if (items.Any(i => i.Applied)) await tx.CommitAsync(ct);
        else await tx.RollbackAsync(ct);
        var applied = items.Count(i => i.Applied);
        var message = applied == 0 ? "Nenhum item foi recebido." : applied == items.Count ? "Lote recebido." : $"Recebimento parcial: {applied} de {items.Count}.";
        return new ProtocolBatchOutcome(applied > 0, applied > 0 && applied < items.Count, items, message);
    }

    public async Task<Result> RecordAttachmentFailureAsync(Guid tenantId, Guid id, Guid userId, string fileName, string? contentType, long sizeBytes, string stage, string reason, string correlationId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocol_request_attachment
set failure_stage=@Stage, failure_reason=@Reason, attempt_count=attempt_count+1, storage_state='FAILED', uploaded_at=now()
where tenant_id=@TenantId and protocol_request_id=@Id and correlation_id=@CorrelationId and reg_status='A';
""", new { TenantId = tenantId, Id = id, Stage = stage, Reason = reason, CorrelationId = correlationId }, cancellationToken: ct));
            if (updated == 0)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_request_attachment
(id, tenant_id, protocol_request_id, file_name, content_type, size_bytes, storage_path, uploaded_by, uploaded_at, reg_status, storage_state, failure_stage, failure_reason, correlation_id, attempt_count)
select gen_random_uuid(), @TenantId, @Id, @FileName, @ContentType, @SizeBytes, '', @UserId, now(), 'A', 'FAILED', @Stage, @Reason, @CorrelationId, 1
where exists(select 1 from ged.protocol_request p where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A');
""", new { TenantId = tenantId, Id = id, FileName = Path.GetFileName(fileName), ContentType = contentType, SizeBytes = sizeBytes, UserId = userId, Stage = stage, Reason = reason, CorrelationId = correlationId }, cancellationToken: ct));
            }
            _logger.LogWarning("Anexo de protocolo {Protocol} falhou na etapa {Stage}. Correlation={Correlation}", id, stage, correlationId);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao persistir pendência de anexo {Correlation}", correlationId);
            return Result.Fail("ATTACHMENT", "Não foi possível registrar a pendência do anexo. Atendimento: " + correlationId);
        }
    }

    private async Task<Result> ReceiveLikeAsync(Guid tenantId, Guid protocolId, Guid? movementId, Guid userId, bool isAdmin, string expected, string target, bool isReturn, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var result = await ReceiveCoreAsync(conn, tx, tenantId, protocolId, movementId, userId, isAdmin, expected, target, isReturn, ct);
            if (!result.IsSuccess) { await tx.RollbackAsync(ct); return result; }
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro na movimentação do protocolo {Id}", protocolId); return Result.Fail("MOVEMENT", "Não foi possível concluir a movimentação."); }
    }

    private async Task<Result> ReceiveCoreAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid protocolId, Guid? movementId, Guid userId, bool isAdmin, string expected, string target, bool isReturn, CancellationToken ct)
    {
        if (!movementId.HasValue || movementId == Guid.Empty)
        {
            var ids = (await conn.QueryAsync<Guid>(new CommandDefinition("""
select t.id
from ged.protocol_tramitation t
where t.tenant_id=@TenantId and t.protocol_request_id=@ProtocolId and t.reg_status='A' and t.status=@Expected
  and (@Admin or t.destination_sector_id in (
      select us.setor_id from ged.protocolo_usuario_setor us
      where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.reg_status='A' and us.ativo=true and us.pode_receber=true))
order by t.forwarded_at;
""", new { TenantId = tenantId, ProtocolId = protocolId, Expected = expected, Admin = isAdmin, UserId = userId }, tx, cancellationToken: ct))).ToList();
            if (ids.Count == 0) return Result.Fail("STATE", "Não há movimentação pendente identificada para o seu setor.");
            if (ids.Count > 1) return Result.Fail("STATE", "Há mais de uma movimentação pendente. Selecione qual transferência deseja receber.");
            movementId = ids[0];
        }

        var row = await conn.QuerySingleOrDefaultAsync<(Guid Id, Guid ProtocolId, string Status, Guid DestinationId, string DestinationName, Guid? ReceivedBy)>(new CommandDefinition("""
select id as Id, protocol_request_id as ProtocolId, status as Status, destination_sector_id as DestinationId, destination_sector_name as DestinationName, received_by as ReceivedBy
from ged.protocol_tramitation
where tenant_id=@TenantId and id=@Id and reg_status='A'
for update;
""", new { TenantId = tenantId, Id = movementId }, tx, cancellationToken: ct));
        if (row.Id == Guid.Empty) return Result.Fail("NOTFOUND", "Movimentação não encontrada neste tenant.");
        if (row.ProtocolId != protocolId) return Result.Fail("STATE", "A movimentação não pertence ao protocolo informado.");
        if (string.Equals(row.Status, target, StringComparison.OrdinalIgnoreCase) && row.ReceivedBy == userId) return Result.Ok();
        if (string.Equals(row.Status, target, StringComparison.OrdinalIgnoreCase)) return Result.Fail("CONFLICT", "Esta movimentação já foi confirmada por outro usuário.");
        if (!string.Equals(row.Status, expected, StringComparison.OrdinalIgnoreCase)) return Result.Fail("STATE", "A movimentação identificada não está pendente para esta ação.");
        if (!isAdmin)
        {
            var can = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.protocolo_usuario_setor where tenant_id=@TenantId and usuario_id=@UserId and setor_id=@SectorId and reg_status='A' and ativo=true and pode_receber=true)", new { TenantId = tenantId, UserId = userId, SectorId = row.DestinationId }, tx, cancellationToken: ct));
            if (!can) return Result.Fail("AUTH", "Você não tem direito de receber neste setor.");
        }
        var changed = await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocol_tramitation
set status=@Target, received_by=@UserId, received_at=now(), updated_at=now(), completed_at=case when @IsReturn then now() else completed_at end
where tenant_id=@TenantId and id=@Id and status=@Expected;
""", new { TenantId = tenantId, row.Id, Target = target, UserId = userId, IsReturn = isReturn, Expected = expected }, tx, cancellationToken: ct));
        if (changed == 0) return Result.Fail("CONFLICT", "A movimentação foi alterada por outro usuário.");
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocol_request p
set assigned_sector_id=@SectorId, assigned_sector_name=@SectorName, assigned_user_id=@UserId,
    assigned_user_name=coalesce(u.name, u.email, @UserId::text), updated_at=now(),
    status=case when p.status='REQUESTED' then 'IN_REVIEW' else p.status end
from ged.app_user u
where p.tenant_id=@TenantId and p.id=@ProtocolId and u.tenant_id=@TenantId and u.id=@UserId;
""", new { TenantId = tenantId, SectorId = row.DestinationId, SectorName = row.DestinationName, UserId = userId, ProtocolId = protocolId }, tx, cancellationToken: ct));
        await WriteHistoryAsync(conn, tx, tenantId, protocolId, null, null, isReturn ? "PROTOCOL_RETURN_CONFIRMED" : "PROTOCOL_RECEIVED", userId, isReturn ? "Retorno confirmado" : "Recebimento confirmado", row.DestinationName, Guid.NewGuid().ToString("N"), ct);
        return Result.Ok();
    }

    private async Task<string> GenerateProtocolNoAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, CancellationToken ct)
    {
        var year = DateTimeOffset.UtcNow.Year;
        // Forma real de ged.code_sequence: (id, tenant_id, entity_name, prefix, current_value, padding, created_at, updated_at, reg_status).
        // Não existe unique em (tenant_id, entity_name), então usa lock por transação + branch insert/update.
        var entityName = $"PROTOCOL-{year}";
        var current = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
select current_value from ged.code_sequence
where tenant_id=@TenantId and entity_name=@EntityName and reg_status='A'
order by id for update;
""", new { TenantId = tenantId, EntityName = entityName }, tx, cancellationToken: ct));
        long value;
        if (current is null)
        {
            value = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding, created_at, updated_at, reg_status)
values(@TenantId, @EntityName, 'PROT', 1, 6, now(), now(), 'A')
returning current_value;
""", new { TenantId = tenantId, EntityName = entityName }, tx, cancellationToken: ct));
        }
        else
        {
            value = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
update ged.code_sequence
set current_value=current_value+1, updated_at=now()
where tenant_id=@TenantId and entity_name=@EntityName and reg_status='A' and current_value=@Current
returning current_value;
""", new { TenantId = tenantId, EntityName = entityName, Current = current.Value }, tx, cancellationToken: ct));
        }
        return $"PROT-{year}-{value:D6}";
    }

    private async Task WriteHistoryAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid id, string? oldStatus, string? newStatus, string action, Guid userId, string? reason, string? internalNotes, string correlationId, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_request_history
(id, tenant_id, protocol_request_id, old_status, new_status, action, user_id, user_name, sector_id, sector_name, reason, internal_notes, metadata_json, correlation_id, created_at, reg_status)
select gen_random_uuid(), @TenantId, @Id, @OldStatus, @NewStatus, @Action, @UserId, coalesce(u.name, u.email, @UserId::text), s.id, nullif(coalesce(s.setor, s.lotacao, ''), ''), @Reason, @InternalNotes, '{}'::jsonb, @CorrelationId, now(), 'A'
from (select 1) seed left join ged.app_user u on u.tenant_id=@TenantId and u.id=@UserId left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id;
""", new { TenantId = tenantId, Id = id, OldStatus = oldStatus, NewStatus = newStatus, Action = action, UserId = userId, Reason = Trim(reason), InternalNotes = Trim(internalNotes), CorrelationId = correlationId }, tx, cancellationToken: ct));
    }

    private Task AuditAsync(Guid tenantId, Guid userId, string action, Guid id, string summary, object data, CancellationToken ct)
        => _audit.WriteAsync(tenantId, userId, action, "protocol_request", id, summary, null, null, data, ct);

    internal static string BuildListMySql(Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, out DynamicParameters parameters)
    {
        var sql = new SafeSqlBuilder(BaseProtocolRequestListSql);
        parameters = new DynamicParameters();

        if (!(scope.CanSeeAll && filter.ShowAll))
        {
            sql.And("""
(
    p.requester_user_id = @UserId
    or p.assigned_user_id = @UserId
)
""");
            parameters.Add("UserId", userId, DbType.Guid);
        }

        AppendCommonProtocolFilters(sql, parameters, filter);
        AppendPagination(sql, parameters, filter, defaultPageSize: 20, maxPageSize: 100);

        var finalSql = sql.ToSql();
        ValidateProtocolSql(finalSql);
        return finalSql;
    }

    internal static string BuildListWorkQueueSql(Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, out DynamicParameters parameters)
    {
        var sql = new SafeSqlBuilder(BaseProtocolRequestListSql);
        parameters = new DynamicParameters();

        if (scope.IsAdmin)
        {
            // Administradores globais enxergam toda a fila do tenant.
        }
        else if (scope.IsAdministradorOphir && scope.SectorId.HasValue)
        {
            sql.And("""
(
    p.assigned_sector_id = @SectorId
    or p.requester_sector_id = @SectorId
    or p.assigned_user_id = @UserId
    or nullif(coalesce(p.assigned_sector_name, ''), '') = @SectorName
    or nullif(coalesce(p.requester_sector_name, ''), '') = @SectorName
)
""");
            parameters.Add("SectorId", scope.SectorId.Value, DbType.Guid);
            parameters.Add("SectorName", scope.SectorName, DbType.String);
            parameters.Add("UserId", userId, DbType.Guid);
        }
        else if (scope.IsAdministradorOphir && !string.IsNullOrWhiteSpace(scope.SectorName))
        {
            sql.And("(nullif(coalesce(p.assigned_sector_name, ''), '') = @SectorName or nullif(coalesce(p.requester_sector_name, ''), '') = @SectorName)");
            parameters.Add("SectorName", scope.SectorName, DbType.String);
        }
        else if (scope.IsAdministradorOphir)
        {
            sql.And("1 = 0");
        }
        else
        {
            sql.And("""
(
    p.requester_user_id = @UserId
    or p.assigned_user_id = @UserId
)
""");
            parameters.Add("UserId", userId, DbType.Guid);
        }

        AppendCommonProtocolFilters(sql, parameters, filter);

        sql.AndIf(filter.OnlyMine, "p.assigned_user_id = @UserId");

        if (filter.Overdue)
        {
            sql.And("p.due_at is not null");
            sql.And("p.due_at < now()");
            sql.And("upper(p.status::text) not in ('FINISHED', 'REJECTED', 'CANCELLED')");
        }

        sql.AndIf(filter.ReturnedForAdjustment, "upper(p.status::text) = 'RETURNED_FOR_ADJUSTMENT'");

        AppendQueueFilter(sql, parameters, userId, scope, filter);
        AppendPagination(sql, parameters, filter, defaultPageSize: 20, maxPageSize: 100);

        var finalSql = sql.ToSql();
        ValidateProtocolSql(finalSql);
        return finalSql;
    }

    private const string BaseProtocolRequestListSql = """
select
    p.id as "Id",
    p.protocol_no as "ProtocolNo",
    p.title as "Title",
    p.description as "Description",
    p.requester_user_id as "RequesterUserId",
    p.requester_name as "RequesterName",
    p.requester_sector_id as "RequesterSectorId",
    p.requester_sector_name as "RequesterSectorName",
    p.assigned_sector_id as "AssignedSectorId",
    p.assigned_sector_name as "AssignedSectorName",
    p.assigned_user_id as "AssignedUserId",
    p.assigned_user_name as "AssignedUserName",
    p.priority as "Priority",
    p.status as "Status",
    p.due_at as "DueAt",
    p.requested_at as "RequestedAt",
    p.updated_at as "UpdatedAt",
    p.finished_at as "FinishedAt",
    (
        select count(*)::int
        from ged.protocol_request_item i
        where i.tenant_id = p.tenant_id
          and i.protocol_request_id = p.id
          and coalesce(i.reg_status, 'A') = 'A'
    ) as "ItemsCount",
    (
        select count(*)::int
        from ged.protocol_request_attachment a
        where a.tenant_id = p.tenant_id
          and a.protocol_request_id = p.id
          and coalesce(a.reg_status, 'A') = 'A'
    ) as "AttachmentsCount",
    (p.due_at is not null and p.due_at < now() and upper(p.status::text) not in ('FINISHED', 'REJECTED', 'CANCELLED')) as "IsOverdue",
    (
        select t.status
        from ged.protocol_tramitation t
        where t.tenant_id = p.tenant_id
          and t.protocol_request_id = p.id
          and coalesce(t.reg_status, 'A') = 'A'
        order by t.forwarded_at desc
        limit 1
    ) as "MovementStatus"
from ged.protocol_request p
where p.tenant_id = @TenantId
  and coalesce(p.reg_status, 'A') = 'A'
""";


    private static void ValidateProtocolSql(string sql)
    {
        var forbidden = new[]
        {
            "@SectorId" + " is not null",
            "@Is" + "Admin",
            "@Is" + "AdministradorOphir",
            "pwhere",
            "where and",
            "and and"
        };

        foreach (var item in forbidden)
        {
            if (sql.Contains(item, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQL de protocolo contém padrão proibido: " + item);
            }
        }
    }

    private void ValidateGeneratedSql(string sql, DynamicParameters? parameters = null)
    {
        var result = SqlSafetyValidator.Validate(sql, parameters);
        if (!result.IsValid)
        {
            _logger.LogError("SQL inválido gerado em ProtocolRequestService. Errors={Errors} Sql={Sql}", string.Join("; ", result.Errors), sql);
            result.ThrowIfInvalid();
        }

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Aviso de SQL gerado em ProtocolRequestService. Warning={Warning} Sql={Sql}", warning, sql);
        }
    }

    private static void AppendCommonProtocolFilters(SafeSqlBuilder sql, DynamicParameters parameters, ProtocolWorkQueueFilter filter)
    {
        var search = string.IsNullOrWhiteSpace(filter.Search) ? filter.Q : filter.Search;

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            sql.And("upper(p.status::text) = upper(@Status)");
            parameters.Add("Status", filter.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            sql.And("upper(p.priority::text) = upper(@Priority)");
            parameters.Add("Priority", filter.Priority.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql.And("""
(
    p.protocol_no ilike @Search
    or p.title ilike @Search
    or coalesce(p.description, '') ilike @Search
    or coalesce(p.requester_name, '') ilike @Search
)
""");
            parameters.Add("Search", $"%{search.Trim()}%");
        }

        if (filter.From.HasValue)
        {
            sql.And("p.requested_at >= @From");
            parameters.Add("From", filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            sql.And("p.requested_at < @To");
            parameters.Add("To", filter.To.Value.AddDays(1));
        }

        if (!string.IsNullOrWhiteSpace(filter.DocumentCode))
        {
            sql.And("""
exists (
    select 1 from ged.protocol_request_item i
    left join ged.document d on d.tenant_id = i.tenant_id and d.id = i.document_id
    where i.tenant_id = p.tenant_id and i.protocol_request_id = p.id and coalesce(i.reg_status,'A')='A'
      and (coalesce(i.reference_code,'') ilike @DocCode or coalesce(i.medical_record_number,'') ilike @DocCode or coalesce(d.code,'') ilike @DocCode)
)
""");
            parameters.Add("DocCode", $"%{filter.DocumentCode.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Interessado))
        {
            sql.And("""
exists (
    select 1 from ged.protocol_request_item i
    where i.tenant_id = p.tenant_id and i.protocol_request_id = p.id and coalesce(i.reg_status,'A')='A'
      and coalesce(i.patient_name,'') ilike @Interessado
)
""");
            parameters.Add("Interessado", $"%{filter.Interessado.Trim()}%");
        }
    }

    private static void AppendQueueFilter(SafeSqlBuilder sql, DynamicParameters parameters, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter)
    {
        var queue = (filter.Queue ?? string.Empty).Trim().ToLowerInvariant();
        if (queue.Length == 0 || queue is "geral") return;
        parameters.Add("QueueAdmin", scope.IsAdmin);
        parameters.Add("QueueUserId", userId);
        parameters.Add("QueueSectorId", scope.SectorId ?? Guid.Empty);
        switch (queue)
        {
            case "receber":
                sql.And("exists (select 1 from ged.protocol_tramitation t where t.tenant_id=p.tenant_id and t.protocol_request_id=p.id and coalesce(t.reg_status,'A')='A' and t.status='PENDING_RECEIPT' and (@QueueAdmin or t.destination_sector_id=@QueueSectorId))");
                break;
            case "tramitar":
                sql.And("upper(p.status::text) not in ('FINISHED','REJECTED','CANCELLED')");
                sql.And("not exists (select 1 from ged.protocol_tramitation t where t.tenant_id=p.tenant_id and t.protocol_request_id=p.id and coalesce(t.reg_status,'A')='A' and t.status in ('PENDING_RECEIPT','RETURN_PENDING'))");
                sql.And("(@QueueAdmin or p.assigned_user_id=@QueueUserId or p.assigned_sector_id=@QueueSectorId)");
                break;
            case "enviados":
                sql.And("exists (select 1 from ged.protocol_tramitation t where t.tenant_id=p.tenant_id and t.protocol_request_id=p.id and coalesce(t.reg_status,'A')='A' and t.status='PENDING_RECEIPT' and (@QueueAdmin or t.forwarded_by=@QueueUserId or t.origin_sector_id=@QueueSectorId))");
                break;
            case "devolucoes":
                sql.And("exists (select 1 from ged.protocol_tramitation t where t.tenant_id=p.tenant_id and t.protocol_request_id=p.id and coalesce(t.reg_status,'A')='A' and t.status='RETURN_PENDING' and (@QueueAdmin or t.destination_sector_id=@QueueSectorId or t.origin_sector_id=@QueueSectorId))");
                break;
        }
    }

    private static void AppendPagination(SafeSqlBuilder sql, DynamicParameters parameters, ProtocolWorkQueueFilter filter, int defaultPageSize, int maxPageSize)
    {
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? defaultPageSize : Math.Min(filter.PageSize, maxPageSize);
        var offset = (page - 1) * pageSize;

        parameters.Add("Offset", offset, DbType.Int32);
        parameters.Add("Limit", pageSize, DbType.Int32);

        sql.OrderBy("p.requested_at desc");
        sql.Paginate();
    }

    private static string NormalizePriority(string? p) => (p ?? "NORMAL").Trim().ToUpperInvariant() switch { "LOW" or "BAIXA" => "LOW", "HIGH" or "ALTA" => "HIGH", "URGENT" or "URGENTE" => "URGENT", _ => "NORMAL" };
    private static string? Trim(string? value) { value = value?.Trim(); return string.IsNullOrWhiteSpace(value) ? null : value; }
}
