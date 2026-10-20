namespace InovaGed.Application.Search
{ 
    public sealed class DocumentSearchRowDto
    {
        public DocumentSearchRowDto() { }
        public DocumentSearchRowDto(Guid DocumentId, Guid VersionId, string Code, string Title, string FileName, string Snippet, float Rank)
        {
            this.DocumentId = DocumentId;
            this.VersionId = VersionId;
            this.Code = Code;
            this.Title = Title;
            this.FileName = FileName;
            this.Snippet = Snippet;
            this.Rank = Rank;
        }
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Snippet { get; set; } = "";
        public float Rank { get; set; }
    }

    public interface IDocumentSearchQueries
    {
        Task<IReadOnlyList<DocumentSearchRowDto>> SearchAsync(
            Guid tenantId,
            string? q,
            Guid? folderId,
            int limit,
            CancellationToken ct);
    }

}
