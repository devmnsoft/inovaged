using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Labels;

public class ManualLabelIndexViewModel
{
    public IReadOnlyList<ManualLabelListItemViewModel> Items { get; set; } = Array.Empty<ManualLabelListItemViewModel>();
}

public class ManualLabelListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ControlNumber { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ManualLabelEditViewModel
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "O template é obrigatório.")]
    public string TemplateCode { get; set; } = string.Empty;
    public Guid? BrandingProfileId { get; set; }
    
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ManualLabelDetailsViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ControlNumber { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public Guid? BrandingProfileId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string PrintedBy { get; set; } = string.Empty;
    
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ManualLabelPrintHistoryItem> History { get; set; } = Array.Empty<ManualLabelPrintHistoryItem>();
}

public class ManualLabelPrintHistoryItem
{
    public Guid Id { get; set; }
    public DateTime PrintedAt { get; set; }
    public string PrintedBy { get; set; } = string.Empty;
    public string? ReprintReason { get; set; }
    public string TraceCode { get; set; } = string.Empty;
}
