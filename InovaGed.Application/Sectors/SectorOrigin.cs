namespace InovaGed.Application.Sectors;

public static class SectorOrigin
{
    public static bool TryNormalize(string? value, out string origin)
    {
        origin = "";
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https"
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.Host.Contains('*')) return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
