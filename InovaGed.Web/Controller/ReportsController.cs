using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class ReportsController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICertificateValidationService _sigValidator;

    public ReportsController(IDbConnectionFactory db, ICertificateValidationService sigValidator)
    {
        _db = db;
        _sigValidator = sigValidator;
    }

    // =========================================================
    // PLC/PCD - Relatório completo
    // GET /Reports/PcdFull
    // =========================================================
    [HttpGet("PcdFull")]
    public async Task<IActionResult> PcdFull(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<PcdRow>(
            """
            select
              id,
              code,
              name as "Title",
              parent_id as "ParentId",
              description as "Description"
            from ged.classification_plan
            where tenant_id = @tenant
            order by code
            """,
            new { tenant = TenantId() }
        )).ToList();

        ViewData["Title"] = "Imprimir PLC/PCD (Completo)";
        ViewData["Subtitle"] = "Plano de Classificação Documental - PoC";

        return View("PcdFull", rows);
    }

    // =========================================================
    // PLC/PCD - por Classe
    // GET /Reports/PcdByClass
    // POST /Reports/PcdByClass
    // =========================================================
    [HttpGet("PcdByClass")]
    public IActionResult PcdByClass()
    {
        ViewData["Title"] = "Imprimir PLC/PCD (por Classe)";
        ViewData["Subtitle"] = "Informe o código (ex.: PCD-001)";
        return View("PcdByClass");
    }

    [HttpPost("PcdByClass")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PcdByClass(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["err"] = "Informe um código (ex.: PCD-001).";
            return RedirectToAction(nameof(PcdByClass));
        }

        await using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<PcdRow>(
            """
            select
              id,
              code,
              name as "Title",
              parent_id as "ParentId",
              description as "Description"
            from ged.classification_plan
            where tenant_id = @tenant
              and (code = @code or code like (@code || '.%'))
            order by code
            """,
            new { tenant = TenantId(), code }
        )).ToList();

        ViewData["Title"] = "Imprimir PLC/PCD (por Classe)";
        ViewData["Subtitle"] = $"Filtro: {code}";

        return View("PcdFull", rows);
    }

    // =========================================================
    // TTD - Completo
    // GET /Reports/TtdFull
    // =========================================================
    [HttpGet("TtdFull")]
    public async Task<IActionResult> TtdFull(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        const string sql = """
        select
          rr.id                                  as "Id",
          rr.class_code                          as "ClassCode",
          coalesce(cp.name, rr.class_code)       as "ClassName",
          rr.current_days                        as "CurrentDays",
          rr.intermediate_days                   as "IntermediateDays",
          ceil(rr.current_days / 30.0)::int      as "ActiveMonths",
          ceil(rr.intermediate_days / 30.0)::int as "ArchiveMonths",
          rr.final_destination                   as "FinalDestination",
          rr.start_event                         as "StartEvent",
          coalesce(rr.notes,'')                  as "Notes"
        from ged.retention_rule rr
        left join ged.classification_plan cp
          on cp.tenant_id = rr.tenant_id
         and cp.code = rr.class_code
        where rr.tenant_id = @tenant
          and rr.reg_status = 'A'
        order by rr.class_code;
        """;

        var rows = (await conn.QueryAsync<TtdRow>(sql, new { tenant = TenantId() })).ToList();

        ViewData["Title"] = "Imprimir TTD (Completo)";
        ViewData["Subtitle"] = "Tabela de Temporalidade e Destinação - PoC";

        return View("TtdFull", rows);
    }

    [HttpGet("TtdByClass")]
    public async Task<IActionResult> TtdByClass(CancellationToken ct)
    {
        ViewData["Title"] = "Imprimir TTD (por Classe)";
        ViewData["Subtitle"] = "Informe o ClassCode (ex.: PCD-001)";

        await using var conn = await _db.OpenAsync(ct);

        // só para ajudar o usuário a escolher (preview)
        var examples = (await conn.QueryAsync<string>(
            """
        select distinct rr.class_code
        from ged.retention_rule rr
        where rr.tenant_id = @tenant
          and rr.reg_status = 'A'
        order by rr.class_code
        limit 30
        """,
            new { tenant = TenantId() }
        )).ToList();

        return View("TtdByClass", examples);
    }

    [HttpPost("TtdByClass")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TtdByClassPost(string classCode, CancellationToken ct)
    {
        classCode = (classCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(classCode))
        {
            TempData["err"] = "Informe um ClassCode (ex.: PCD-001).";
            return RedirectToAction(nameof(TtdByClass));
        }

        await using var conn = await _db.OpenAsync(ct);

        const string sql = """
        select
          rr.id                                  as "Id",
          rr.class_code                          as "ClassCode",
          coalesce(cp.name, rr.class_code)       as "ClassName",
          rr.current_days                        as "CurrentDays",
          rr.intermediate_days                   as "IntermediateDays",
          ceil(rr.current_days / 30.0)::int      as "ActiveMonths",
          ceil(rr.intermediate_days / 30.0)::int as "ArchiveMonths",
          rr.final_destination                   as "FinalDestination",
          rr.start_event                         as "StartEvent",
          coalesce(rr.notes,'')                  as "Notes"
        from ged.retention_rule rr
        left join ged.classification_plan cp
          on cp.tenant_id = rr.tenant_id
         and cp.code = rr.class_code
        where rr.tenant_id = @tenant
          and rr.reg_status = 'A'
          and rr.class_code = @classCode
        order by rr.class_code;
        """;

        var rows = (await conn.QueryAsync<TtdRow>(
            sql, new { tenant = TenantId(), classCode }
        )).ToList();

        ViewData["Title"] = "Imprimir TTD (por Classe)";
        ViewData["Subtitle"] = $"Filtro: {classCode}";

        return View("TtdFull", rows);
    }

    // =========================================================
    // Empréstimos - Relatório
    // GET /Reports/Loans
    // =========================================================
    [HttpGet("Loans")]
    public async Task<IActionResult> Loans(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<LoanReportRow>(
            """
            select
              protocol_no    as "ProtocolNo",
              requester_name as "RequesterName",
              requested_at   as "RequestedAt",
              due_at         as "DueAt",
              status         as "Status",
              document_code  as "DocumentCode",
              document_title as "DocumentTitle"
            from ged.vw_loan_report
            where tenant_id = @tenant
            order by requested_at desc
            """,
            new { tenant = TenantId() }
        )).ToList();

        ViewData["Title"] = "Relatório de Empréstimos";
        ViewData["Subtitle"] = "Por solicitante/período/tipo (PoC)";

        return View("Loans", rows);
    }

    // =========================================================
    // Assinatura ICP - Validação
    // GET /Reports/SignatureValidation
    // =========================================================
    [HttpGet("SignatureValidation")]
    public async Task<IActionResult> SignatureValidation(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<SignatureValidationRow>(
            """
            select
              d.code  as "DocumentCode",
              d.title as "DocumentTitle",

              coalesce(s.status::text, 'PENDENTE') as "Status",
              s.signing_time as "SigningTime",
              coalesce(s.signed_by_name, u.name, u.email, 'Usuário') as "SignedByName",
              coalesce(s.cpf, '') as "Cpf",
              coalesce(s.status_details, null::text) as "Details"
            from ged.document d
            left join ged.document_signature s
              on s.document_id = d.id
             and s.tenant_id   = d.tenant_id
             and s.reg_status  = 'A'
            left join ged.app_user u
              on u.id        = s.signed_by
             and u.tenant_id = d.tenant_id
            where d.tenant_id = @tenant
            order by
              s.signing_time desc nulls last,
              d.created_at   desc
            limit 500
            """,
            new { tenant = TenantId() }
        )).ToList();

        ViewData["Title"] = "Validação de Assinaturas";
        ViewData["Subtitle"] = "ICP-Brasil: status e dados básicos (PoC)";

        return View("SignatureValidation", rows);
    }

    // =========================================================
    // Relatório Conjunto Assinado (Item 26)
    // GET /Reports/SignedSet
    // POST /Reports/SignedSet/Run
    // =========================================================
    [HttpGet("SignedSet")]
    public IActionResult SignedSet() => View();

    [HttpPost("SignedSet/Run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignedSetRun(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var runId = Guid.NewGuid();

        // IMPORTANTE:
        // - ged.document NÃO tem reg_status
        // - ged.document_signature TEM reg_status
        var docs = (await conn.QueryAsync<(Guid DocId, string Title, byte[] SigBytes)>(
            """
            select d.id as "DocId", d.title as "Title", s.signature_bytes as "SigBytes"
            from ged.document d
            join ged.document_signature s
              on s.document_id = d.id
             and s.tenant_id   = d.tenant_id
             and s.reg_status  = 'A'
            where d.tenant_id = @tenant
              and (@from is null or d.created_at >= @from)
              and (@to   is null or d.created_at < (@to + interval '1 day'))
            order by d.created_at asc
            """,
            new { tenant = TenantId(), from, to },
            tx
        )).ToList();

        var items = new List<SignedSetRow>();

        foreach (var d in docs)
        {
            var val = await _sigValidator.ValidateSignatureAsync(TenantId(), d.SigBytes, ct);
            items.Add(new SignedSetRow(d.DocId, d.Title, val.Status, val.Details));
        }

        // Se suas tabelas report_* já existem e têm reg_date/reg_status, ok.
        // Se não existirem ainda, eu te passo o CREATE também.
        await conn.ExecuteAsync(
            """
            insert into ged.report_run(id, tenant_id, report_type, created_at, created_by, reg_date, reg_status)
            values (@id, @tenant, 'SIGNED_SET', now(), @userId, now(), 'A');
            """,
            new { id = runId, tenant = TenantId(), userId = UserId() },
            tx
        );

        foreach (var it in items)
        {
            await conn.ExecuteAsync(
                """
                insert into ged.report_print_item
                (id, tenant_id, run_id, document_id, validation_status, validation_details, reg_date, reg_status)
                values
                (gen_random_uuid(), @tenant, @runId, @docId, @st, @dt, now(), 'A');
                """,
                new { tenant = TenantId(), runId, docId = it.DocumentId, st = it.Status, dt = it.Details },
                tx
            );
        }

        await tx.CommitAsync(ct);

        return View("SignedSetPrint", new SignedSetPrintVm(runId, items));
    }

    // ---------------------------------------------------------
    // Helpers (padronizado)
    // ---------------------------------------------------------
    private Guid TenantId() => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId() => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
}

// =============================================================
// ViewModels / DTOs (FORA do controller) -> evita CS1022
// =============================================================
public sealed record PcdRow(Guid Id, string Code, string Title, Guid? ParentId, string? Description);

// Classe (não record) pra evitar problema de materialization do Dapper quando há construtor
public sealed class TtdRow
{
    public Guid Id { get; set; }
    public string ClassCode { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int CurrentDays { get; set; }
    public int IntermediateDays { get; set; }
    public int ActiveMonths { get; set; }
    public int ArchiveMonths { get; set; }
    public string? FinalDestination { get; set; }
    public string? StartEvent { get; set; }
    public string? Notes { get; set; }
}

public sealed record LoanReportRow(
    long ProtocolNo,
    string RequesterName,
    DateTime RequestedAt,
    DateTime DueAt,
    string Status,
    string DocumentCode,
    string DocumentTitle);

public sealed record SignatureValidationRow(
    string DocumentCode,
    string DocumentTitle,
    string Status,
    DateTime? SigningTime,
    string SignedByName,
    string Cpf,
    string? Details);

public sealed record SignedSetRow(Guid DocumentId, string Title, string Status, string? Details);
public sealed record SignedSetPrintVm(Guid RunId, IReadOnlyList<SignedSetRow> Items);