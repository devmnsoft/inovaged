namespace InovaGed.Web.Services;

public sealed record FileVisualDefinition(string Icon, string Label, string Tone, string CssClass);

public interface IFileVisualResolver
{
    FileVisualDefinition Resolve(string? extension, string? mimeType);
}
