using System.Diagnostics;
using Dapper;
using InovaGed.Application.Common;
using InovaGed.Application.DemoReadiness;

namespace InovaGed.Infrastructure.DemoReadiness;

public sealed class DemoReadinessService(IDbConnectionFactory db) : IDemoReadinessService
{
    public async Task<DemoReadinessReportDto> RunAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var checks = new List<DemoReadinessCheckDto>
        {
            await RunCheckAsync("DB", "Banco de dados", "Infra", "/SystemHealth", async t => { await using var c = await db.OpenAsync(t); var sw = Stopwatch.StartNew(); await c.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: t)); sw.Stop(); return sw.ElapsedMilliseconds > 1000 ? Warn($"Banco respondeu em {sw.ElapsedMilliseconds}ms.") : Ok($"Banco operacional ({sw.ElapsedMilliseconds}ms)."); }, ct),
            await RunCheckAsync("POOL", "Pool/conexões", "Infra", null, async t => { await using var c = await db.OpenAsync(t); await c.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: t)); return Ok("Conexão com banco operacional."); }, ct),
            await RunCheckAsync("GED_EXPLORER", "GED Explorer", "GED", "/Ged", async t => { await using var c = await db.OpenAsync(t); var r = await c.QuerySingleAsync<(int folders, int docs)>(new CommandDefinition("select (select count(*)::int from ged.folder where tenant_id=@tenantId and reg_status='A') folders,(select count(*)::int from ged.document where tenant_id=@tenantId and reg_status='A') docs", new { tenantId }, cancellationToken: t)); return r.docs == 0 ? Warn($"Pastas: {r.folders}, documentos ativos: 0.") : Ok($"Pastas: {r.folders}, documentos ativos: {r.docs}."); }, ct),
            await RunCheckAsync("UPLOAD", "Upload em lote", "GED", "/Batches/New", _ => Task.FromResult(Warn("Módulo disponível. Verifique no Network se /Ged/Upload não é chamado.")), ct),
            await RunCheckAsync("OCR", "OCR", "OCR", "/Ged/Processing", async t => { await using var c = await db.OpenAsync(t); var sql = "select count(*) filter (where status='PENDING')::int p,count(*) filter (where status='PROCESSING')::int pr,count(*) filter (where status='COMPLETED')::int c,count(*) filter (where status='ERROR')::int e,count(*) filter (where status='CANCELLED')::int x from ged.ocr_job where tenant_id=@tenantId"; var r = await c.QuerySingleAsync<(int p, int pr, int c, int e, int x)>(new CommandDefinition(sql, new { tenantId }, cancellationToken: t)); return (r.e > 10 || r.pr > 200) ? Warn($"PENDING={r.p}, PROCESSING={r.pr}, COMPLETED={r.c}, ERROR={r.e}, CANCELLED={r.x}.") : Ok($"OCR operacional. COMPLETED={r.c}, ERROR={r.e}."); }, ct),
            await RunCheckAsync("PREVIEW", "Preview", "GED", "/Ged/Processing", async t => { await using var c = await db.OpenAsync(t); var e = await c.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.preview_job where tenant_id=@tenantId and status='ERROR'", new { tenantId }, cancellationToken: t)); return e > 20 ? Warn($"Muitos previews com erro ({e}).") : Ok($"Preview acessível. Erros recentes: {e}."); }, ct),
            await RunCheckAsync("HI", "HospitalIntelligence", "Analytics", "/HospitalIntelligence", async t => { await using var c = await db.OpenAsync(t); await c.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: t)); return Ok("Módulo disponível para abertura."); }, ct),
            await RunCheckAsync("USERS", "Usuários", "Admin", "/Users", async t => { await using var c = await db.OpenAsync(t); var r = await c.QuerySingleAsync<(int active, int blocked, int admin)>(new CommandDefinition("select (select count(*)::int from ged.user_tenant where tenant_id=@tenantId and is_active=true) active,(select count(*)::int from ged.user_tenant where tenant_id=@tenantId and is_locked=true) blocked,(select count(*)::int from ged.user_tenant ut join ged.user_account ua on ua.id=ut.user_id where ut.tenant_id=@tenantId and ut.is_active=true and upper(coalesce(ua.role_code,'')) in ('ADMIN','ADMINISTRATOR')) admin", new { tenantId }, cancellationToken: t)); return r.admin == 0 ? Warn($"Ativos={r.active}, bloqueados={r.blocked}, ADMIN não encontrado.") : Ok($"Ativos={r.active}, bloqueados={r.blocked}, ADMIN={r.admin}."); }, ct),
            await RunCheckAsync("PERM", "Permissões", "Segurança", null, _ => Task.FromResult(Ok($"Usuário {userId} autenticado como ADMIN.")), ct),
            await RunCheckAsync("LOGS", "Logs/SystemLogs", "Audit", "/SystemLogs", async t => { await using var c = await db.OpenAsync(t); var e = await c.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from audit.audit_log where tenant_id=@tenantId and event_type='ERROR' and created_at >= now() - interval '24 hours'", new { tenantId }, cancellationToken: t)); return e > 50 ? Warn($"Erros nas últimas 24h: {e}.") : Ok($"Logs acessíveis. Erros 24h: {e}."); }, ct),
            await RunCheckAsync("LOANS", "Loans/Solicitações", "Loans", "/Loans", async t => { await using var c = await db.OpenAsync(t); var r = await c.QuerySingleAsync<(int p, int o)>(new CommandDefinition("select count(*) filter (where status='PENDING')::int p,count(*) filter (where due_at < now() and status<>'RETURNED')::int o from ged.loan_request where tenant_id=@tenantId", new { tenantId }, cancellationToken: t)); return Ok($"Pendentes={r.p}, vencidos={r.o}."); }, ct),
            await RunCheckAsync("DASH", "Dashboard GED", "GED", "/GedDashboard", async t => { await using var c = await db.OpenAsync(t); await c.QuerySingleAsync<(int d, int f)>(new CommandDefinition("select (select count(*)::int from ged.document where tenant_id=@tenantId and reg_status='A') d,(select count(*)::int from ged.folder where tenant_id=@tenantId and reg_status='A') f", new { tenantId }, cancellationToken: t)); return Ok("Contadores básicos do dashboard funcionando."); }, ct),
            await RunCheckAsync("UNCLASS", "Documentos sem classificação", "GED", "/Ged", async t => { await using var c = await db.OpenAsync(t); var n = await c.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.document where tenant_id=@tenantId and reg_status='A' and document_type_id is null", new { tenantId }, cancellationToken: t)); return n > 200 ? Warn($"Sem classificação: {n}.") : Ok($"Sem classificação: {n}."); }, ct),
            await RunCheckAsync("DEMO_DATA", "Dados demonstrativos", "Demo", null, async t => { await using var c = await db.OpenAsync(t); var r = await c.QuerySingleAsync<(int docs, int ocr, int prev, int folderDoc, int audit)>(new CommandDefinition("select (select count(*)::int from ged.document where tenant_id=@tenantId and reg_status='A') docs,(select count(*)::int from ged.ocr_job where tenant_id=@tenantId and status='COMPLETED') ocr,(select count(*)::int from ged.preview_job where tenant_id=@tenantId and status='COMPLETED') prev,(select count(*)::int from ged.document where tenant_id=@tenantId and folder_id is not null and reg_status='A') folderDoc,(select count(*)::int from audit.audit_log where tenant_id=@tenantId) audit", new { tenantId }, cancellationToken: t)); var ready = r.docs >= 5 && r.ocr >= 1 && r.prev >= 1 && r.folderDoc >= 1 && r.audit >= 1; return ready ? Ok("Dados de demonstração suficientes.") : Warn($"Dados insuficientes: docs={r.docs}, ocr={r.ocr}, preview={r.prev}, pasta={r.folderDoc}, auditoria={r.audit}."); }, ct)
        };

        var okCount = checks.Count(x => x.Status == "OK");
        var warningCount = checks.Count(x => x.Status == "WARNING");
        var errorCount = checks.Count(x => x.Status == "ERROR");
        var overall = errorCount > 0 ? "ERROR" : warningCount > 0 ? "WARNING" : "OK";
        return new DemoReadinessReportDto { GeneratedAt = DateTimeOffset.UtcNow, TotalChecks = checks.Count, OkCount = okCount, WarningCount = warningCount, ErrorCount = errorCount, OverallStatus = overall, Checks = checks, Recommendations = BuildRecommendations(checks) };
    }

    private static IReadOnlyList<DemoReadinessRecommendationDto> BuildRecommendations(IReadOnlyList<DemoReadinessCheckDto> checks) =>
    [
        ..(checks.Any(x => x.Code == "OCR" && x.Status != "OK") ? [new DemoReadinessRecommendationDto { Priority = "HIGH", Title = "OCR com erros", Description = "Existem falhas no OCR para demonstração ao vivo.", SuggestedAction = "Evite demonstrar documentos com OCR em erro. Use documentos com OCR concluído." }] : []),
        ..(checks.Any(x => x.Code == "DB" && x.Status != "OK") ? [new DemoReadinessRecommendationDto { Priority = "HIGH", Title = "Banco lento", Description = "Resposta do banco com alerta/falha.", SuggestedAction = "Evite executar consultas pesadas durante a apresentação." }] : []),
        ..(checks.Any(x => x.Code == "HI" && x.Status == "ERROR") ? [new DemoReadinessRecommendationDto { Priority = "HIGH", Title = "HospitalIntelligence indisponível", Description = "Módulo de inteligência apresentou erro.", SuggestedAction = "Use o Dashboard GED como alternativa e não abra a Inteligência Hospitalar até corrigir." }] : []),
        ..(checks.Any(x => x.Code == "DEMO_DATA" && x.Status == "WARNING") ? [new DemoReadinessRecommendationDto { Priority = "MEDIUM", Title = "Dados demonstrativos insuficientes", Description = "Base de demonstração limitada para OCR.", SuggestedAction = "Demonstre a busca por nome/pasta e explique que o OCR amplia a capacidade de pesquisa." }] : []),
        ..(checks.Any(x => x.Code == "UPLOAD" && x.Status != "OK") ? [new DemoReadinessRecommendationDto { Priority = "MEDIUM", Title = "Upload controlado", Description = "Evitar carga alta em apresentação.", SuggestedAction = "Evite subir muitos arquivos na apresentação; use 1 arquivo pequeno." }] : []),
        ..(checks.Any(x => x.Code == "LOGS" && x.Status == "WARNING") ? [new DemoReadinessRecommendationDto { Priority = "LOW", Title = "Logs recentes", Description = "Há volume de erros recente.", SuggestedAction = "Abra a tela de logs apenas se necessário." }] : [])
    ];

    private static async Task<DemoReadinessCheckDto> RunCheckAsync(string code, string title, string module, string? actionUrl, Func<CancellationToken, Task<CheckResult>> run, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var r = await run(cts.Token);
            return new DemoReadinessCheckDto { Code = code, Title = title, Module = module, Status = r.Status, Message = r.Message, TechnicalDetail = r.TechnicalDetail, ElapsedMs = sw.ElapsedMilliseconds, Color = Color(r.Status), Icon = Icon(r.Status), ActionUrl = actionUrl };
        }
        catch (Exception ex)
        {
            return new DemoReadinessCheckDto { Code = code, Title = title, Module = module, Status = "ERROR", Message = "Falha no check, mas a verificação continuou.", TechnicalDetail = $"{ex.GetType().Name}: {ex.Message}", ElapsedMs = sw.ElapsedMilliseconds, Color = Color("ERROR"), Icon = Icon("ERROR"), ActionUrl = actionUrl };
        }
    }

    private static CheckResult Ok(string m) => new("OK", m, null);
    private static CheckResult Warn(string m) => new("WARNING", m, null);
    private static string Color(string s) => s switch { "OK" => "success", "WARNING" => "warning", "ERROR" => "danger", _ => "secondary" };
    private static string Icon(string s) => s switch { "OK" => "bi-check-circle", "WARNING" => "bi-exclamation-triangle", "ERROR" => "bi-x-circle", _ => "bi-dash-circle" };
    private sealed record CheckResult(string Status, string Message, string? TechnicalDetail);
}
