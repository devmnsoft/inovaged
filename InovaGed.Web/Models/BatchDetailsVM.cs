using InovaGed.Application.Ged.Batches;

namespace InovaGed.Web.Models
{
    internal class BatchDetailsVM
    {
        public BatchRowDto Header { get; set; }
        public List<BatchItemDto> Items { get; set; }
        public List<BatchHistoryDto> History { get; set; }
    }
}