namespace InovaGed.Web.ViewModels;

public class SignatureIndexVm
{
    public List<DocumentRow> Docs { get; set; } = new();
    public List<BatchRow> Batches { get; set; } = new();

    public class DocumentRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
    }

    public class BatchRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Status { get; set; } = "";
    }
}