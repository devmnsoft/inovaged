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
    public BillingExtractionService(IDbConnectionFactory db, IBillingCommands commands) { _db = db; _commands = commands; }

    public bool LooksFinancial(string text) => FinancialWords().IsMatch(text ?? "");

    public BillingExtractionDto Extract(BillingExtractionCandidate candidate)
    {
        var text = candidate.Text ?? "";
        var result = new BillingExtractionDto { DocumentId = candidate.DocumentId, DocumentVersionId = candidate.DocumentVersionId, ExtractionStatus = "PENDING_REVIEW", Confidence = 25 };
        result.SupplierDocument = Match(text, DocumentRegex());
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
        var found = new object?[] { result.SupplierDocument, result.InvoiceNumber, result.GrossAmount, result.IssueDate, result.DueDate, result.ContractNumber }.Count(x => x is not null);
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
        var extraction = Extract(candidate);
        await _commands.SaveExtractionAsync(tenantId, extraction, ct);
        return extraction;
    }

    private static string? Match(string text, Regex regex) { var m = regex.Match(text); return m.Success ? m.Groups[1].Value.Trim() : null; }
    private static decimal? Money(string? value) => decimal.TryParse(value?.Replace(".", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static DateTime? Date(string? value) => DateTime.TryParseExact(value, new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string Kind(string text) => Regex.IsMatch(text, "nota fiscal|nf-e", RegexOptions.IgnoreCase) ? "INVOICE" : Regex.IsMatch(text, "medição", RegexOptions.IgnoreCase) ? "MEASUREMENT" : Regex.IsMatch(text, "contrato", RegexOptions.IgnoreCase) ? "CONTRACT" : Regex.IsMatch(text, "recibo", RegexOptions.IgnoreCase) ? "RECEIPT" : "BILLING_DOCUMENT";

    [GeneratedRegex(@"(?i)\b((?:\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})|(?:\d{3}\.?\d{3}\.?\d{3}-?\d{2}))\b")] private static partial Regex DocumentRegex();
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
