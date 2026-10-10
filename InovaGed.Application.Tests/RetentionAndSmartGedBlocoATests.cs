using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Retention;
using InovaGed.Application.SmartGed;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class RetentionAndSmartGedBlocoATests
{
    [Theory]
    [InlineData("FINALIZADO")]
    [InlineData("ENCERRADO")]
    [InlineData("ARQUIVADO")]
    [InlineData("CANCELADO")]
    [InlineData("DEFERIDO")]
    [InlineData("INDEFERIDO")]
    public void ProtocolCustodyRules_treats_all_final_and_decided_statuses_as_closed(string status)
    {
        Assert.True(ProtocolCustodyRules.IsClosed(status));
    }

    [Theory]
    [InlineData("ABERTO")]
    [InlineData("EM_TRAMITACAO")]
    [InlineData("RASCUNHO")]
    [InlineData("AGUARDANDO_RECEBIMENTO")]
    public void ProtocolCustodyRules_does_not_treat_active_statuses_as_closed(string status)
    {
        Assert.False(ProtocolCustodyRules.IsClosed(status));
    }

    [Fact]
    public void Leap_year_february_29_date_arithmetic_equivalence()
    {
        // 2024 é bissexto. 29 de fevereiro + 1 ano deve resultar em 28 de fevereiro de 2025.
        var leapBasis = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc);
        var oneYearLater = leapBasis.AddYears(1);
        Assert.Equal(new DateTime(2025, 2, 28, 0, 0, 0, DateTimeKind.Utc), oneYearLater);

        // 2024-02-29 + 4 anos deve resultar em 2028-02-29.
        var fourYearsLater = leapBasis.AddYears(4);
        Assert.Equal(new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc), fourYearsLater);
    }

    [Fact]
    public void Month_end_and_day_month_combination_consistency()
    {
        // 31 de janeiro + 1 mês + 15 dias
        var basis = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var oneMonthLater = basis.AddMonths(1); // 2024-02-29 (ano bissexto)
        Assert.Equal(29, oneMonthLater.Day);
        Assert.Equal(2, oneMonthLater.Month);

        var plus15Days = oneMonthLater.AddDays(15);
        Assert.Equal(new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc), plus15Days);
    }

    [Fact]
    public void Retention_simulation_memory_distinguishes_persisted_from_calculated_result()
    {
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "001.01",
            CalculatedStatus = "OK",
            CalculatedDueAt = DateTime.UtcNow.AddYears(5),
            PersistedStatus = "INCOMPLETE_RULE",
            PersistedDueAt = null,
            RuleSource = "VERSIONED_ITEM",
            PlanVersionNo = 2,
            StartEvent = "CRIACAO",
            BasisDate = DateTime.UtcNow
        };

        Assert.Equal("OK", mem.CalculatedStatus);
        Assert.Equal("INCOMPLETE_RULE", mem.PersistedStatus);
        Assert.NotNull(mem.CalculatedDueAt);
        Assert.Null(mem.PersistedDueAt);
        Assert.Equal("VERSIONED_ITEM", mem.RuleSource);
    }

    [Fact]
    public void SmartGedSearchItem_preserves_version_and_outdated_analysis_flag()
    {
        var item = new SmartGedSearchItem(
            DocumentId: Guid.NewGuid(),
            Document: "Contrato de Prestação de Serviços",
            Summary: "Resumo extraído",
            Classification: "010 - Contratos e Convênios",
            PhysicalLocation: null,
            QualityStatus: "OK",
            Excerpt: "…vigência do contrato de prestação…",
            VersionNumber: 3,
            RetentionStatus: "OK",
            IsAnalysisOutdated: true
        );

        Assert.Equal(3, item.VersionNumber);
        Assert.True(item.IsAnalysisOutdated);
        Assert.Equal("010 - Contratos e Convênios", item.Classification);
        Assert.Contains("vigência", item.Excerpt);
    }

    [Fact]
    public void SmartGedSearchQuery_allows_filtering_solely_by_date_or_retention_without_text()
    {
        var q = new SmartGedSearchQuery(
            TenantId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Text: string.Empty,
            RetentionStatus: "OVERDUE",
            CreatedAfter: DateTime.UtcNow.AddDays(-30)
        );

        Assert.Empty(q.Text);
        Assert.Equal("OVERDUE", q.RetentionStatus);
        Assert.NotNull(q.CreatedAfter);
    }

    [Fact]
    public void DocumentClassificationSuggestionItem_maps_obsolescence_and_current_values()
    {
        var item = new DocumentClassificationSuggestionItem(
            Id: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Code: "010.1",
            Title: "Contratos de Serviço",
            Reason: "Trecho do contrato de fornecimento",
            Confidence: 85.5m,
            Status: "PENDING",
            DocumentTitle: "Contrato 123",
            VersionNumber: 2,
            CurrentClassification: "010 - Geral",
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-2),
            IsObsolete: true,
            ReviewNotes: "Revisão agendada"
        );

        Assert.Equal("010.1", item.Code);
        Assert.Equal("Contrato 123", item.DocumentTitle);
        Assert.Equal(2, item.VersionNumber);
        Assert.Equal("010 - Geral", item.CurrentClassification);
        Assert.True(item.IsObsolete);
        Assert.Equal("PENDING", item.Status);
    }

    [Fact]
    public void DocumentRetentionSuggestionItem_maps_obsolescence_and_current_values()
    {
        var item = new DocumentRetentionSuggestionItem(
            Id: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Phase: "Corrente",
            FinalDestination: "Guarda Permanente",
            TriggerEvent: "FIM_VIGENCIA",
            RetentionUntil: new DateOnly(2030, 12, 31),
            Reason: "Regra arquivística institucional",
            Confidence: 90.0m,
            Status: "PENDING",
            DocumentTitle: "Termo de Homologação",
            VersionNumber: 1,
            CurrentRetention: "CALCULADO",
            CreatedAt: DateTimeOffset.UtcNow,
            IsObsolete: false
        );

        Assert.Equal("Corrente", item.Phase);
        Assert.Equal("Guarda Permanente", item.FinalDestination);
        Assert.Equal("Termo de Homologação", item.DocumentTitle);
        Assert.False(item.IsObsolete);
        Assert.Equal(new DateOnly(2030, 12, 31), item.RetentionUntil);
    }

    [Fact]
    public void SmartGedReviewQueue_supports_status_filtering_defaults()
    {
        var queue = new SmartGedReviewQueue([], [], "ACCEPTED");
        Assert.Equal("ACCEPTED", queue.CurrentStatus);
        Assert.Empty(queue.Classifications);
        Assert.Empty(queue.Retentions);
    }
}

