
namespace InovaGed.Infrastructure.Pacs
{
    internal class TicketCreateDto
    {
        public string Title { get; set; }
        public string PatientName { get; set; }
        public string PatientId { get; set; }
        public string ExamType { get; set; }
        public DateTime? ExamDate { get; set; }
        public string Notes { get; set; }
        public string Source { get; set; }
        public string SourceRef { get; set; }
    }
}