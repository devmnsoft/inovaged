using InovaGed.Application.DocumentGuardian.Rules;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class GuardianRulesAndScoresTests
{
    [Fact]
    public void Catalogo_Deve_Conter_Regras_Obrigatorias()
    {
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "GED-001" && r.Category == "QUALIDADE");
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "ARQ-001");
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "SEC-001");
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "LGPD-001");
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "OPS-001");
        Assert.Contains(DocumentGuardianRules.All, r => r.Code == "PHY-003");
        Assert.Equal(35, DocumentGuardianRules.All.Select(r => r.Code).Distinct().Count());
    }

    [Fact]
    public void Risco_Deve_Ser_Deterministico_Explicavel_E_Limitado()
    {
        var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var result = new GuardianRiskScoreCalculator().Calculate(new[]
        {
            new GuardianFindingInput("LGPD-001", "CRITICAL", "LGPD", "CONFIRMED", now.AddDays(-21), true, 3),
            new GuardianFindingInput("GED-001", "MEDIUM", "QUALIDADE", "OPEN", now.AddDays(-7))
        }, now);
        Assert.InRange(result.Score, 0, 100);
        Assert.Contains("LGPD-001", result.Factors[0]);
        Assert.Equal(GuardianRiskScoreCalculator.Version, result.Version);
    }

    [Fact]
    public void Completude_Deve_Considerar_Pesos_E_Ausencias_Obrigatorias()
    {
        var result = new DocumentCompletenessCalculator().Calculate(new[]
        {
            new CompletenessRequirement("CLASSIFICACAO", 40, true, true),
            new CompletenessRequirement("ASSINATURA", 30, true, false),
            new CompletenessRequirement("OCR", 30, false, true)
        });
        Assert.Equal(70, result.Score);
        Assert.Contains("ASSINATURA", result.Missing);
    }
}
