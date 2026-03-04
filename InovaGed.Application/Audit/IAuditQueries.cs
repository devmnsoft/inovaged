namespace InovaGed.Application.Audit
{
    public interface IAuditQueries
    {
        Task<AuditIndexVM> ListAsync(Guid tenantId, AuditSearchVM search, CancellationToken ct);
        Task<AuditIndexVM> ListAccessDeniedAsync(Guid tenantId, AuditSearchVM search, CancellationToken ct);
    }
}
