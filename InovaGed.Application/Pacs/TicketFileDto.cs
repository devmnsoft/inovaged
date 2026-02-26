using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InovaGed.Application.Pacs
{
    public sealed class TicketFileDto
    {
        public Guid Id { get; set; }
        public Guid TicketId { get; set; }
        public Guid TenantId { get; set; }

        public string OriginalFileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long FileSize { get; set; }
        public string Sha256 { get; set; } = "";
        public string StorageRelPath { get; set; } = "";

        public string OcrStatus { get; set; } = "PENDING";
        public string? OcrText { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
