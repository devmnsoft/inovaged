namespace InovaGed.Web.Models.Labels;
public static class LocDeskLabelKind
{
    public const string Folder = "FOLDER";
    public const string Box = "BOX";
    public static bool IsValid(string? value) => value is Folder or Box;
}
