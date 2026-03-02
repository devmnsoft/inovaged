using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InovaGed.Application.Retention
{
    public sealed class RetentionRuleRow
    {
        public Guid Id { get; set; }
        public string ClassCode { get; set; } = "";
        public string StartEvent { get; set; } = "CREATED"; // CREATED|ARCHIVED|CLOSED
        public int CurrentDays { get; set; }
        public int IntermediateDays { get; set; }
        public string FinalDestination { get; set; } = "ELIMINAR";
        public string? Notes { get; set; }
    }
     
}
