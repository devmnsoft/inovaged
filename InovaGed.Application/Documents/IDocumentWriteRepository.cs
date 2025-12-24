using System.Data;
using InovaGed.Domain.Documents;

namespace InovaGed.Application.Documents;

public interface IDocumentWriteRepository
{
    Task InsertDocumentAsync(DocumentRow row, IDbTransaction tx, CancellationToken ct);
    Task<int> GetNextVersionNumberAsync(Guid tenantId, Guid documentId, IDbTransaction tx, CancellationToken ct);
    Task InsertVersionAsync(DocumentVersionRow row, IDbTransaction tx, CancellationToken ct);
    Task UpdateCurrentVersionAsync(Guid tenantId, Guid documentId, Guid currentVersionId, Guid? userId, IDbTransaction tx, CancellationToken ct);

     
}


