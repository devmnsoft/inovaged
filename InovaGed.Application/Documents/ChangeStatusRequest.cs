using System;
using InovaGed.Domain.Documents;

namespace InovaGed.Application.Documents.Workflow;

public sealed class ChangeStatusRequest
{
    public Guid DocumentId { get; init; }
    public DocumentStatus ToStatus { get; init; }
    public string? Reason { get; init; }

    // opcional: metadados
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}
