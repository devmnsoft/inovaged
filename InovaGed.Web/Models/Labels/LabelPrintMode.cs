namespace InovaGed.Web.Models.Labels;
public static class LabelPrintMode { public const string Factory="FACTORY"; public const string Custom="CUSTOM"; public static bool IsValid(string? value)=>value is Factory or Custom; }
