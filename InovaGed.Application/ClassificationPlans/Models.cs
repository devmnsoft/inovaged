namespace InovaGed.Application.ClassificationPlans;

public sealed record ClassificationNodeRow(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    bool IsActive,
    bool IsConfidential,
    bool RequiresDigitalSignature,
    string RetentionStartEvent,
    int RetActiveDays,
    int RetActiveMonths,
    int RetActiveYears,
    int RetArchiveDays,
    int RetArchiveMonths,
    int RetArchiveYears,
    string FinalDestination,
    string? RetentionNotes
);

public sealed class ClassificationEditVM
{
    public Guid? Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string ActivityType { get; set; } = "MEIO";
    public int DisplayOrder { get; set; }
    public string CurrentRetentionText { get; set; } = "";
    public string CurrentStartEvent { get; set; } = "";
    public string IntermediateRetentionText { get; set; } = "";
    public string IntermediateStartEvent { get; set; } = "";
    public string? NormativeSource { get; set; }
    public string? ConditionException { get; set; }
    public string ConfidentialityLevel { get; set; } = "PUBLICO";
    public string ReviewStatus { get; set; } = "RASCUNHO";

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

public sealed record ClassificationHistoryRow(long Id, DateTimeOffset ChangedAt, Guid ChangedBy,
    string ChangeReason, string Code, string Name, string? ParentCode);

public sealed record ClassificationVersionDiffRow(string Code, string ChangeType, string? PreviousName,
    string? CurrentName, string? PreviousRetention, string? CurrentRetention,
    string? PreviousDestination, string? CurrentDestination);

public sealed record ClassificationVersionRow(
    Guid Id,
    int VersionNo,
    string Title,
    DateTimeOffset PublishedAt,
    Guid? PublishedBy
);

public sealed class ClassificationVersionDetailsVM
{
    public Guid Id { get; set; }
    public int VersionNo { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
}

public sealed record ClassificationVersionItemRow(
    long Id,
    Guid ClassificationId,
    string Code,
    string Name,
    string? Description,
    string? ParentCode,
    string RetentionStartEvent,
    int RetentionActiveDays,
    int RetentionActiveMonths,
    int RetentionActiveYears,
    int RetentionArchiveDays,
    int RetentionArchiveMonths,
    int RetentionArchiveYears,
    string FinalDestination,
    bool RequiresDigitalSignature,
    bool IsConfidential,
    bool IsActive,
    string? RetentionNotes
);
