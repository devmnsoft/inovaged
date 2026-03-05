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

// Usado por SignedSetPrint.cshtml — Item 26
public sealed record SignedSetPrintVm(
    Guid RunId,
    DateTime GeneratedAt,
    List<SignatureValidationRow> Items);