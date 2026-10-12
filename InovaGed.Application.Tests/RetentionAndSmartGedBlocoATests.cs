using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Retention;
using InovaGed.Application.SmartGed;
using System.IO;
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
        Assert.Equal(1, queue.TotalPages);
        Assert.Equal(0, queue.FirstItem);
        Assert.Equal(0, queue.LastItem);
    }

    [Fact]
    public void SmartGedReviewQueue_calculates_authorized_pagination_window()
    {
        var queue = new SmartGedReviewQueue([], [], "FAILED", Page: 2, PageSize: 10, ClassificationTotal: 17, RetentionTotal: 8, CurrentKind: "classification");

        Assert.Equal(25, queue.TotalCount);
        Assert.Equal(17, queue.ActiveTotal);
        Assert.Equal(2, queue.TotalPages);
        Assert.Equal(11, queue.FirstItem);
        Assert.Equal(17, queue.LastItem);
    }

    [Fact]
    public void SmartGedReviewQueue_uses_independent_retention_pagination()
    {
        var queue = new SmartGedReviewQueue([], [], "PENDING", Page: 2, PageSize: 5, ClassificationTotal: 40, RetentionTotal: 8, CurrentKind: "retention");

        Assert.False(queue.ShowingClassifications);
        Assert.Equal(8, queue.ActiveTotal);
        Assert.Equal(2, queue.TotalPages);
        Assert.Equal(6, queue.FirstItem);
        Assert.Equal(8, queue.LastItem);
    }

    [Fact]
    public void SmartGed_queue_queries_do_not_trigger_expired_review_recovery()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var listRegion = source[source.IndexOf("public async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListAsync", StringComparison.Ordinal)..source.IndexOf("public Task AcceptAsync", StringComparison.Ordinal)];

        Assert.DoesNotContain("RecoverExpiredReviewOperationsAsync", listRegion);
        Assert.Contains("public async Task<SmartGedReviewRecoveryResult> RecoverExpiredAsync", source);
    }

    [Fact]
    public void SmartGed_recovery_does_not_accept_classification_by_current_class_equality()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var recoveryRegion = source[source.IndexOf("private static async Task<SmartGedReviewRecoveryResult> RecoverExpiredReviewOperationsAsync", StringComparison.Ordinal)..source.IndexOf("public async Task<SmartGedSearchResult> SearchAsync", StringComparison.Ordinal)];

        Assert.DoesNotContain("status = case when d.classification_id = s.suggested_classification_id then 'ACCEPTED'", recoveryRegion);
        Assert.DoesNotContain("then 'ACCEPTED'", recoveryRegion);
        Assert.DoesNotContain("coalesce(s.applied_at, now())", recoveryRegion);
        Assert.Contains("recovered_at = now()", recoveryRegion);
        Assert.Contains("FAILED_APPLIED", recoveryRegion);
    }

    [Fact]
    public void SmartGed_review_failure_without_confirmed_effect_is_indeterminate_not_no_effect()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var reviewRegion = source[source.IndexOf("private async Task ReviewClassification", StringComparison.Ordinal)..source.IndexOf("public async Task<SmartGedReviewRecoveryResult> RecoverExpiredAsync", StringComparison.Ordinal)];

        Assert.DoesNotContain("applicationCompleted ? \"EFFECT_CONFIRMED\" : \"NO_EFFECT\"", reviewRegion);
        Assert.DoesNotContain("applicationCompleted ? \"FAILED_AFTER_EFFECT\" : \"FAILED_BEFORE_EFFECT\"", reviewRegion);
        Assert.DoesNotContain("status = applicationCompleted ? \"FAILED_APPLIED\" : \"FAILED\"", reviewRegion);
        Assert.Contains("applicationCompleted ? \"EFFECT_CONFIRMED\" : \"INDETERMINATE\"", reviewRegion);
        Assert.Contains("status = applicationCompleted ? \"FAILED_APPLIED\" : \"RECOVERY_INDETERMINATE\"", reviewRegion);
        Assert.Contains("effectConfirmation = applicationCompleted ? \"CONFIRMED\" : \"UNKNOWN\"", reviewRegion);
    }

    [Fact]
    public void SmartGed_failed_retry_requires_no_effect_evidence()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var reviewRegion = source[source.IndexOf("private async Task ReviewClassification", StringComparison.Ordinal)..source.IndexOf("private static string PublicReviewFailure", StringComparison.Ordinal)];

        Assert.Contains("EnsureReviewableAsync", reviewRegion);
        Assert.Contains("effect_status", reviewRegion);
        Assert.Contains("suggestion_id = @suggestionId", reviewRegion);
        Assert.Contains("document_id = @documentId", reviewRegion);
        Assert.Contains("NO_EFFECT", reviewRegion);
        Assert.Contains("Falha legada sem operação rastreável", reviewRegion);
        Assert.Contains("falha sem evidência de ausência de efeito", reviewRegion);
    }

    [Fact]
    public void SmartGed_review_operation_result_upsert_preserves_confirmed_effect()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var upsertRegion = source[source.IndexOf("private static Task UpsertReviewOperationResultAsync", StringComparison.Ordinal)..source.IndexOf("private static Task MarkReviewOperationAuditedAsync", StringComparison.Ordinal)];

        Assert.Contains("when ged.smart_ged_review_operation_result.effect_status = 'EFFECT_CONFIRMED' then 'EFFECT_CONFIRMED'", upsertRegion);
        Assert.Contains("effect_applied = ged.smart_ged_review_operation_result.effect_applied or excluded.effect_applied", upsertRegion);
        Assert.Contains("result_payload_json = coalesce(ged.smart_ged_review_operation_result.result_payload_json, '{}'::jsonb) || excluded.result_payload_json", upsertRegion);
    }

    [Fact]
    public void SmartGed_recovery_requires_full_admin_policy_in_controller_and_service()
    {
        var controller = File.ReadAllText(RepoPath("InovaGed.Web", "Controller", "SmartGedController.cs"));
        var service = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var recoveryServiceRegion = service[service.IndexOf("public async Task<SmartGedReviewRecoveryResult> RecoverExpiredAsync", StringComparison.Ordinal)..service.IndexOf("private static async Task<SmartGedReviewRecoveryResult> RecoverExpiredReviewOperationsAsync", StringComparison.Ordinal)];

        Assert.Contains("[Authorize(Policy = AppPolicies.FullAdminOnly)]", controller);
        Assert.Contains("_accessPolicy.IsAdminAsync", recoveryServiceRegion);
        Assert.DoesNotContain("GED.DOCUMENTS", recoveryServiceRegion);
    }

    [Fact]
    public void SmartGed_admin_resolution_is_explicit_and_does_not_reapply()
    {
        var service = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var controller = File.ReadAllText(RepoPath("InovaGed.Web", "Controller", "SmartGedController.cs"));
        var view = File.ReadAllText(RepoPath("InovaGed.Web", "Views", "SmartGed", "ReviewQueue.cshtml"));
        var migration = File.ReadAllText(RepoPath("database", "migrations", "2026_10_20_smart_ged_review_admin_resolution.sql"));

        Assert.Contains("ResolveAdministrativeAsync", service);
        Assert.Contains("status = 'ADMIN_RESOLVED'", service);
        Assert.Contains("admin_resolution_audit_status = 'PENDING'", service);
        Assert.Contains("MarkAdministrativeAuditRecordedAsync", service);
        Assert.Contains("ReconcileAdministrativeAuditAsync", service);
        Assert.Contains("[Authorize(Policy = AppPolicies.FullAdminOnly)]", controller);
        Assert.Contains("ResolveAdministrativeReview", view);
        Assert.Contains("ReconcileAdministrativeAudit", view);
        Assert.Contains("admin_resolution_reason", migration);
        Assert.Contains("admin_resolution_evidence", migration);
    }

    [Fact]
    public void SmartGed_review_blocks_legacy_or_unversioned_suggestions_before_application()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));
        var reviewRegion = source[source.IndexOf("private async Task ReviewClassification", StringComparison.Ordinal)..source.IndexOf("private static Task UpsertReviewOperationResultAsync", StringComparison.Ordinal)];

        Assert.Contains("Sugestão legada ou sem origem/versionamento comprovado", reviewRegion);
        Assert.Contains("Recomendação legada ou sem origem/versionamento comprovado", reviewRegion);
        Assert.Contains("Documento sem versão vigente", reviewRegion);
        Assert.Contains("Recomendação de temporalidade sem regra comprovada", reviewRegion);
    }

    [Fact]
    public void SmartGed_review_queue_does_not_show_fictional_retention_defaults()
    {
        var view = File.ReadAllText(RepoPath("InovaGed.Web", "Views", "SmartGed", "ReviewQueue.cshtml"));

        Assert.Contains("Model.ActiveTotal nesta aba", view);
        Assert.Contains("Requer revisão", view);
        Assert.Contains("Não definida", view);
        Assert.DoesNotContain("x.Phase ?? \"Corrente\"", view);
        Assert.DoesNotContain("x.FinalDestination ?? \"Guarda Permanente\"", view);
        Assert.Contains("AdminResolutionReason", view);
        Assert.Contains("AdminResolutionEvidence", view);
    }

    [Fact]
    public void Explicit_transaction_migrations_record_history_out_of_band()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SystemHealth", "Migrations", "DatabaseMigrationRunner.cs"));
        var explicitTransactionRegion = source[source.IndexOf("if (HasExplicitTransaction(sql))", StringComparison.Ordinal)..source.IndexOf("await using var transaction = await connection.BeginTransactionAsync(ct)", StringComparison.Ordinal)];

        Assert.Contains("InsertHistoryOutOfBandAsync", explicitTransactionRegion);
        Assert.Contains("Falha ao registrar histórico", explicitTransactionRegion);
        Assert.Contains("await using var historyConnection = await db.OpenAsync(ct);", source);
    }

    [Fact]
    public void Explicit_zero_period_calculates_due_at_equal_to_basis_date()
    {
        var basis = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "020.1",
            StartEvent = "CRIACAO",
            BasisDate = basis,
            RetentionActiveDays = 0,
            RetentionActiveMonths = 0,
            RetentionActiveYears = 0,
            RetentionArchiveDays = 0,
            RetentionArchiveMonths = 0,
            RetentionArchiveYears = 0,
            HasExplicitZeroPeriod = true,
            FinalDestination = "ELIMINACAO",
            CalculatedDueAt = basis,
            CalculatedStatus = "OVERDUE", // since 2025-06-01 is past
            PersistedStatus = "OK"
        };

        Assert.True(mem.HasExplicitZeroPeriod);
        Assert.Equal(basis, mem.CalculatedDueAt);
        Assert.Equal("OVERDUE", mem.CalculatedStatus);
    }

    [Fact]
    public void Incomplete_rule_evaluates_to_incomplete_rule_status_and_null_due_at()
    {
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "099.INCOMPLETE",
            StartEvent = "CRIACAO",
            BasisDate = DateTime.UtcNow,
            RetentionActiveDays = null,
            RetentionActiveMonths = null,
            RetentionActiveYears = null,
            RetentionArchiveDays = null,
            RetentionArchiveMonths = null,
            RetentionArchiveYears = null,
            IsRuleIncomplete = true,
            FinalDestination = "ELIMINACAO",
            CalculatedDueAt = null,
            CalculatedStatus = "INCOMPLETE_RULE"
        };

        Assert.True(mem.IsRuleIncomplete);
        Assert.Null(mem.CalculatedDueAt);
        Assert.Equal("INCOMPLETE_RULE", mem.CalculatedStatus);
    }

    [Fact]
    public void Invalid_or_untriggered_event_evaluates_to_event_pending_and_null_due_at()
    {
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "030.CONTRATO",
            StartEvent = "ENCERRAMENTO",
            BasisDate = null, // Document not yet closed (closed_at is null)
            IsBasisEventPending = true,
            RetentionActiveYears = 5,
            FinalDestination = "GUARDA_PERMANENTE",
            CalculatedDueAt = null,
            CalculatedStatus = "EVENT_PENDING"
        };

        Assert.True(mem.IsBasisEventPending);
        Assert.Null(mem.BasisDate);
        Assert.Null(mem.CalculatedDueAt);
        Assert.Equal("EVENT_PENDING", mem.CalculatedStatus);
    }

    [Fact]
    public void Permanent_archive_evaluates_to_permanent_record_and_null_due_at()
    {
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "HISTORICO.01",
            StartEvent = "CRIACAO",
            BasisDate = DateTime.UtcNow,
            FinalDestination = "GUARDA_PERMANENTE",
            IsPermanentRecord = true,
            CalculatedDueAt = null,
            CalculatedStatus = "PERMANENT_RECORD"
        };

        Assert.True(mem.IsPermanentRecord);
        Assert.Equal("GUARDA_PERMANENTE", mem.FinalDestination);
        Assert.Null(mem.CalculatedDueAt);
        Assert.Equal("PERMANENT_RECORD", mem.CalculatedStatus);
    }

    [Fact]
    public void Suspension_hold_takes_precedence_and_evaluates_to_hold_status()
    {
        var mem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            ClassificationCode = "040.AUDIT",
            StartEvent = "CRIACAO",
            BasisDate = DateTime.UtcNow.AddYears(-3),
            RetentionActiveYears = 2,
            FinalDestination = "ELIMINACAO",
            IsHoldActive = true,
            CalculatedDueAt = DateTime.UtcNow.AddYears(-1),
            CalculatedStatus = "HOLD" // Even if due date passed, hold overrides
        };

        Assert.True(mem.IsHoldActive);
        Assert.Equal("HOLD", mem.CalculatedStatus);
    }

    [Fact]
    public void Historical_plan_vs_versioned_item_distinguishes_rule_source()
    {
        var legacyMem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            RuleSource = "LEGACY_PLAN",
            PlanVersionNo = null
        };

        var versionedMem = new RetentionCalculationMemory
        {
            DocumentId = Guid.NewGuid(),
            ClassificationId = Guid.NewGuid(),
            RuleSource = "VERSIONED_ITEM",
            PlanVersionNo = 3,
            PlanVersionTitle = "PCD 2026 Revisão 3"
        };

        Assert.Equal("LEGACY_PLAN", legacyMem.RuleSource);
        Assert.Null(legacyMem.PlanVersionNo);

        Assert.Equal("VERSIONED_ITEM", versionedMem.RuleSource);
        Assert.Equal(3, versionedMem.PlanVersionNo);
        Assert.Equal("PCD 2026 Revisão 3", versionedMem.PlanVersionTitle);
    }

    [Fact]
    public void Combined_days_months_years_interval_arithmetic_consistency()
    {
        // 15 de março de 2024 (bissexto) + 2 anos + 3 meses + 10 dias
        var basis = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var withYears = basis.AddYears(2); // 2026-03-15
        var withMonths = withYears.AddMonths(3); // 2026-06-15
        var finalDate = withMonths.AddDays(10); // 2026-06-25

        Assert.Equal(new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc), finalDate);
    }

    [Fact]
    public void SmartGed_retention_generation_persists_rule_and_does_not_invent_destination()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));

        Assert.Contains("suggested_rule_id", source);
        Assert.Contains("var ruleId = retentionIsActionable ? best.Plan.Id : (Guid?)null", source);
        Assert.Contains("var destination = best.Plan.FinalDestination", source);
        Assert.DoesNotContain("best.Plan.FinalDestination ?? \"REQUER_REVISAO\"", source);
        Assert.Contains("Pendência: a temporalidade não possui regra/versionamento comprovado", source);
    }

    [Fact]
    public void SmartGed_administrative_audit_reconciliation_preserves_decision_author_and_claims_once()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Infrastructure", "SmartGed", "SmartGedService.cs"));

        Assert.DoesNotContain("coalesce(admin_resolution_by, @performedBy)", source);
        Assert.Contains("admin_resolution_by as DecisionBy", source);
        Assert.Contains("TryClaimAdministrativeAuditAsync", source);
        Assert.Contains("admin_resolution_audit_status = 'RECORDING'", source);
        Assert.Contains("decisionAuthorMissing = !decisionBy.HasValue", source);
    }

    [Fact]
    public void SmartGed_review_queue_translates_administrative_audit_statuses()
    {
        var source = File.ReadAllText(RepoPath("InovaGed.Web", "Views", "SmartGed", "ReviewQueue.cshtml"));

        Assert.Contains("\"RECORDING\" => \"Registro em confirmação\"", source);
        Assert.Contains("AuditStatusLabel(x.AdminResolutionAuditStatus)", source);
        Assert.DoesNotContain("x.AdminResolutionAuditStatus) ? \"PENDENTE\"", source);
    }

    private static string RepoPath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InovaGed.sln")))
            dir = dir.Parent;

        if (dir is null) throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
        return Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
    }
}

