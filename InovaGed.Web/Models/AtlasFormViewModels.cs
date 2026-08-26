namespace InovaGed.Web.Models;

public sealed class SelectOptionViewModel
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Description { get; init; }
    public bool Disabled { get; init; }
}

public sealed class AtlasFormFieldViewModel
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Value { get; init; }
    public string? HelpText { get; init; }
    public string? Placeholder { get; init; }
    public string Type { get; init; } = "text";
    public bool Required { get; init; }
    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public IReadOnlyList<SelectOptionViewModel> Options { get; init; } = Array.Empty<SelectOptionViewModel>();
}
