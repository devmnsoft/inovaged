// =============================================================
// InovaGed.Web/Models/Reports/ReportsViewModels.cs
// =============================================================

namespace InovaGed.Web.Models.Reports;

// Usado por PcdFull.cshtml e TtdFull.cshtml
public sealed class TtdRow
{
    public TtdRow() { }
    public TtdRow(Guid Id, string ClassCode, string ClassName, int CurrentDays, int IntermediateDays, int? ActiveMonths, int? ArchiveMonths, string? FinalDestination, string? StartEvent, string? Notes)
    {
        this.Id = Id;
        this.ClassCode = ClassCode;
        this.ClassName = ClassName;
        this.CurrentDays = CurrentDays;
        this.IntermediateDays = IntermediateDays;
        this.ActiveMonths = ActiveMonths;
        this.ArchiveMonths = ArchiveMonths;
        this.FinalDestination = FinalDestination;
        this.StartEvent = StartEvent;
        this.Notes = Notes;
    }
    public Guid Id { get; set; }
    public string ClassCode { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int CurrentDays { get; set; }
    public int IntermediateDays { get; set; }
    public int? ActiveMonths { get; set; }
    public int? ArchiveMonths { get; set; }
    public string? FinalDestination { get; set; }
    public string? StartEvent { get; set; }
    public string? Notes { get; set; }
}

// Alias mantido para compatibilidade
public sealed record PcdRow(
    Guid Id,
    string Code,
    string Title,
    Guid? ParentId,
    string? Description);

// Usado por Loans.cshtml
public sealed class LoanReportRow
{
    public LoanReportRow() { }
    public LoanReportRow(long ProtocolNo, string RequesterName, DateTime RequestedAt, DateTime DueAt, string Status, string DocumentCode, string DocumentTitle)
    {
        this.ProtocolNo = ProtocolNo;
        this.RequesterName = RequesterName;
        this.RequestedAt = RequestedAt;
        this.DueAt = DueAt;
        this.Status = Status;
        this.DocumentCode = DocumentCode;
        this.DocumentTitle = DocumentTitle;
    }
    public long ProtocolNo { get; set; }
    public string RequesterName { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime DueAt { get; set; }
    public string Status { get; set; } = "";
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
}

// Usado por SignatureValidation.cshtml — Item 21
public sealed class SignatureValidationRow
{
    public SignatureValidationRow() { }
    public SignatureValidationRow(string DocumentCode, string DocumentTitle, string Status, DateTime SigningTime, string SignedByName, string Cpf, string? Details)
    {
        this.DocumentCode = DocumentCode;
        this.DocumentTitle = DocumentTitle;
        this.Status = Status;
        this.SigningTime = SigningTime;
        this.SignedByName = SignedByName;
        this.Cpf = Cpf;
        this.Details = Details;
    }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime SigningTime { get; set; }
    public string SignedByName { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string? Details { get; set; }
}

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