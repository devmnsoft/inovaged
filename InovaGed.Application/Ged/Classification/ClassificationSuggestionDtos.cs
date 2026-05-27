namespace InovaGed.Application.Ged.Classification;

public sealed class ClassificationSuggestionDto
{
    public Guid DocumentId { get; set; }
    public Guid? SuggestedDocumentTypeId { get; set; }
    public string? SuggestedDocumentTypeName { get; set; }
    public Guid? SuggestedClassificationId { get; set; }
    public string? SuggestedClassificationCode { get; set; }
    public string? SuggestedClassificationName { get; set; }
    public Guid? SuggestedFolderId { get; set; }
    public string? SuggestedFolderPath { get; set; }
    public string? SuggestedRetentionRule { get; set; }
    public string? SuggestedFinalDestination { get; set; }
    public string? SuggestedSecurityLevel { get; set; }
    public decimal Confidence { get; set; }
    public List<string> Reasons { get; set; } = new();
    public string? Source { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
