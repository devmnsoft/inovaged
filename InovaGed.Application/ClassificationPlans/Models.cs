namespace InovaGed.Application.ClassificationPlans;

public sealed class ClassificationNodeRow
{
    public ClassificationNodeRow() { }
    public ClassificationNodeRow(Guid Id, Guid? ParentId, string Code, string Name, bool IsActive, bool IsConfidential, bool RequiresDigitalSignature, string RetentionStartEvent, int RetActiveDays, int RetActiveMonths, int RetActiveYears, int RetArchiveDays, int RetArchiveMonths, int RetArchiveYears, string FinalDestination, string? RetentionNotes)
    {
        this.Id = Id;
        this.ParentId = ParentId;
        this.Code = Code;
        this.Name = Name;
        this.IsActive = IsActive;
        this.IsConfidential = IsConfidential;
        this.RequiresDigitalSignature = RequiresDigitalSignature;
        this.RetentionStartEvent = RetentionStartEvent;
        this.RetActiveDays = RetActiveDays;
        this.RetActiveMonths = RetActiveMonths;
        this.RetActiveYears = RetActiveYears;
        this.RetArchiveDays = RetArchiveDays;
        this.RetArchiveMonths = RetArchiveMonths;
        this.RetArchiveYears = RetArchiveYears;
        this.FinalDestination = FinalDestination;
        this.RetentionNotes = RetentionNotes;
    }
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsConfidential { get; set; }
    public bool RequiresDigitalSignature { get; set; }
    public string RetentionStartEvent { get; set; } = "";
    public int RetActiveDays { get; set; }
    public int RetActiveMonths { get; set; }
    public int RetActiveYears { get; set; }
    public int RetArchiveDays { get; set; }
    public int RetArchiveMonths { get; set; }
    public int RetArchiveYears { get; set; }
    public string FinalDestination { get; set; } = "";
    public string? RetentionNotes { get; set; }
}

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

public sealed class ClassificationHistoryRow
{
    public ClassificationHistoryRow() { }
    public ClassificationHistoryRow(long Id, DateTimeOffset ChangedAt, Guid ChangedBy, string ChangeReason, string Code, string Name, string? ParentCode)
    {
        this.Id = Id;
        this.ChangedAt = ChangedAt;
        this.ChangedBy = ChangedBy;
        this.ChangeReason = ChangeReason;
        this.Code = Code;
        this.Name = Name;
        this.ParentCode = ParentCode;
    }
    public long Id { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedBy { get; set; }
    public string ChangeReason { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentCode { get; set; }
}

public sealed class ClassificationVersionDiffRow
{
    public ClassificationVersionDiffRow() { }
    public ClassificationVersionDiffRow(string Code, string ChangeType, string? PreviousName, string? CurrentName, string? PreviousRetention, string? CurrentRetention, string? PreviousDestination, string? CurrentDestination)
    {
        this.Code = Code;
        this.ChangeType = ChangeType;
        this.PreviousName = PreviousName;
        this.CurrentName = CurrentName;
        this.PreviousRetention = PreviousRetention;
        this.CurrentRetention = CurrentRetention;
        this.PreviousDestination = PreviousDestination;
        this.CurrentDestination = CurrentDestination;
    }
    public string Code { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public string? PreviousName { get; set; }
    public string? CurrentName { get; set; }
    public string? PreviousRetention { get; set; }
    public string? CurrentRetention { get; set; }
    public string? PreviousDestination { get; set; }
    public string? CurrentDestination { get; set; }
}

public sealed class ClassificationVersionRow
{
    public ClassificationVersionRow() { }
    public ClassificationVersionRow(Guid Id, int VersionNo, string Title, DateTimeOffset PublishedAt, Guid? PublishedBy)
    {
        this.Id = Id;
        this.VersionNo = VersionNo;
        this.Title = Title;
        this.PublishedAt = PublishedAt;
        this.PublishedBy = PublishedBy;
    }
    public Guid Id { get; set; }
    public int VersionNo { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
}

public sealed class ClassificationVersionDetailsVM
{
    public Guid Id { get; set; }
    public int VersionNo { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
}

public sealed class ClassificationVersionItemRow
{
    public ClassificationVersionItemRow() { }
    public ClassificationVersionItemRow(long Id, Guid ClassificationId, string Code, string Name, string? Description, string? ParentCode, string RetentionStartEvent, int RetentionActiveDays, int RetentionActiveMonths, int RetentionActiveYears, int RetentionArchiveDays, int RetentionArchiveMonths, int RetentionArchiveYears, string FinalDestination, bool RequiresDigitalSignature, bool IsConfidential, bool IsActive, string? RetentionNotes)
    {
        this.Id = Id;
        this.ClassificationId = ClassificationId;
        this.Code = Code;
        this.Name = Name;
        this.Description = Description;
        this.ParentCode = ParentCode;
        this.RetentionStartEvent = RetentionStartEvent;
        this.RetentionActiveDays = RetentionActiveDays;
        this.RetentionActiveMonths = RetentionActiveMonths;
        this.RetentionActiveYears = RetentionActiveYears;
        this.RetentionArchiveDays = RetentionArchiveDays;
        this.RetentionArchiveMonths = RetentionArchiveMonths;
        this.RetentionArchiveYears = RetentionArchiveYears;
        this.FinalDestination = FinalDestination;
        this.RequiresDigitalSignature = RequiresDigitalSignature;
        this.IsConfidential = IsConfidential;
        this.IsActive = IsActive;
        this.RetentionNotes = RetentionNotes;
    }
    public long Id { get; set; }
    public Guid ClassificationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ParentCode { get; set; }
    public string RetentionStartEvent { get; set; } = "";
    public int RetentionActiveDays { get; set; }
    public int RetentionActiveMonths { get; set; }
    public int RetentionActiveYears { get; set; }
    public int RetentionArchiveDays { get; set; }
    public int RetentionArchiveMonths { get; set; }
    public int RetentionArchiveYears { get; set; }
    public string FinalDestination { get; set; } = "";
    public bool RequiresDigitalSignature { get; set; }
    public bool IsConfidential { get; set; }
    public bool IsActive { get; set; }
    public string? RetentionNotes { get; set; }
}
