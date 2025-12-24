using System.ComponentModel.DataAnnotations;
using InovaGed.Domain.Workflow;

namespace InovaGed.Web.Models.Workflow;

public sealed class WorkflowIndexVM
{
    public IReadOnlyList<WorkflowDefinitionRowDto> Workflows { get; set; } = Array.Empty<WorkflowDefinitionRowDto>();
    public WorkflowDefinitionDetailsDto? Selected { get; set; }

    public IReadOnlyList<WorkflowStageRowDto> Stages { get; set; } = Array.Empty<WorkflowStageRowDto>();
    public IReadOnlyList<WorkflowTransitionRowDto> Transitions { get; set; } = Array.Empty<WorkflowTransitionRowDto>();
}

public sealed class WorkflowDefinitionFormVM
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Informe o código.")]
    [StringLength(50)]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(200)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class WorkflowStageFormVM
{
    public Guid? Id { get; set; }
    public Guid WorkflowId { get; set; }

    [Required(ErrorMessage = "Informe o código.")]
    [StringLength(50)]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(200)]
    public string? Name { get; set; }

    [Range(0, 999)]
    public int SortOrder { get; set; }

    public bool IsStart { get; set; }
    public bool IsFinal { get; set; }

    [StringLength(100)]
    public string? RequiredRole { get; set; }
}

public sealed class WorkflowTransitionFormVM
{
    public Guid? Id { get; set; }
    public Guid WorkflowId { get; set; }

    [Required]
    public Guid FromStageId { get; set; }

    [Required]
    public Guid ToStageId { get; set; }

    [Required(ErrorMessage = "Informe o nome da transição.")]
    [StringLength(200)]
    public string? Name { get; set; }

    public bool RequiresReason { get; set; }
}
