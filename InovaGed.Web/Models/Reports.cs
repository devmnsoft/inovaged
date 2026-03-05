// =============================================================
// InovaGed.Web/Models/Reports/ReportsViewModels.cs
// =============================================================

namespace InovaGed.Web.Models.Reports;

// Usado por PcdFull.cshtml e TtdFull.cshtml
public sealed record TtdRow(
    Guid Id,
    string ClassCode,
    string ClassName,
    int CurrentDays,
    int IntermediateDays,
    int? ActiveMonths,
    int? ArchiveMonths,
    string? FinalDestination,
    string? StartEvent,
    string? Notes);

// Alias mantido para compatibilidade
public sealed record PcdRow(
    Guid Id,
    string Code,
    string Title,
    Guid? ParentId,
    string? Description);

// Usado por Loans.cshtml
public sealed record LoanReportRow(
    long ProtocolNo,
    string RequesterName,
    DateTime RequestedAt,
    DateTime DueAt,
    string Status,
    string DocumentCode,
    string DocumentTitle);

// Usado por SignatureValidation.cshtml — Item 21
public sealed record SignatureValidationRow(
    string DocumentCode,
    string DocumentTitle,
    string Status,
    DateTime SigningTime,
    string SignedByName,
    string Cpf,
    string? Details);

// Alias de compatibilidade
public sealed record SignatureRow(
    string DocumentCode,
    string DocumentTitle,
    string Status,
    DateTime SigningTime,
    string SignedByName,
    string Cpf,
    string? Details);

// Usado pela tela de seleção do item 26
public sealed record SignedSetSelectVM(
    List<SignedDocRow> SignedDocuments);

public sealed class SignedDocRow
{
    public Guid DocumentId { get; init; }
    public string DocumentCode { get; init; } = "";
    public string DocumentTitle { get; init; } = "";
    public string? SignerName { get; init; }
    public string? Cpf { get; init; }
    public DateTime? SigningTime { get; init; }
    public string SigStatus { get; init; } = "";
    public string? SigDetails { get; init; }
}

// Usado pela view de impressão — Item 26
public sealed record SignedSetPrintVm(
    Guid RunId,
    DateTime GeneratedAt,
    List<SignedSetPrintItem> Items);

// Classe (não record posicional) para Dapper mapear por propriedade sem depender
// da ordem/tipo exato do construtor. SeqNo é long porque ROW_NUMBER() retorna bigint.
public sealed class SignedSetPrintItem
{
    public long SeqNo { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentCode { get; init; } = "";
    public string DocumentTitle { get; init; } = "";
    public string? SignerName { get; init; }
    public string? Cpf { get; init; }
    public DateTime? SigningTime { get; init; }
    public string SigStatus { get; init; } = "";
    public string? SigDetails { get; init; }
    public DateTime ValidatedAt { get; init; }
}