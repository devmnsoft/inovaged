using System;
using System.Threading;
using System.Threading.Tasks;

namespace InovaGed.Application.Documents.Workflow;

public interface IDocumentWorkflowService
{
    Task ChangeStatusAsync(Guid tenantId, Guid? userId, ChangeStatusRequest req, CancellationToken ct);
}
