using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text;

namespace InovaGed.Infrastructure.Ged.Protocols;

public sealed class ProtocolRequestService : IProtocolRequestService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<ProtocolRequestService> _logger;

    public ProtocolRequestService(IDbConnectionFactory db, IAuditWriter audit, ILogger<ProtocolRequestService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
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
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var correlationId = Guid.NewGuid().ToString("N");

            var protocolNo = await GenerateProtocolNoAsync(conn, tx, tenantId, ct);
            var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.protocol_request
(id, tenant_id, protocol_no, requester_user_id, requester_name, requester_sector_id, requester_sector_name, assigned_sector_id, assigned_sector_name, title, description, priority, status, due_at, requested_at, updated_at, correlation_id, reg_status)
select gen_random_uuid(), @TenantId, @ProtocolNo, @UserId,
       coalesce(u.name, u.email, @UserId::text), s.id, nullif(coalesce(s.setor, s.lotacao, ''), ''),
       @AssignedSectorId, nullif(@AssignedSectorName,''), @Title, @Description, @Priority, 'REQUESTED', @DueAt, now(), now(), @CorrelationId, 'A'
from (select 1) seed
left join ged.app_user u on u.tenant_id=@TenantId and u.id=@UserId
left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
returning id;
""", new { TenantId = tenantId, UserId = userId, ProtocolNo = protocolNo, AssignedSectorId = vm.AssignedSectorId, AssignedSectorName = Trim(vm.AssignedSectorName), Title = vm.Title.Trim(), Description = Trim(vm.Description), Priority = NormalizePriority(vm.Priority), DueAt = vm.DueAt?.ToUniversalTime(), CorrelationId = correlationId }, tx, cancellationToken: ct));

            const string itemSql = """
insert into ged.protocol_request_item
(id, tenant_id, protocol_request_id, document_id, document_version_id, is_manual, reference_code, description, document_type, patient_name, medical_record_number, box_code, physical_location, notes, created_at, reg_status)
values (gen_random_uuid(), @TenantId, @ProtocolId, @DocumentId, @DocumentVersionId, @IsManual, @ReferenceCode, @Description, @DocumentType, @PatientName, @MedicalRecordNumber, @BoxCode, @PhysicalLocation, @Notes, now(), 'A');
""";
            foreach (var docId in docIds)
            {
                var versionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("select current_version_id from ged.document where tenant_id=@TenantId and id=@DocumentId", new { TenantId = tenantId, DocumentId = docId }, tx, cancellationToken: ct));
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

    public async Task<Result> AddAttachmentAsync(Guid tenantId, Guid id, Guid userId, string fileName, string? contentType, long sizeBytes, string storagePath, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_request_attachment
(id, tenant_id, protocol_request_id, file_name, content_type, size_bytes, storage_path, uploaded_by, uploaded_by_name, uploaded_at, reg_status)
select gen_random_uuid(), @TenantId, @Id, @FileName, @ContentType, @SizeBytes, @StoragePath, @UserId, coalesce(u.name, u.email, @UserId::text), now(), 'A'
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
            var loanId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.loan_request(id, tenant_id, protocol_no, status, requester_id, requester_name, requester_sector, requested_at, due_at, protocol_request_id, reg_status)
select gen_random_uuid(), p.tenant_id, (select coalesce(max(protocol_no),0)+1 from ged.loan_request where tenant_id=@TenantId), 'REQUESTED', p.requester_user_id, p.requester_name, p.requester_sector_name, now(), coalesce(p.due_at, now() + interval '7 days'), p.id, 'A'
from ged.protocol_request p where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A'
returning id;
""", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            await WriteHistoryAsync(conn, tx, tenantId, id, null, null, "PROTOCOL_LOAN_CREATED", userId, "Solicitação de empréstimo/documento gerada", null, Guid.NewGuid().ToString("N"), ct);
            await tx.CommitAsync(ct);
            await AuditAsync(tenantId, userId, "PROTOCOL_LOAN_CREATED", id, "Loan vinculado ao protocolo", new { loanId }, ct);
            return Result<Guid>.Ok(loanId);
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro ao gerar loan do protocolo {Id}", id); return Result<Guid>.Fail("LOAN", "Erro ao gerar solicitação de empréstimo/documento."); }
    }

    public async Task<IReadOnlyList<ProtocolRequestRowVm>> ListMyAsync(Guid tenantId, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, CancellationToken ct)
    {
        filter ??= new();
        var sql = new StringBuilder(BaseListSql);
        sql.AppendLine("where p.tenant_id = @TenantId");
        sql.AppendLine("  and coalesce(p.reg_status, 'A') = 'A'");

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);

        if (!(scope.CanSeeAll && filter.ShowAll))
        {
            sql.AppendLine("""
and (
    p.requester_user_id = @UserId
    or p.assigned_user_id = @UserId
)
""");
            parameters.Add("UserId", userId);
        }

        AppendCommonProtocolFilters(sql, parameters, filter);
        AppendPagination(sql, parameters, filter, defaultPageSize: 20, maxPageSize: 100);

        var finalSql = sql.ToString();
        LogPotentiallyInvalidSql(finalSql);

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
        var sql = new StringBuilder(BaseListSql);
        sql.AppendLine("where p.tenant_id = @TenantId");
        sql.AppendLine("  and coalesce(p.reg_status, 'A') = 'A'");
        sql.AppendLine("""
and (
    @IsAdmin = true
    or (
        @IsAdministradorOphir = true
        and (
            (@SectorId is not null and p.assigned_sector_id = @SectorId)
            or p.assigned_user_id = @UserId
        )
    )
)
""");

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);
        parameters.Add("UserId", userId);
        parameters.Add("IsAdmin", scope.IsAdmin);
        parameters.Add("IsAdministradorOphir", scope.IsAdministradorOphir);
        parameters.Add("SectorId", scope.SectorId);

        AppendCommonProtocolFilters(sql, parameters, filter);

        if (filter.OnlyMine)
        {
            sql.AppendLine("and p.assigned_user_id = @UserId");
        }

        if (filter.Overdue)
        {
            sql.AppendLine("and p.due_at is not null");
            sql.AppendLine("and p.due_at < now()");
            sql.AppendLine("and upper(p.status::text) not in ('FINISHED', 'REJECTED', 'CANCELLED')");
        }

        if (filter.ReturnedForAdjustment)
        {
            sql.AppendLine("and upper(p.status::text) = 'RETURNED_FOR_ADJUSTMENT'");
        }

        AppendPagination(sql, parameters, filter, defaultPageSize: 20, maxPageSize: 500);

        var finalSql = sql.ToString();
        LogPotentiallyInvalidSql(finalSql);

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
        var header = await conn.QuerySingleOrDefaultAsync<ProtocolRequestRowVm>(new CommandDefinition(BaseListSql + "where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A';", new { TenantId = tenantId, Id = id }, cancellationToken: ct));
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
        var attachments = (await conn.QueryAsync<ProtocolAttachmentVm>(new CommandDefinition("select id, file_name as FileName, content_type as ContentType, size_bytes as SizeBytes, uploaded_by_name as UploadedByName, uploaded_at as UploadedAt from ged.protocol_request_attachment where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by uploaded_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        var history = (await conn.QueryAsync<ProtocolHistoryVm>(new CommandDefinition("select created_at as CreatedAt, action as Action, old_status as OldStatus, new_status as NewStatus, user_name as UserName, sector_name as SectorName, reason as Reason, internal_notes as InternalNotes from ged.protocol_request_history where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by created_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        var loans = (await conn.QueryAsync<ProtocolLoanVm>(new CommandDefinition("select id, protocol_no as ProtocolNo, status::text as Status from ged.loan_request where tenant_id=@TenantId and protocol_request_id=@Id and reg_status='A' order by requested_at desc", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        return new ProtocolRequestDetailsVm { Header = header, Description = desc, Items = items, Attachments = attachments, History = history, Loans = loans };
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
            var oldStatus = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select status from ged.protocol_request where tenant_id=@TenantId and id=@Id and reg_status='A'", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));
            if (oldStatus is null) { await tx.RollbackAsync(ct); return Result.Fail("NOTFOUND", "Protocolo não encontrado."); }
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

    private async Task<string> GenerateProtocolNoAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, CancellationToken ct)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var value = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
insert into ged.code_sequence(tenant_id, module, year, current_value, updated_at)
values(@TenantId, 'PROTOCOL', @Year, 1, now())
on conflict(tenant_id, module, year)
do update set current_value = ged.code_sequence.current_value + 1, updated_at = now()
returning current_value;
""", new { TenantId = tenantId, Year = year }, tx, cancellationToken: ct));
        return $"PROT-{year}-{value:000000}";
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

    private const string BaseListSql = """
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
    (p.due_at is not null and p.due_at < now() and upper(p.status::text) not in ('FINISHED', 'REJECTED', 'CANCELLED')) as "IsOverdue"
from ged.protocol_request p
""";

    private void LogPotentiallyInvalidSql(string finalSql)
    {
        if (finalSql.Contains("where and", StringComparison.OrdinalIgnoreCase)
            || finalSql.Contains("and and", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SQL de Protocolo possivelmente inválido: {Sql}", finalSql);
        }
    }

    private static void AppendCommonProtocolFilters(StringBuilder sql, DynamicParameters parameters, ProtocolWorkQueueFilter filter)
    {
        var search = string.IsNullOrWhiteSpace(filter.Search) ? filter.Q : filter.Search;

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            sql.AppendLine("and upper(p.status::text) = upper(@Status)");
            parameters.Add("Status", filter.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            sql.AppendLine("and upper(p.priority::text) = upper(@Priority)");
            parameters.Add("Priority", filter.Priority.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql.AppendLine("""
and (
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
            sql.AppendLine("and p.requested_at >= @From");
            parameters.Add("From", filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            sql.AppendLine("and p.requested_at < @To");
            parameters.Add("To", filter.To.Value.AddDays(1));
        }
    }

    private static void AppendPagination(StringBuilder sql, DynamicParameters parameters, ProtocolWorkQueueFilter filter, int defaultPageSize, int maxPageSize)
    {
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? defaultPageSize : Math.Min(filter.PageSize, maxPageSize);
        var offset = (page - 1) * pageSize;

        parameters.Add("Offset", offset);
        parameters.Add("Limit", pageSize);

        sql.AppendLine("order by p.requested_at desc");
        sql.AppendLine("offset @Offset limit @Limit");
    }

    private static string NormalizePriority(string? p) => (p ?? "NORMAL").Trim().ToUpperInvariant() switch { "LOW" or "BAIXA" => "LOW", "HIGH" or "ALTA" => "HIGH", "URGENT" or "URGENTE" => "URGENT", _ => "NORMAL" };
    private static string? Trim(string? value) { value = value?.Trim(); return string.IsNullOrWhiteSpace(value) ? null : value; }
}
