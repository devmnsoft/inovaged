namespace InovaGed.Application.Ged.Loans
{
    public sealed class DocumentPickDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
    }
}