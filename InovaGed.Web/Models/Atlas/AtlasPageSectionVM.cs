namespace InovaGed.Web.Models.Atlas;

public sealed record AtlasPageSectionVM(
    string? Title,
    string? Description,
    string? Icon,
    string? CssClass,
    bool Divided);
