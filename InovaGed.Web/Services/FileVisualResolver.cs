namespace InovaGed.Web.Services;

public sealed class FileVisualResolver : IFileVisualResolver
{
    private static readonly FileVisualDefinition Generic = new("file-generic", "Arquivo", "muted", "file-visual--generic");
    private static readonly IReadOnlyDictionary<string, FileVisualDefinition> Extensions =
        new Dictionary<string, FileVisualDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new("file-pdf", "PDF", "danger", "file-visual--pdf"),
            [".doc"] = new("file-doc", "Word", "primary", "file-visual--word"),
            [".docx"] = new("file-docx", "Word", "primary", "file-visual--word"),
            [".xls"] = new("file-xls", "Excel", "success", "file-visual--excel"),
            [".xlsx"] = new("file-xlsx", "Excel", "success", "file-visual--excel"),
            [".ppt"] = new("file-ppt", "PowerPoint", "warning", "file-visual--powerpoint"),
            [".pptx"] = new("file-pptx", "PowerPoint", "warning", "file-visual--powerpoint"),
            [".jpg"] = new("file-jpg", "Imagem", "violet", "file-visual--image"),
            [".jpeg"] = new("file-jpg", "Imagem", "violet", "file-visual--image"),
            [".png"] = new("file-png", "Imagem", "violet", "file-visual--image"),
            [".tif"] = new("file-tiff", "Imagem", "violet", "file-visual--image"),
            [".tiff"] = new("file-tiff", "Imagem", "violet", "file-visual--image"),
            [".txt"] = new("file-txt", "Texto", "muted", "file-visual--text"),
            [".csv"] = new("file-csv", "CSV", "success", "file-visual--csv"),
            [".zip"] = new("file-zip", "ZIP", "warning", "file-visual--zip"),
            [".dcm"] = new("file-dicom", "DICOM", "medical", "file-visual--dicom")
        };

    public FileVisualDefinition Resolve(string? extension, string? mimeType)
    {
        var normalized = NormalizeExtension(extension);
        if (Extensions.TryGetValue(normalized, out var definition)) return definition;
        if (mimeType?.Equals("application/dicom", StringComparison.OrdinalIgnoreCase) == true) return Extensions[".dcm"];
        if (mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return Extensions[".png"];
        if (mimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true) return Extensions[".txt"];
        return Generic;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}
