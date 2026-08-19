namespace InovaGed.Web.Models.Labels;
public static class LabelSubjectType { public const string Box="BOX"; public const string Document="DOCUMENT"; public const string Batch="BATCH"; public const string Manual="MANUAL_LABEL"; public static bool IsValid(string? value)=>value is Box or Document or Batch or Manual; }
