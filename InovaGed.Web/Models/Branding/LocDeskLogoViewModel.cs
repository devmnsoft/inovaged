namespace InovaGed.Web.Models.Branding;

public sealed class LocDeskLogoViewModel
{
    public string? CssClass { get; init; }
    public string? Alt { get; init; }
    public bool Decorative { get; init; }
    public string Loading { get; init; } = "eager";
}
