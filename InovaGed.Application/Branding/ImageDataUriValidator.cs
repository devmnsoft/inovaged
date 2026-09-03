namespace InovaGed.Application.Branding;

/// <summary>Validates image sources that are safe to embed directly in printable HTML.</summary>
public static class ImageDataUriValidator
{
    public static bool IsValidImageDataUri(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            && value.Contains(";base64,", StringComparison.OrdinalIgnoreCase)
            && value.Length > "data:image/png;base64,".Length;
    }
}
