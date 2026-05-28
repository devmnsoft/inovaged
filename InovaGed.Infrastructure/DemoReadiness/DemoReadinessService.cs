using System.Data;
using System.Diagnostics;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.DemoReadiness;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.DemoReadiness;

public sealed class DemoReadinessService : IDemoReadinessService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DemoReadinessService> _logger;

    public DemoReadinessService(
        IDbConnectionFactory db,
        ILogger<DemoReadinessService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DemoReadinessReportDto> RunAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var report = new DemoReadinessReportDto { GeneratedAt = DateTimeOffset.UtcNow };

        var checks = new List<DemoReadinessCheckDto>
        {
            await CheckDatabaseAsync(tenantId, ct),
            await CheckDocumentsAsync(tenantId, ct),
            await CheckOcrAsync(tenantId, ct),
            await CheckUsersAsync(tenantId, ct)
        };

        report.Checks = checks;
        report.TotalChecks = checks.Count;
        report.OkCount = checks.Count(x => x.Status == "OK");
        report.WarningCount = checks.Count(x => x.Status == "WARNING");
        report.ErrorCount = checks.Count(x => x.Status == "ERROR");
        report.ReadinessScore = CalculateScore(checks);
        report.OverallStatus = report.ErrorCount > 0 ? "ERROR" : report.WarningCount > 0 ? "WARNING" : "OK";
        report.Recommendations = BuildRecommendations(report);
        report.ExecutiveIndicators = BuildExecutiveIndicators(report);
        report.DigitalMaturity = BuildDigitalMaturity(report);
        report.ModuleMap = BuildModuleMap(report);
        report.ScriptSteps = BuildScriptSteps(report);
        report.FinancialEstimates = BuildFinancialEstimates(report);

        return report;
    }

    private async Task<DemoReadinessCheckDto> CheckDatabaseAsync(Guid tenantId, CancellationToken ct) =>
        await SafeCheckAsync("DB", "Banco de dados", "Infra", async () =>
        {
            using var conn = _db.CreateConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
            var one = await conn.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: ct));
            return one == 1 ? Ok("Conexão com banco operacional.") : Warn("Banco retornou resposta inesperada.");
        });

    private async Task<DemoReadinessCheckDto> CheckDocumentsAsync(Guid tenantId, CancellationToken ct) =>
        await SafeCheckAsync("DOCS", "Documentos", "GED", async () =>
        {
            using var conn = _db.CreateConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
            var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.document where tenant_id=@tenantId and reg_status='A'", new { tenantId }, cancellationToken: ct));
            return total > 0 ? Ok($"Documentos ativos: {total}.") : Warn("Nenhum documento ativo encontrado.");
        });

    private async Task<DemoReadinessCheckDto> CheckOcrAsync(Guid tenantId, CancellationToken ct) =>
        await SafeCheckAsync("OCR", "OCR", "GED", async () =>
        {
            using var conn = _db.CreateConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
            var completed = await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.ocr_job where tenant_id=@tenantId and status='COMPLETED'", new { tenantId }, cancellationToken: ct));
            return completed > 0 ? Ok($"OCR concluído em {completed} itens.") : Warn("Nenhum OCR concluído encontrado.");
        });

    private async Task<DemoReadinessCheckDto> CheckUsersAsync(Guid tenantId, CancellationToken ct) =>
        await SafeCheckAsync("USERS", "Usuários", "Admin", async () =>
        {
            using var conn = _db.CreateConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
            var active = await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.user_tenant where tenant_id=@tenantId and is_active=true", new { tenantId }, cancellationToken: ct));
            return active > 0 ? Ok($"Usuários ativos: {active}.") : Warn("Nenhum usuário ativo encontrado.");
        });

    private async Task<DemoReadinessCheckDto> SafeCheckAsync(string code, string title, string module, Func<Task<DemoReadinessCheckDto>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var dto = await action();
            dto.Code = code;
            dto.Title = title;
            dto.Module = module;
            dto.ElapsedMs = sw.ElapsedMilliseconds;
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no check {Code}", code);
            return new DemoReadinessCheckDto
            {
                Code = code,
                Title = title,
                Module = module,
                Status = "ERROR",
                Message = "Falha no check.",
                TechnicalDetail = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
                Icon = "bi-x-circle",
                Color = "danger"
            };
        }
    }

    private static DemoReadinessCheckDto Ok(string message) => new() { Status = "OK", Message = message, Icon = "bi-check-circle", Color = "success" };
    private static DemoReadinessCheckDto Warn(string message) => new() { Status = "WARNING", Message = message, Icon = "bi-exclamation-triangle", Color = "warning" };
    private static int CalculateScore(List<DemoReadinessCheckDto> checks) => checks.Count == 0 ? 0 : (int)Math.Round((checks.Count(x => x.Status == "OK") * 100.0) / checks.Count);
    private static List<DemoReadinessRecommendationDto> BuildRecommendations(DemoReadinessReportDto r) => r.ErrorCount > 0 ? [new() { Priority = "Alta", Title = "Corrigir erros", Description = "Foram encontrados erros críticos.", SuggestedAction = "Corrigir os checks com status ERROR." }] : [];
    private static List<ExecutiveValueIndicatorDto> BuildExecutiveIndicators(DemoReadinessReportDto r) => [new() { Title = "Score", Value = r.ReadinessScore + "%", Subtitle = "Prontidão", Explanation = "Resumo geral da prontidão." }];
    private static List<DigitalMaturityItemDto> BuildDigitalMaturity(DemoReadinessReportDto r) => [new() { Label = "Checks OK", Percentage = r.TotalChecks == 0 ? 0 : (decimal)r.OkCount * 100 / r.TotalChecks, Status = r.OverallStatus, Recommendation = "Aumentar cobertura de checks OK." }];
    private static List<DemoModuleStatusDto> BuildModuleMap(DemoReadinessReportDto r) => [new() { ModuleName = "DemoReadiness", Status = r.OverallStatus, Benefit = "Visão consolidada", SuggestedDemoTime = "3 min", Recommendation = "Abrir e revisar cards executivos." }];
    private static List<DemoScriptStepDto> BuildScriptSteps(DemoReadinessReportDto r) => [new() { Order = 1, Title = "Abrir Central Executiva GED", SuggestedSpeech = "Apresentar score, riscos e recomendações.", Status = r.OverallStatus, EstimatedMinutes = 3 }];
    private static List<FinancialImpactEstimateDto> BuildFinancialEstimates(DemoReadinessReportDto r) => [new() { Title = "Ganho operacional potencial", Value = r.OkCount.ToString(), Explanation = "Baseado em checks com status OK." }];
}
