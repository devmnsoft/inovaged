namespace InovaGed.Web.Services.Commercial;

/// <summary>
/// Boundary for a future payment provider. The public portal currently provisions
/// trials and records the selected plan without simulating a financial transaction.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentCheckoutResult> CreateCheckoutAsync(Guid organizationId, string planCode, CancellationToken ct);
}

public sealed record PaymentCheckoutResult(bool Available, Uri? CheckoutUrl, string Message);

public sealed class ManualPaymentGateway : IPaymentGateway
{
    public Task<PaymentCheckoutResult> CreateCheckoutAsync(Guid organizationId, string planCode, CancellationToken ct) =>
        Task.FromResult(new PaymentCheckoutResult(false, null,
            "A ativação comercial deste plano será concluída com a equipe Valora."));
}
