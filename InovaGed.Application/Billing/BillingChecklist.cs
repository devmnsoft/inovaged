namespace InovaGed.Application.Billing;

public sealed record BillingChecklistItem(string Code, string Label, bool Passed, bool Required, string Detail);

public static class BillingChecklist
{
    public const decimal ReviewConfidence = 60m;

    public static IReadOnlyList<BillingChecklistItem> Evaluate(BillingExtractionDto item)
    {
        var kind = string.IsNullOrWhiteSpace(item.DocumentKind) ? null : item.DocumentKind.Trim();
        var competence = item.CompetenceMonth?.Trim();
        var competenceValid = string.IsNullOrWhiteSpace(competence)
            || System.Text.RegularExpressions.Regex.IsMatch(competence, @"^(0[1-9]|1[0-2])/\d{4}$");
        var datesConsistent = item.DueDate is null || item.IssueDate is null || item.DueDate >= item.IssueDate;
        var netConsistent = item.NetAmount is null || item.GrossAmount is null || item.NetAmount <= item.GrossAmount;
        var amountsNonNegative = new decimal?[]
        {
            item.GrossAmount, item.NetAmount, item.TaxAmount, item.UstQuantity, item.UstUnitValue
        }.All(value => value is null or >= 0);

        return
        [
            new("IDENTIFIED", "Documento identificado como faturamento", kind is not null, true, KindLabel(kind)),
            new("SUPPLIER", "Fornecedor informado", !string.IsNullOrWhiteSpace(item.SupplierName), true, item.SupplierName ?? "Não informado"),
            new("TAX_ID", "CPF ou CNPJ válido", BrazilianTaxId.IsValid(item.SupplierDocument), true, string.IsNullOrWhiteSpace(item.SupplierDocument) ? "Não informado" : item.SupplierDocument),
            new("INVOICE", "Número do documento fiscal", !string.IsNullOrWhiteSpace(item.InvoiceNumber), true, item.InvoiceNumber ?? "Não informado"),
            new("GROSS", "Valor bruto maior que zero", item.GrossAmount is > 0, true, item.GrossAmount?.ToString("N2") ?? "Não informado"),
            new("DATES", "Vencimento não anterior à emissão", datesConsistent, true, datesConsistent ? "Datas consistentes" : "Vencimento anterior à emissão"),
            new("NET", "Valor líquido não excede o bruto", netConsistent, true, netConsistent ? "Valores consistentes" : "Líquido maior que o bruto"),
            new("AMOUNTS", "Nenhum valor negativo", amountsNonNegative, true, amountsNonNegative ? "Valores não negativos" : "Há valor negativo"),
            new("COMPETENCE", "Competência no formato MM/AAAA", competenceValid, false, string.IsNullOrWhiteSpace(competence) ? "Não informada" : competence!),
            new("CONFIDENCE", "Confiança suficiente para revisão", item.Confidence >= ReviewConfidence, false, $"{item.Confidence:N0}%")
        ];
    }

    public static IReadOnlyList<BillingChecklistItem> EvaluateReview(BillingExtractionDto stored, BillingReviewInput input) =>
        Evaluate(new BillingExtractionDto
        {
            DocumentKind = stored.DocumentKind,
            Confidence = stored.Confidence,
            SupplierName = input.SupplierName,
            SupplierDocument = input.SupplierDocument,
            InvoiceNumber = input.InvoiceNumber,
            IssueDate = input.IssueDate,
            DueDate = input.DueDate,
            CompetenceMonth = input.CompetenceMonth,
            GrossAmount = input.GrossAmount,
            NetAmount = input.NetAmount,
            TaxAmount = input.TaxAmount,
            UstQuantity = input.UstQuantity,
            UstUnitValue = input.UstUnitValue
        });

    public static bool CanApprove(IEnumerable<BillingChecklistItem> items) => items.Where(item => item.Required).All(item => item.Passed);

    public static string KindLabel(string? kind) => kind switch
    {
        "INVOICE" => "Nota fiscal",
        "BANK_SLIP" => "Boleto",
        "PAYMENT_RECEIPT" => "Comprovante de pagamento",
        "UST_REPORT" => "Relatório de UST",
        "SERVICE_ORDER" => "Ordem de serviço",
        "MEASUREMENT" => "Medição",
        "CONTRACT" => "Contrato",
        "RECEIPT" => "Recibo",
        "BILL" => "Fatura",
        "BILLING_DOCUMENT" => "Documento de faturamento",
        null or "" => "Tipo não reconhecido",
        _ => kind
    };
}
