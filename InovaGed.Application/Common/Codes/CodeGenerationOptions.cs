namespace InovaGed.Application.Common.Codes;

public sealed class CodeGenerationOptions
{
    public int DefaultPadding { get; set; } = 4;
    public Dictionary<string, CodeGenerationEntityOptions> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CodeGenerationEntityOptions
{
    public string? Prefix { get; set; }
    public int? Padding { get; set; }
}
