namespace InovaGed.Application.Ged.Instruments;

public sealed class ClassificationPlanRow
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public Guid? ParentId { get; init; }
    public string ActivityType { get; init; } = "MEIO";
    public int SortOrder { get; init; }
    public string? CurrentTermText { get; init; }
    public string? CurrentEvent { get; init; }
    public string? IntermediateTermText { get; init; }
    public string? IntermediateEvent { get; init; }
    public string? NormativeSource { get; init; }
    public string? ConditionOrException { get; init; }
    public string ReviewStatus { get; init; } = "PENDENTE";

    public string RetentionStartEvent { get; init; } = "INCLUSAO"; // enum ged.retention_start_event
    public int RetentionActiveDays { get; init; }
    public int RetentionActiveMonths { get; init; }
    public int RetentionActiveYears { get; init; }
    public int RetentionArchiveDays { get; init; }
    public int RetentionArchiveMonths { get; init; }
    public int RetentionArchiveYears { get; init; }
    public string FinalDestination { get; init; } = "ELIMINAR"; // ged.final_destination
    public bool RequiresDigitalSignature { get; init; }
    public bool IsConfidential { get; init; }
    public bool IsActive { get; init; }
    public string? RetentionNotes { get; init; }
}

public class ClassificationPlanCreateVM
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string ActivityType { get; set; } = "MEIO";
    public int SortOrder { get; set; }
    public string? CurrentTermText { get; set; }
    public string? CurrentEvent { get; set; }
    public string? IntermediateTermText { get; set; }
    public string? IntermediateEvent { get; set; }
    public string? NormativeSource { get; set; }
    public string? ConditionOrException { get; set; }
    public string ReviewStatus { get; set; } = "PENDENTE";

    public string RetentionStartEvent { get; set; } = "INCLUSAO";
    public int RetentionActiveDays { get; set; }
    public int RetentionActiveMonths { get; set; }
    public int RetentionActiveYears { get; set; }
    public int RetentionArchiveDays { get; set; }
    public int RetentionArchiveMonths { get; set; }
    public int RetentionArchiveYears { get; set; }
    public string FinalDestination { get; set; } = "ELIMINAR";
    public bool RequiresDigitalSignature { get; set; }
    public bool IsConfidential { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RetentionNotes { get; set; }
}

public sealed class ClassificationPlanUpdateVM : ClassificationPlanCreateVM { }

public sealed class ClassificationPlanMoveVM
{
    public Guid Id { get; set; }
    public Guid? NewParentId { get; set; }
    public string? NewCode { get; set; } // se null -> só move de parent
    public string? Reason { get; set; }
}

public sealed class PublishClassificationPlanVersionVM
{
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
}
