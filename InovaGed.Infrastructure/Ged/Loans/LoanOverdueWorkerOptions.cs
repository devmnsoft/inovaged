namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueWorkerOptions
{
    /// <summary>
    /// Habilita ou desabilita o worker
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Intervalo de execução em minutos
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Tenant utilizado para execução do worker
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;
}