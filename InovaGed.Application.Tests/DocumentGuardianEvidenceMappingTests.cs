using InovaGed.Application.DocumentGuardian;
using InovaGed.Infrastructure.DocumentGuardian;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class DocumentGuardianEvidenceMappingTests
{
    [Fact]
    public void AssignEvidencesToFindings_Deve_Agrupar_Por_Finding_Copiar_Valores_E_Isolar_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var findingWithEvidenceId = Guid.NewGuid();
        var findingWithoutEvidenceId = Guid.NewGuid();
        var validEvidenceId = Guid.NewGuid();

        var findings = new[]
        {
            new DocumentGuardianFindingDto { Id = findingWithEvidenceId },
            new DocumentGuardianFindingDto { Id = findingWithoutEvidenceId }
        };

        var rows = new[]
        {
            new DocumentGuardianService.DocumentGuardianEvidenceRow
            {
                TenantId = tenantId,
                FindingId = findingWithEvidenceId,
                Id = validEvidenceId,
                SourceType = "OCR",
                EvidenceKey = "cpf",
                EvidenceValue = null,
                Excerpt = null,
                Confidence = 87.35m
            },
            new DocumentGuardianService.DocumentGuardianEvidenceRow
            {
                TenantId = otherTenantId,
                FindingId = findingWithEvidenceId,
                Id = Guid.NewGuid(),
                SourceType = "METADATA",
                EvidenceKey = "leak",
                EvidenceValue = "nao deve associar",
                Excerpt = "tenant incorreto",
                Confidence = 1m
            }
        };

        DocumentGuardianService.AssignEvidencesToFindings(findings, rows, tenantId);

        var evidence = Assert.Single(findings[0].Evidences);
        Assert.Equal(validEvidenceId, evidence.Id);
        Assert.Equal("OCR", evidence.SourceType);
        Assert.Equal("cpf", evidence.EvidenceKey);
        Assert.Null(evidence.EvidenceValue);
        Assert.Null(evidence.Excerpt);
        Assert.Equal(87.35m, evidence.Confidence);
        Assert.Empty(findings[1].Evidences);
    }
}
