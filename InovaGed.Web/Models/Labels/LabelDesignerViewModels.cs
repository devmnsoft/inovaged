namespace InovaGed.Web.Models.Labels;

public sealed class LabelDesignViewModel
{
    public Guid Id { get; set; }
    public string TemplateCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? Description { get; set; }
    public string SubjectType { get; set; } = "DOCUMENT";
    public string PrintMode { get; set; } = "CUSTOM";
    public string Status { get; set; } = "DRAFT";
    public decimal WidthMm { get; set; } = 100;
    public decimal HeightMm { get; set; } = 145;
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "PORTRAIT";
    public bool IsSystemTemplate { get; set; }
    public string? BaseTemplateCode { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<LabelDesignFieldViewModel> Fields { get; set; } = [];
    public List<LabelDesignValidationViewModel> Validations { get; set; } = [];
    public bool CanEdit => !IsSystemTemplate && Status == "DRAFT";
}

public sealed class LabelDesignFieldViewModel
{
    public string FieldKey { get; set; } = "";
    public string FieldLabel { get; set; } = "";
    public string FieldType { get; set; } = "TEXT";
    public string? DataSource { get; set; }
    public decimal XMm { get; set; }
    public decimal YMm { get; set; }
    public decimal WidthMm { get; set; } = 30;
    public decimal HeightMm { get; set; } = 8;
    public decimal FontSizePt { get; set; } = 9;
    public string? FontWeight { get; set; }
    public string TextAlign { get; set; } = "LEFT";
    public string? Color { get; set; }
    public bool IsRequired { get; set; }
    public bool IsPrintable { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed record LabelDesignValidationViewModel(string ValidationType, string Severity, string Title, string? Description, string Status);
public sealed record LabelDesignVersionViewModel(int VersionNumber, string Status, DateTime PublishedAt, string? Notes);

public sealed class SaveLabelDesignInput
{
    public string TemplateName { get; set; } = "";
    public string? Description { get; set; }
    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public string FieldsJson { get; set; } = "[]";
}
