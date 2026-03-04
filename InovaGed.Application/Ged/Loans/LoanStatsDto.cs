namespace InovaGed.Application.Ged.Loans
{
    public sealed class LoanStatsDto
    {
        public int Total { get; set; }
        public int Open { get; set; }
        public int Overdue { get; set; }
        public int Requested { get; set; }  
        public int Approved { get; set; }
        public int Delivered { get; set; }
        public int Returned { get; set; }
    }
}