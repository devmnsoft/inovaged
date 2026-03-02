using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionTerms;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.RetentionTerms;

public sealed class RetentionTermRepository : IRetentionTermRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionTermRepository> _logger;
    private readonly IRetentionAuditWriter _audit;

    public RetentionTermRepository(
        IDbConnectionFactory db,
        ILogger<RetentionTermRepository> logger,
        IRetentionAuditWriter audit)
    {
        _db = db;
        _logger = logger;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RetentionTermRow>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            const string sql = """
            select
              id         as "Id",
              term_no    as "TermNo",
              case_id    as "CaseId",
              term_type  as "TermType",
              status     as "Status",
              created_at as "CreatedAt"
            from ged.retention_term
            where tenant_id=@tenantId
            order by created_at desc
            limit 200;
            """;

            await using var conn = await _db.OpenAsync(ct);
            var rows = (await conn.QueryAsync<RetentionTermRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).ToList();

            _logger.LogInformation("RetentionTerm.ListAsync OK. Tenant={TenantId} Rows={Rows}", tenantId, rows.Count);
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.ListAsync ERROR. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    // ✅ NOVO: versão com filtros (para seu Controller)
    public async Task<IReadOnlyList<RetentionTermRow>> ListAsync(
        Guid tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? status,
        CancellationToken ct)
    {
        try
        {
            var sql = """
            select
              id         as "Id",
              term_no    as "TermNo",
              case_id    as "CaseId",
              term_type  as "TermType",
              status     as "Status",
              created_at as "CreatedAt"
            from ged.retention_term
            where tenant_id=@tenantId
            """;

            var p = new DynamicParameters();
            p.Add("tenantId", tenantId);

            if (from is not null)
            {
                sql += " and created_at >= @from ";
                p.Add("from", from);
            }
            if (to is not null)
            {
                sql += " and created_at < @to ";
                p.Add("to", to);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                sql += " and status = @status ";
                p.Add("status", status.Trim());
            }

            sql += " order by created_at desc limit 500;";

            await using var conn = await _db.OpenAsync(ct);
            var rows = (await conn.QueryAsync<RetentionTermRow>(
                new CommandDefinition(sql, p, cancellationToken: ct)
            )).ToList();

            _logger.LogInformation(
                "RetentionTerm.ListAsync(filters) OK. Tenant={TenantId} From={From} To={To} Status={Status} Rows={Rows}",
                tenantId, from, to, status, rows.Count);

            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RetentionTerm.ListAsync(filters) ERROR. Tenant={TenantId} From={From} To={To} Status={Status}",
                tenantId, from, to, status);

            throw;
        }
    }

    public async Task<(RetentionTermRow Term, string Html)?> GetAsync(Guid tenantId, Guid termId, CancellationToken ct)
    {
        try
        {
            const string sql = """
            select
              id           as "Id",
              term_no      as "TermNo",
              case_id      as "CaseId",
              term_type    as "TermType",
              status       as "Status",
              created_at   as "CreatedAt",
              content_html as "Html"
            from ged.retention_term
            where tenant_id=@tenantId and id=@termId
            limit 1;
            """;

            await using var conn = await _db.OpenAsync(ct);

            var row = await conn.QueryFirstOrDefaultAsync<TermWithHtml>(
                new CommandDefinition(sql, new { tenantId, termId }, cancellationToken: ct));

            if (row is null)
            {
                _logger.LogWarning("RetentionTerm.GetAsync NOT FOUND. Tenant={TenantId} Term={TermId}", tenantId, termId);
                return null;
            }

            var term = new RetentionTermRow
            {
                Id = row.Id,
                TermNo = row.TermNo,
                CaseId = row.CaseId,
                TermType = row.TermType,
                Status = row.Status,
                CreatedAt = row.CreatedAt
            };
            _logger.LogInformation("RetentionTerm.GetAsync OK. Tenant={TenantId} Term={TermId} TermNo={TermNo}", tenantId, termId, row.TermNo);

            return (term, row.Html ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.GetAsync ERROR. Tenant={TenantId} Term={TermId}", tenantId, termId);
            throw;
        }
    }

    public async Task<Guid> CreateFromCaseAsync(Guid tenantId, Guid userId, CreateTermRequest req, CancellationToken ct)
    {
        var termId = Guid.NewGuid();

        const string sqlNextNo = @"select coalesce(max(term_no),0)+1 from ged.retention_term where tenant_id=@tenantId;";

        const string sqlItems = """
        select
          i.doc_code, i.doc_title, i.classification_code, i.classification_name,
          i.retention_due_at, i.suggested_destination, i.document_id
        from ged.retention_case_item i
        where i.tenant_id=@tenantId and i.case_id=@caseId
          and i.decision='APPROVE'
        order by i.doc_title;
        """;

        const string sqlInsert = """
        insert into ged.retention_term(
          id, tenant_id, term_no, case_id, term_type, status,
          content_html, content_hash_sha256,
          created_at, created_by, notes
        )
        values (
          @id, @tenantId, @termNo, @caseId, @termType, 'DRAFT',
          @html, @hash,
          now(), @userId, @notes
        );
        """;

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var termNo = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sqlNextNo, new { tenantId }, tx, cancellationToken: ct));

            var items = (await conn.QueryAsync(
                new CommandDefinition(sqlItems, new { tenantId, caseId = req.CaseId }, tx, cancellationToken: ct)
            )).ToList();

            if (items.Count == 0)
                throw new InvalidOperationException("O caso não possui itens aprovados (APPROVE).");

            var html = BuildTermHtml(termNo, req.TermType, req.CaseId, items);
            var hash = Sha256Hex(html);

            await conn.ExecuteAsync(new CommandDefinition(sqlInsert, new
            {
                id = termId,
                tenantId,
                termNo,
                caseId = req.CaseId,
                termType = req.TermType,
                html,
                hash,
                userId,
                notes = req.Notes
            }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            await _audit.WriteAsync(tenantId, userId, req.CaseId, "TERM_CREATED", $"TermNo={termNo} TermId={termId}", ct);

            _logger.LogInformation(
                "RetentionTerm.CreateFromCaseAsync OK. Tenant={TenantId} Case={CaseId} TermId={TermId} TermNo={TermNo} Items={Items}",
                tenantId, req.CaseId, termId, termNo, items.Count);

            return termId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.CreateFromCaseAsync ERROR. Tenant={TenantId} Case={CaseId}", tenantId, req.CaseId);
            throw;
        }
    }

    public async Task MarkReadyToSignAsync(Guid tenantId, Guid userId, Guid termId, CancellationToken ct)
    {
        try
        {
            const string sql = """
            update ged.retention_term
            set status='READY_TO_SIGN'
            where tenant_id=@tenantId and id=@termId and status in ('DRAFT','READY_TO_SIGN');
            """;

            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, termId }, cancellationToken: ct));

            if (rows == 0) throw new InvalidOperationException("Termo não encontrado ou status inválido.");

            await _audit.WriteAsync(tenantId, userId, termId, "TERM_READY_TO_SIGN", null, ct);

            _logger.LogInformation("RetentionTerm.MarkReadyToSignAsync OK. Tenant={TenantId} Term={TermId}", tenantId, termId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.MarkReadyToSignAsync ERROR. Tenant={TenantId} Term={TermId}", tenantId, termId);
            throw;
        }
    }

    public async Task SignAsync(Guid tenantId, Guid userId, SignTermRequest req, CancellationToken ct)
    {
        try
        {
            const string sqlGet = @"select content_hash_sha256 from ged.retention_term where tenant_id=@tenantId and id=@termId limit 1;";

            const string sqlInsSig = """
            insert into ged.retention_term_signature(
              tenant_id, term_id,
              signer_name, signer_role, signer_document,
              signed_at, signature_hash_sha256, signature_provider, meta
            ) values (
              @tenantId, @termId,
              @name, @role, @doc,
              now(), @sigHash, 'INTERNAL', @meta::jsonb
            );
            """;

            const string sqlMarkSigned = """
            update ged.retention_term
            set status='SIGNED',
                signed_at=now(),
                signed_by=@userId
            where tenant_id=@tenantId and id=@termId and status in ('READY_TO_SIGN','DRAFT');
            """;

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var termHash = await conn.ExecuteScalarAsync<string>(
                new CommandDefinition(sqlGet, new { tenantId, termId = req.TermId }, tx, cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(termHash))
                throw new InvalidOperationException("Termo inválido.");

            var payload = $"{termHash}|{req.SignerName}|{req.SignerRole}|{req.SignerDocument}|{DateTimeOffset.UtcNow:O}";
            var sigHash = Sha256Hex(payload);

            var meta = "{\"mode\":\"internal\",\"note\":\"assinatura interna (placeholder)\"}";

            await conn.ExecuteAsync(new CommandDefinition(sqlInsSig, new
            {
                tenantId,
                termId = req.TermId,
                name = req.SignerName.Trim(),
                role = req.SignerRole,
                doc = req.SignerDocument,
                sigHash,
                meta
            }, tx, cancellationToken: ct));

            var rows = await conn.ExecuteAsync(new CommandDefinition(sqlMarkSigned, new
            {
                tenantId,
                termId = req.TermId,
                userId
            }, tx, cancellationToken: ct));

            if (rows == 0)
                throw new InvalidOperationException("Não foi possível marcar como SIGNED.");

            await tx.CommitAsync(ct);

            await _audit.WriteAsync(tenantId, userId, req.TermId, "TERM_SIGNED", $"Signer={req.SignerName}", ct);

            _logger.LogInformation("RetentionTerm.SignAsync OK. Tenant={TenantId} Term={TermId} Signer={Signer}",
                tenantId, req.TermId, req.SignerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.SignAsync ERROR. Tenant={TenantId} Term={TermId}", tenantId, req.TermId);
            throw;
        }
    }

    public async Task ExecuteFinalAsync(Guid tenantId, Guid userId, Guid termId, CancellationToken ct)
    {
        try
        {
            const string sqlTerm = """
            select case_id as "CaseId", term_type as "TermType", status as "Status"
            from ged.retention_term
            where tenant_id=@tenantId and id=@termId
            limit 1;
            """;

            const string sqlDocs = """
            select i.document_id as "DocumentId", i.suggested_destination as "SuggestedDestination"
            from ged.retention_case_item i
            join ged.retention_term t
              on t.tenant_id=i.tenant_id and t.case_id=i.case_id
            where t.tenant_id=@tenantId and t.id=@termId
              and i.decision='APPROVE';
            """;

            const string sqlUpdateDocDisposed = """
            update ged.document
            set disposition_status='DISPOSED',
                disposition_case_id = (select case_id from ged.retention_term where tenant_id=@tenantId and id=@termId),
                disposition_at = now(),
                disposition_by = @userId
            where tenant_id=@tenantId and id=@docId;
            """;

            const string sqlMarkTerm = """
            update ged.retention_term
            set status='EXECUTED',
                executed_at=now(),
                executed_by=@userId
            where tenant_id=@tenantId and id=@termId;
            """;

            const string sqlMarkCase = """
            update ged.retention_case
            set status='EXECUTED',
                closed_at=coalesce(closed_at, now()),
                closed_by=coalesce(closed_by, @userId)
            where tenant_id=@tenantId and id=@caseId;
            """;

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var t = await conn.QueryFirstOrDefaultAsync<TermHeader>(
                new CommandDefinition(sqlTerm, new { tenantId, termId }, tx, cancellationToken: ct));

            if (t is null) throw new InvalidOperationException("Termo não encontrado.");
            if (!string.Equals(t.Status, "SIGNED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Termo precisa estar SIGNED para executar.");

            var docs = (await conn.QueryAsync<DocToDispose>(
                new CommandDefinition(sqlDocs, new { tenantId, termId }, tx, cancellationToken: ct))).ToList();

            if (docs.Count == 0) throw new InvalidOperationException("Nenhum documento aprovado no termo/caso.");

            foreach (var d in docs)
            {
                await conn.ExecuteAsync(new CommandDefinition(sqlUpdateDocDisposed, new
                {
                    tenantId,
                    termId,
                    userId,
                    docId = d.DocumentId
                }, tx, cancellationToken: ct));
            }

            await conn.ExecuteAsync(new CommandDefinition(sqlMarkTerm, new { tenantId, termId, userId }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlMarkCase, new { tenantId, caseId = t.CaseId, userId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            await _audit.WriteAsync(tenantId, userId, termId, "TERM_EXECUTED", $"Docs={docs.Count}", ct);

            _logger.LogInformation("RetentionTerm.ExecuteFinalAsync OK. Tenant={TenantId} Term={TermId} Case={CaseId} Docs={Docs}",
                tenantId, termId, t.CaseId, docs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionTerm.ExecuteFinalAsync ERROR. Tenant={TenantId} Term={TermId}", tenantId, termId);
            throw;
        }
    }

    // ===== Helpers / DTOs internos =====

    private sealed class TermWithHtml
    {
        public Guid Id { get; init; }
        public int TermNo { get; init; }
        public Guid CaseId { get; init; }
        public string TermType { get; init; } = "";
        public string Status { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public string? Html { get; init; }
    }

    private sealed class TermHeader
    {
        public Guid CaseId { get; init; }
        public string TermType { get; init; } = "";
        public string Status { get; init; } = "";
    }

    private sealed class DocToDispose
    {
        public Guid DocumentId { get; init; }
        public string? SuggestedDestination { get; init; }
    }

    private static string BuildTermHtml(int termNo, string termType, Guid caseId, List<dynamic> items)
    {
        var sb = new StringBuilder();
        sb.Append("""
        <!doctype html><html><head><meta charset='utf-8'/>
        <title>Termo</title>
        <style>
        body{font-family:Arial,Helvetica,sans-serif;font-size:12pt;line-height:1.35}
        h1{font-size:16pt;margin:0 0 6px}
        .small{font-size:10pt;color:#444}
        table{width:100%;border-collapse:collapse;margin-top:12px}
        th,td{border:1px solid #999;padding:6px;vertical-align:top}
        th{background:#f2f2f2}
        hr{margin:16px 0}
        </style>
        </head><body>
        """);

        sb.Append($"<div class='small'>INOVAGED • Termo Nº {termNo:0000} • Tipo: {termType} • Caso: {caseId}</div>");
        sb.Append($"<h1>TERMO DE {(termType == "ELIMINATION" ? "ELIMINAÇÃO" : termType == "TRANSFER" ? "TRANSFERÊNCIA" : "RECOLHIMENTO")} DE DOCUMENTOS</h1>");

        sb.Append("""
        <p>
        Nos termos das normas internas e do Plano de Classificação e Tabela de Temporalidade (PCD/TTD) vigente,
        este termo consolida os documentos selecionados para destinação, com base nos prazos de guarda calculados pelo sistema.
        </p>

        <p><strong>Responsáveis:</strong> ________________________________________________</p>
        <p><strong>Unidade/Setor:</strong> ________________________________________________</p>

        <hr/>
        <h3 style='font-size:13pt;margin:0'>Relação de Documentos</h3>
        """);

        sb.Append("<table><thead><tr>");
        sb.Append("<th style='width:12%'>Código</th>");
        sb.Append("<th>Título</th>");
        sb.Append("<th style='width:18%'>Classificação</th>");
        sb.Append("<th style='width:12%'>Vencimento</th>");
        sb.Append("<th style='width:14%'>Destino</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var i in items)
        {
            string code = i.doc_code ?? "";
            string title = i.doc_title ?? "";
            string cls = $"{i.classification_code} - {i.classification_name}";
            string due = i.retention_due_at is null ? "—" : ((DateTimeOffset)i.retention_due_at).ToString("dd/MM/yyyy");
            string dest = i.suggested_destination ?? "REAVALIAR";

            sb.Append("<tr>");
            sb.Append($"<td>{Html(code)}</td>");
            sb.Append($"<td>{Html(title)}</td>");
            sb.Append($"<td>{Html(cls)}</td>");
            sb.Append($"<td>{Html(due)}</td>");
            sb.Append($"<td>{Html(dest)}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("""
        <hr/>
        <p>
        <strong>Declaração:</strong> Após verificação de impedimentos (HOLD, sigilo e pendências), os documentos acima
        estão aptos para destinação conforme indicado.
        </p>

        <p>Local e Data: _____________________________</p>
        <p>Assinatura: ________________________________</p>

        </body></html>
        """);

        return sb.ToString();
    }

    private static string Html(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}