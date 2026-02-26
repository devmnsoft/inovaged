using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Pacs;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Pacs;

public sealed class TicketRepository : ITicketRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<TicketRepository> _logger;

    public TicketRepository(IDbConnectionFactory db, ILogger<TicketRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid> CreateTicketAsync(
        Guid tenantId,
        string protocolCode,
        string? patientName,
        string? patientId,
        string? modality,
        string? examType,
        string? studyUid,
        string? notes,
        CancellationToken ct)
    {
        try
        {
            var id = Guid.NewGuid();

            const string sql = @"
insert into pacs.ticket
(id, tenant_id, protocol_code, patient_name, patient_id, modality, exam_type, study_uid, notes, status, created_at)
values
(@id, @tenantId, @protocolCode, @patientName, @patientId, @modality, @examType, @studyUid, @notes, 'RECEIVED', now());
";

            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                id,
                tenantId,
                protocolCode,
                patientName,
                patientId,
                modality,
                examType,
                studyUid,
                notes
            }, cancellationToken: ct));

            return id;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: criação de ticket cancelada. Tenant={Tenant} Protocol={Protocol}", tenantId, protocolCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro criando ticket. Tenant={Tenant} Protocol={Protocol}", tenantId, protocolCode);
            throw;
        }
    }

    public async Task AddFileAsync(Guid tenantId, TicketFileDto file, CancellationToken ct)
    {
        try
        {
            const string sql = @"
insert into pacs.ticket_file
(id, ticket_id, tenant_id, original_file_name, content_type, file_size, sha256, storage_rel_path, ocr_status, created_at)
values
(@Id, @TicketId, @TenantId, @OriginalFileName, @ContentType, @FileSize, @Sha256, @StorageRelPath, @OcrStatus, now());
";

            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                file.Id,
                file.TicketId,
                TenantId = tenantId,
                file.OriginalFileName,
                file.ContentType,
                FileSize = file.FileSize,
                file.Sha256,
                file.StorageRelPath,
                file.OcrStatus
            }, cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: inserção de arquivo cancelada. Tenant={Tenant} Ticket={Ticket}", tenantId, file.TicketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro inserindo arquivo. Tenant={Tenant} Ticket={Ticket}", tenantId, file.TicketId);
            throw;
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string ProtocolCode, string Status, DateTimeOffset CreatedAt)>> ListTicketsAsync(
        Guid tenantId, string? q, CancellationToken ct)
    {
        try
        {
            const string sql = @"
select id as Id, protocol_code as ProtocolCode, status as Status, created_at as CreatedAt
from pacs.ticket
where tenant_id = @tenantId
  and (@q is null
       or protocol_code ilike ('%'||@q||'%')
       or patient_name ilike ('%'||@q||'%')
       or patient_id ilike ('%'||@q||'%'))
order by created_at desc
limit 200;
";

            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<(Guid Id, string ProtocolCode, string Status, DateTimeOffset CreatedAt)>(
                new CommandDefinition(sql, new { tenantId, q }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: listagem de tickets cancelada. Tenant={Tenant}", tenantId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro listando tickets. Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<(Guid Id, string ProtocolCode, string Status, string? PatientName, string? PatientId, string? Modality, string? ExamType, string? StudyUid, string? Notes, DateTimeOffset CreatedAt)?>
        GetTicketAsync(Guid tenantId, Guid ticketId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
select
  id as Id,
  protocol_code as ProtocolCode,
  status as Status,
  patient_name as PatientName,
  patient_id as PatientId,
  modality as Modality,
  exam_type as ExamType,
  study_uid as StudyUid,
  notes as Notes,
  created_at as CreatedAt
from pacs.ticket
where tenant_id = @tenantId and id = @ticketId;
";

            using var conn = await _db.OpenAsync(ct);

            return await conn.QueryFirstOrDefaultAsync<
                (Guid Id, string ProtocolCode, string Status, string? PatientName, string? PatientId, string? Modality, string? ExamType, string? StudyUid, string? Notes, DateTimeOffset CreatedAt)?>(
                new CommandDefinition(sql, new { tenantId, ticketId }, cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: leitura de ticket cancelada. Tenant={Tenant} Ticket={Ticket}", tenantId, ticketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro lendo ticket. Tenant={Tenant} Ticket={Ticket}", tenantId, ticketId);
            throw;
        }
    }

    public async Task<IReadOnlyList<TicketFileDto>> ListFilesAsync(Guid tenantId, Guid ticketId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
select
  id as Id,
  ticket_id as TicketId,
  tenant_id as TenantId,
  original_file_name as OriginalFileName,
  content_type as ContentType,
  file_size as FileSize,
  sha256 as Sha256,
  storage_rel_path as StorageRelPath,
  ocr_status as OcrStatus,
  ocr_text as OcrText,
  created_at as CreatedAt
from pacs.ticket_file
where tenant_id = @tenantId and ticket_id = @ticketId
order by created_at asc;
";

            using var conn = await _db.OpenAsync(ct);

            var list = await conn.QueryAsync<TicketFileDto>(
                new CommandDefinition(sql, new { tenantId, ticketId }, cancellationToken: ct));

            return list.AsList();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: listagem de arquivos cancelada. Tenant={Tenant} Ticket={Ticket}", tenantId, ticketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro listando arquivos. Tenant={Tenant} Ticket={Ticket}", tenantId, ticketId);
            throw;
        }
    }

    public async Task<TicketFileDto?> GetFileAsync(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
select
  id as Id,
  ticket_id as TicketId,
  tenant_id as TenantId,
  original_file_name as OriginalFileName,
  content_type as ContentType,
  file_size as FileSize,
  sha256 as Sha256,
  storage_rel_path as StorageRelPath,
  ocr_status as OcrStatus,
  ocr_text as OcrText,
  created_at as CreatedAt
from pacs.ticket_file
where tenant_id = @tenantId and id = @fileId;
";

            using var conn = await _db.OpenAsync(ct);

            return await conn.QueryFirstOrDefaultAsync<TicketFileDto>(
                new CommandDefinition(sql, new { tenantId, fileId }, cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: leitura de arquivo cancelada. Tenant={Tenant} File={File}", tenantId, fileId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro lendo arquivo. Tenant={Tenant} File={File}", tenantId, fileId);
            throw;
        }
    }
}