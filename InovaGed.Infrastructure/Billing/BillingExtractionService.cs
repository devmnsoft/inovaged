using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;

public sealed partial class BillingExtractionService : IBillingExtractionService
{
    private readonly IDbConnectionFactory _db;
    private readonly IBillingCommands _commands;
    private readonly IBillingRuleService _rules;
    public BillingExtractionService(IDbConnectionFactory db, IBillingCommands commands, IBillingRuleService rules) { _db = db; _commands = commands; _rules = rules; }

    public bool LooksFinancial(string text) => FinancialWords().IsMatch(text ?? "");

    public BillingExtractionDto Extract(BillingExtractionCandidate candidate)
    {
        var text = candidate.Text ?? "";
        var result = new BillingExtractionDto { DocumentId = candidate.DocumentId, DocumentVersionId = candidate.DocumentVersionId, ExtractionStatus = "PENDING_REVIEW", Confidence = 25 };
        result.SupplierDocument = Match(text, DocumentRegex());
        result.SupplierName = Match(text, SupplierRegex());
        result.InvoiceNumber = Match(text, InvoiceRegex());
        result.InvoiceSeries = Match(text, SeriesRegex());
        result.CompetenceMonth = Match(text, CompetenceRegex());
        result.ContractNumber = Match(text, ContractRegex());
        result.PurchaseOrder = Match(text, PurchaseOrderRegex());
        result.CostCenter = Match(text, CostCenterRegex());
        result.UstQuantity = Money(Match(text, UstQuantityRegex()));
        result.UstUnitValue = Money(Match(text, UstValueRegex()));
        result.GrossAmount = Money(Match(text, TotalRegex())) ?? Money(Match(text, MoneyRegex()));
        result.NetAmount = Money(Match(text, NetRegex()));
        result.IssAmount = Money(Match(text, IssRegex())); result.InssAmount = Money(Match(text, InssRegex()));
        result.PisAmount = Money(Match(text, PisRegex())); result.CofinsAmount = Money(Match(text, CofinsRegex()));
        result.IrAmount = Money(Match(text, IrRegex())); result.CsllAmount = Money(Match(text, CsllRegex()));
        result.TaxAmount = new[] { result.IssAmount, result.InssAmount, result.PisAmount, result.CofinsAmount, result.IrAmount, result.CsllAmount }.Where(x => x.HasValue).Sum(x => x!.Value);
        result.IssueDate = Date(Match(text, IssueDateRegex())); result.DueDate = Date(Match(text, DueDateRegex()));
        result.DocumentKind = Kind(text);
        var found = new object?[] { result.SupplierDocument, result.SupplierName, result.InvoiceNumber, result.GrossAmount, result.IssueDate, result.DueDate, result.ContractNumber }.Count(x => x is not null);
        result.Confidence = Math.Min(95, 25 + found * 10);
        result.Warnings = new[] { result.SupplierDocument is null ? "CPF/CNPJ não identificado." : null, result.GrossAmount is null ? "Valor total não identificado." : null, result.DueDate is null ? "Vencimento não identificado." : null }.Where(x => x is not null).Cast<string>().ToArray();
        return result;
    }

