namespace InovaGed.Application.Pacs;

public interface ITicketRepository
{
    Task<Guid> CreateTicketAsync(
        Guid tenantId,
        string protocolCode,
        string? patientName,
        string? patientId,
        string? modality,
        string? examType,
        string? studyUid,
        string? notes,
        CancellationToken ct);

    Task AddFileAsync(Guid tenantId, TicketFileDto file, CancellationToken ct);

    Task<IReadOnlyList<(Guid Id, string ProtocolCode, string Status, DateTimeOffset CreatedAt)>> ListTicketsAsync(
        Guid tenantId, string? q, CancellationToken ct);

    Task<(Guid Id, string ProtocolCode, string Status, string? PatientName, string? PatientId, string? Modality, string? ExamType, string? StudyUid, string? Notes, DateTimeOffset CreatedAt)?>
        GetTicketAsync(Guid tenantId, Guid ticketId, CancellationToken ct);

    Task<IReadOnlyList<TicketFileDto>> ListFilesAsync(Guid tenantId, Guid ticketId, CancellationToken ct);

    Task<TicketFileDto?> GetFileAsync(Guid tenantId, Guid fileId, CancellationToken ct);
}