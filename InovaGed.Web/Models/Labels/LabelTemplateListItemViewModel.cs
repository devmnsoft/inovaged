namespace InovaGed.Web.Models.Labels;

public class LabelTemplateListItemViewModel
{
    public string TemplateKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string BrandingProfileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string Health { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsLegacy { get; set; }
}