    public async Task<BillingExtractionDto?> ExtractDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = """
select d.id as "DocumentId", ds.version_id as "DocumentVersionId", ds.ocr_text as "Text"
from ged.document d join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
where d.tenant_id=@tenantId and d.id=@documentId and d.reg_status='A' and nullif(btrim(ds.ocr_text),'') is not null
order by ds.updated_at desc nulls last limit 1
""";
        var candidate = await conn.QuerySingleOrDefaultAsync<BillingExtractionCandidate>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        if (candidate is null || !LooksFinancial(candidate.Text)) return null;
        var extraction = await ExtractAsync(tenantId, candidate, ct);
        await _commands.SaveExtractionAsync(tenantId, extraction, ct);
        return extraction;
    }

    public async Task<BillingExtractionDto> ExtractAsync(Guid tenantId, BillingExtractionCandidate candidate, CancellationToken ct)
    {
        var result = Extract(candidate);
        var rules = await _rules.ListAsync(tenantId, ct);
        foreach (var rule in rules.Where(x => x.IsActive && (x.DocumentKind == "*" || x.DocumentKind.Equals(result.DocumentKind, StringComparison.OrdinalIgnoreCase))))
        {
            var keywordFound = string.IsNullOrWhiteSpace(rule.Keyword) || candidate.Text.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase);
            if (!keywordFound)
            {
                if (rule.IsRequired) result.Warnings = [.. result.Warnings, $"Regra obrigatória não atendida: {rule.Name}."];
                continue;
            }
            string? value = rule.Keyword;
            if (!string.IsNullOrWhiteSpace(rule.RegexPattern))
            {
                var match = Regex.Match(candidate.Text, rule.RegexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
                value = match.Success ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim() : null;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                if (rule.IsRequired) result.Warnings = [.. result.Warnings, $"Regra obrigatória sem valor: {rule.Name}."];
                continue;
            }
            ApplyRuleValue(result, rule.TargetField, value);
        }
        return result;
    }

    private static void ApplyRuleValue(BillingExtractionDto result, string field, string value)
    {
        switch (field)
        {
            case "SupplierName": result.SupplierName = value; break;
            case "SupplierDocument": result.SupplierDocument = value; break;
            case "InvoiceNumber": result.InvoiceNumber = value; break;
            case "InvoiceSeries": result.InvoiceSeries = value; break;
            case "CompetenceMonth": result.CompetenceMonth = value; break;
            case "ContractNumber": result.ContractNumber = value; break;
            case "PurchaseOrder": result.PurchaseOrder = value; break;
            case "CostCenter": result.CostCenter = value; break;
            case "ServiceDescription": result.ServiceDescription = value; break;
            case "GrossAmount": result.GrossAmount = Money(value); break;
            case "NetAmount": result.NetAmount = Money(value); break;
            case "TaxAmount": result.TaxAmount = Money(value); break;
            case "UstQuantity": result.UstQuantity = Money(value); break;
            case "UstUnitValue": result.UstUnitValue = Money(value); break;
            case "IssueDate": result.IssueDate = Date(value); break;
            case "DueDate": result.DueDate = Date(value); break;
        }
    }

    private static string? Match(string text, Regex regex) { var m = regex.Match(text); return m.Success ? m.Groups[1].Value.Trim() : null; }
    private static decimal? Money(string? value) => decimal.TryParse(value?.Replace(".", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static DateTime? Date(string? value) => DateTime.TryParseExact(value, new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string Kind(string text) =>
        Regex.IsMatch(text, "nota fiscal|nf-e", RegexOptions.IgnoreCase) ? "INVOICE" :
        Regex.IsMatch(text, "boleto|linha digitável|código de barras", RegexOptions.IgnoreCase) ? "BANK_SLIP" :
        Regex.IsMatch(text, "comprovante de pagamento|pagamento efetuado", RegexOptions.IgnoreCase) ? "PAYMENT_RECEIPT" :
        Regex.IsMatch(text, "relatório de UST|quantidade de UST", RegexOptions.IgnoreCase) ? "UST_REPORT" :
        Regex.IsMatch(text, "ordem de serviço", RegexOptions.IgnoreCase) ? "SERVICE_ORDER" :
        Regex.IsMatch(text, "medição", RegexOptions.IgnoreCase) ? "MEASUREMENT" :
        Regex.IsMatch(text, "contrato", RegexOptions.IgnoreCase) ? "CONTRACT" :
        Regex.IsMatch(text, "recibo", RegexOptions.IgnoreCase) ? "RECEIPT" :
        Regex.IsMatch(text, "fatura", RegexOptions.IgnoreCase) ? "BILL" : "BILLING_DOCUMENT";

    [GeneratedRegex(@"(?i)\b((?:\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})|(?:\d{3}\.?\d{3}\.?\d{3}-?\d{2}))\b")] private static partial Regex DocumentRegex();
    [GeneratedRegex(@"(?im)(?:fornecedor|prestador|raz[ãa]o social|benefici[áa]rio)\s*[:#-]?\s*([^\r\n]{3,120})")] private static partial Regex SupplierRegex();
    [GeneratedRegex(@"(?i)(?:nota\s*fiscal|nf-e|n[úu]mero\s*(?:da\s*)?nota)\s*(?:n[º°o.]|:|-)*\s*([A-Z0-9./-]+)")] private static partial Regex InvoiceRegex();
    [GeneratedRegex(@"(?i)s[ée]rie\s*[:#-]?\s*([A-Z0-9.-]+)")] private static partial Regex SeriesRegex();
    [GeneratedRegex(@"(?i)compet[êe]ncia\s*[:#-]?\s*(\d{2}/\d{4})")] private static partial Regex CompetenceRegex();
    [GeneratedRegex(@"(?i)contrato\s*(?:n[º°o.]|:|-)*\s*([A-Z0-9./-]+)")] private static partial Regex ContractRegex();
    [GeneratedRegex(@"(?i)(?:ordem\s+de\s+(?:servi[çc]o|compra)|O[SC])\s*(?:n[º°o.]|:|-)*\s*([A-Z0-9./-]+)")] private static partial Regex PurchaseOrderRegex();
    [GeneratedRegex(@"(?i)centro\s+de\s+custo\s*[:#-]?\s*([^\r\n]+)")] private static partial Regex CostCenterRegex();
    [GeneratedRegex(@"(?i)(?:quantidade|qtd)\s*(?:de\s*)?UST\s*[:#-]?\s*([\d.,]+)")] private static partial Regex UstQuantityRegex();
    [GeneratedRegex(@"(?i)(?:valor\s+unit[áa]rio|unit[áa]rio)\s*(?:da\s*)?UST\s*[:R$ -]*([\d.,]+)")] private static partial Regex UstValueRegex();
    [GeneratedRegex(@"(?i)(?:valor\s+total|total\s+(?:geral|da\s+nota))\s*[:R$ -]*([\d.,]+)")] private static partial Regex TotalRegex();
    [GeneratedRegex(@"R\$\s*([\d.]+,\d{2})")] private static partial Regex MoneyRegex();
    [GeneratedRegex(@"(?i)valor\s+l[íi]quido\s*[:R$ -]*([\d.,]+)")] private static partial Regex NetRegex();
    [GeneratedRegex(@"(?i)(?:emiss[ãa]o|data\s+de\s+emiss[ãa]o)\s*[: -]*(\d{2}[/.-]\d{2}[/.-]\d{4}|\d{4}-\d{2}-\d{2})")] private static partial Regex IssueDateRegex();
    [GeneratedRegex(@"(?i)vencimento\s*[: -]*(\d{2}[/.-]\d{2}[/.-]\d{4}|\d{4}-\d{2}-\d{2})")] private static partial Regex DueDateRegex();
    [GeneratedRegex(@"(?i)ISS\s*[:R$ -]*([\d.,]+)")] private static partial Regex IssRegex();
    [GeneratedRegex(@"(?i)INSS\s*[:R$ -]*([\d.,]+)")] private static partial Regex InssRegex();
    [GeneratedRegex(@"(?i)PIS\s*[:R$ -]*([\d.,]+)")] private static partial Regex PisRegex();
    [GeneratedRegex(@"(?i)COFINS\s*[:R$ -]*([\d.,]+)")] private static partial Regex CofinsRegex();
    [GeneratedRegex(@"(?i)(?:IRRF|IR)\s*[:R$ -]*([\d.,]+)")] private static partial Regex IrRegex();
    [GeneratedRegex(@"(?i)CSLL\s*[:R$ -]*([\d.,]+)")] private static partial Regex CsllRegex();
    [GeneratedRegex(@"(?i)nota\s*fiscal|nf-e|fatura|cobran[çc]a|boleto|recibo|medi[çc][ãa]o|valor\s+total|vencimento|CNPJ|UST|ordem\s+de\s+servi[çc]o")] private static partial Regex FinancialWords();
}
