using System;
using System.Collections.Generic;
using InovaGed.Domain.Documents;

namespace InovaGed.Web.Models.Ged;

public sealed class GedDetailsVM
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsConfidential { get; set; }

    public Guid? CreatedBy { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public Guid? AutoPreviewVersionId { get; set; }
    public string? AutoPreviewLabel { get; set; }

    // ✅ versão que o preview deve abrir (auto-switch OCR)
    public Guid? SelectedVersionId { get; set; }

    // ✅ se estiver visualizando OCR, qual é a versão base (origem)
    public Guid? OcrSourceVersionId { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    // ✅ versões
    public List<VersionVM> Versions { get; set; } = new();

    // ✅ workflow sempre existe (evita null na View)
    public WorkflowVM Workflow { get; set; } = new();

    public sealed class VersionVM
    {
        public Guid Id { get; set; }
        public int VersionNumber { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public bool IsCurrent { get; set; }

        // OCR status
        public string? OcrStatus { get; set; }
        public long? OcrJobId { get; set; }
        public string? OcrErrorMessage { get; set; }
        public DateTime? OcrRequestedAt { get; set; }
        public DateTime? OcrStartedAt { get; set; }
        public DateTime? OcrFinishedAt { get; set; }
        public bool OcrInvalidateDigitalSignatures { get; set; }
    }

    public sealed class WorkflowVM
    {
        public bool HasActiveWorkflow { get; set; }
        public Guid? DocumentWorkflowId { get; set; }

        public Guid? WorkflowId { get; set; }
        public string? WorkflowName { get; set; }

        public Guid? CurrentStageId { get; set; }
        public string? CurrentStageName { get; set; }

        public bool IsCompleted { get; set; }
        public DateTime? StartedAt { get; set; }
        public Guid? StartedBy { get; set; }

        // ✅ REMOVIDO: public WorkflowVM Workflow { get; set; } = new();
        // Isso causava recursão infinita.

        public List<WorkflowDefinitionRow> AvailableWorkflows { get; set; } = new();
        public List<TransitionRow> AvailableTransitions { get; set; } = new();
        public List<HistoryRow> History { get; set; } = new();

        public sealed class WorkflowDefinitionRow
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed class TransitionRow
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public Guid ToStageId { get; set; }
            public string ToStageName { get; set; } = "";
            public bool RequiresReason { get; set; }
        }

        public sealed class HistoryRow
        {
            public long Id { get; set; }
            public string? FromStageName { get; set; }
            public string ToStageName { get; set; } = "";
            public DateTime PerformedAt { get; set; }
            public Guid? PerformedBy { get; set; }
            public string? Reason { get; set; }
            public string? Comments { get; set; }
        }
    }
}
