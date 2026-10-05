using System.Data;
using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using Npgsql;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

public sealed class DocumentAiAssistService(
    IDbConnectionFactory db,
    IDocumentAiGateway ai,
    IAiGovernanceStore governance,
    IAbacAuthorizationService authorization,
    IAuditWriter audit,
    IAssistedDocumentStore documents,
    RetentionRecalcService retention) : IDocumentAiAssistService
{
    private const int TextLimit = 120_000;
    private const int CandidateLimit = 40;
    private const string MetadataSchema = """{"type":"object","additionalProperties":false,"required":["fields"],"properties":{"fields":{"type":"object","additionalProperties":false,"properties":{"title":{"type":"object","additionalProperties":false,"required":["sufficient"],"properties":{"sufficient":{"type":"boolean"},"value":{"type":"string","maxLength":300},"evidence":{"type":"string","maxLength":500}}},"description":{"type":"object","additionalProperties":false,"required":["sufficient"],"properties":{"sufficient":{"type":"boolean"},"value":{"type":"string","maxLength":2000},"evidence":{"type":"string","maxLength":500}}},"isConfidential":{"type":"object","additionalProperties":false,"required":["sufficient"],"properties":{"sufficient":{"type":"boolean"},"value":{"type":"boolean"},"evidence":{"type":"string","maxLength":500}}}}}}}""";
    private const string TypeSchema = """{"type":"object","additionalProperties":false,"required":["outcome"],"properties":{"outcome":{"type":"string","enum":["suggested","insufficient"]},"typeName":{"type":"string","maxLength":200},"evidence":{"type":"string","maxLength":500},"justification":{"type":"string","maxLength":500}}}""";
    private const string ArchivalSchema = """{"type":"object","additionalProperties":false,"required":["outcome"],"properties":{"outcome":{"type":"string","enum":["suggested","insufficient"]},"classCode":{"type":"string","maxLength":80},"evidence":{"type":"string","maxLength":500},"justification":{"type":"string","maxLength":500}}}""";

    public async Task<AssistResponse> SummarizeAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        if (!Guid.TryParse(idempotencyKey, out _)) return Fail(400, "Identificador da solicitação inválido.");
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadAsync(connection, caller.TenantId, versionId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "VIEW", ct)) return await DenyAsync(caller, document, versionId, "authorization_missing", ct);
        document.Text = await OcrAsync(connection, caller.TenantId, document, ct);
        if (string.IsNullOrWhiteSpace(document.Text)) return Fail(422, "Esta versão não possui texto OCR para resumir.");
        if (document.Text.Length > TextLimit) return Fail(422, "O documento excede 120.000 caracteres. Nenhum conteúdo foi enviado nem truncado.");
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["subject","facts","dates","pending","limitations"],"properties":{"subject":{"type":"string","minLength":1,"maxLength":500},"facts":{"type":"array","maxItems":12,"items":{"type":"object","additionalProperties":false,"required":["text","evidence"],"properties":{"text":{"type":"string","minLength":1,"maxLength":800},"evidence":{"type":"string","minLength":1,"maxLength":500}}}},"dates":{"type":"array","maxItems":10,"items":{"type":"object","additionalProperties":false,"required":["text","evidence"],"properties":{"text":{"type":"string","minLength":1,"maxLength":300},"evidence":{"type":"string","minLength":1,"maxLength":500}}}},"pending":{"type":"array","maxItems":10,"items":{"type":"object","additionalProperties":false,"required":["text","evidence"],"properties":{"text":{"type":"string","minLength":1,"maxLength":500},"evidence":{"type":"string","minLength":1,"maxLength":500}}}},"limitations":{"type":"array","maxItems":10,"items":{"type":"string","minLength":1,"maxLength":500}}}}""");
        var reference = Reference(document);
        var result = await ai.ExecuteAsync(new(caller.TenantId, caller.UserId, AiTask.Summarize, "Produza resumo documental em português. Para cada fato, data e pendência, inclua evidence como trecho literal da fonte. Não invente páginas, localização ou informação.", [new(reference, document.Text)], schema, idempotencyKey, Sources: [new AiExecutionSource(document.DocumentId, document.VersionId)]), ct);
        if (!result.Success || result.ExecutionId is null) return ProviderFailure(result);
        if (result.StructuredData is null || !SummaryEvidenceIsLiteral(result.StructuredData.RootElement, document.Text)) return Fail(422, "O provedor retornou evidência que não pôde ser validada na versão consultada.", result.CorrelationId);
        var current = await CurrentAsync(connection, caller.TenantId, document.DocumentId, ct);
        if (current is null || current.VersionId != document.VersionId || !await CanAsync(caller, document with { Confidential = current.Confidential }, "VIEW", ct))
            return await DenyAsync(caller, document, versionId, "authorization_or_state_changed", ct, 409, "O acesso, a situação ou a versão mudou durante o resumo; o resultado foi descartado.");
        await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_SUMMARY", "DOCUMENT", document.DocumentId, "Resumo documental revisável gerado", caller.Ip, caller.UserAgent, new { document.VersionId, result.Provider, result.Model, result.CorrelationId, result.ExecutionId }, ct);
        return Ok(new { success = true, executionId = result.ExecutionId, state = "Completed", document.Title, document.VersionNumber, source = reference, sources = Sources(document), data = result.StructuredData.RootElement, correlationId = result.CorrelationId, reviewRequired = true });
    }

    public async Task<AssistResponse> GetExecutionAsync(Guid executionId, Guid? versionId, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        var execution = await governance.GetExecutionAsync(caller.TenantId, caller.UserId, executionId, ct);
        if (execution is null) return Fail(404, "Execução não encontrada.");
        if (execution.ResultMalformed) return Fail(422, "O resultado persistido está malformado e não foi reutilizado.");
        if (execution.Result is null || execution.ResultExpiresAt is null || execution.ResultExpiresAt <= DateTimeOffset.UtcNow) return Fail(409, "O resultado expirou e não pode ser reutilizado.");
        var sources = await AuthorizedSourcesAsync(execution, caller, ct);
        if (sources.Error is not null) return sources.Error;
        if (versionId is null || sources.Rows.All(x => x.VersionId != versionId)) return Fail(409, "A execução não está vinculada à versão informada.");
        return Ok(new { success = true, execution.ExecutionId, state = execution.State.ToString(), execution.CreatedAt, execution.CompletedAt, resultExpiresAt = execution.ResultExpiresAt, sources = sources.Rows.Select(x => new { x.DocumentId, x.VersionId, x.Title, x.VersionNumber }), result = execution.Result });
    }

    public Task<AssistResponse> SuggestMetadataAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct) =>
        SuggestAsync(versionId, idempotencyKey, caller, AiTask.ExtractMetadata, MetadataSchema, true, false, "Sugira apenas os metadados sustentados por trecho literal. Para cada campo use sufficient=true com value e evidence, ou sufficient=false quando não houver informação bastante. Não invente título, descrição ou sigilo. Nunca marque isConfidential como false: ausência de dado sensível não torna o documento público.", async (connection, document, result, token) =>
        {
            if (!result.StructuredData!.RootElement.TryGetProperty("fields", out var fieldRoot) || fieldRoot.ValueKind != JsonValueKind.Object)
                return Fail(422, "A sugestão não trouxe campos verificáveis.", result.CorrelationId);
            var fields = MetadataSuggestionReader.Read(fieldRoot, document.Text);
            var notes = fields.Where(x => !x.Sufficient).Select(x => $"{Label(x.Name)}: {x.Note}").ToArray();
            return Ok(new { success = true, executionId = result.ExecutionId, state = "Completed", correlationId = result.CorrelationId, reviewRequired = true, concurrencyToken = token, document.Title, document.VersionNumber, sources = Sources(document), coverage = new { partial = notes.Length > 0, notes }, current = new { title = document.Title, description = document.Description, isConfidential = document.Confidential }, fields = fields.Select(x => new { x.Name, label = Label(x.Name), x.Sufficient, suggested = x.Name == "isConfidential" ? (object?)x.Flag : x.Text, x.Evidence, x.Note }) });
        }, ct);

    public async Task<AssistResponse> ApplyMetadataAsync(ApplyMetadataCommand command, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadByDocumentAsync(connection, caller.TenantId, command.DocumentId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "EDIT", ct)) return await DenyAsync(caller, document, command.VersionId, "edit_permission_missing", ct);
        if (command.TitleSet && string.IsNullOrWhiteSpace(command.Title)) return Fail(400, "Informe um título para aplicar ou mantenha o valor atual.");
        if (command.TitleSet && command.Title!.Trim().Length > 300) return Fail(400, "Título acima do limite de 300 caracteres.");
        if (command.DescriptionSet && (command.Description?.Length ?? 0) > 2000) return Fail(400, "Descrição acima do limite de 2000 caracteres.");
        var newTitle = command.TitleSet ? command.Title!.Trim() : document.Title;
        var newDescription = command.DescriptionSet ? (command.Description ?? "").Trim() : document.Description;
        var decisionJson = ReviewIdentity.MetadataJson(command.TitleSet, command.TitleSet ? newTitle : null, command.DescriptionSet, command.DescriptionSet ? newDescription : null, command.IsConfidentialSet, command.IsConfidential, command.ConfidentialityJustification);
        var replay = await ReplayIfRecordedAsync(caller, command.ExecutionId, command.DocumentId, command.VersionId, AiTask.ExtractMetadata, decisionJson, ct);
        if (replay is not null) return replay;
        if (document.VersionId != command.VersionId) return Fail(409, "A versão atual do documento mudou; revalide a sugestão antes de aplicar.");
        var bound = await BindAsync(connection, caller, command.ExecutionId, command.DocumentId, command.VersionId, AiTask.ExtractMetadata, ct);
        if (bound.Error is not null) return bound.Error;
        var canChangeSecrecy = await CanAsync(caller, document, "Security.Manage", ct);
        var decision = ConfidentialityDecision.Resolve(document.Confidential, command.IsConfidentialSet, command.IsConfidential, canChangeSecrecy);
        if (decision.Error is not null) return decision.Error.Contains("permissão", StringComparison.OrdinalIgnoreCase) ? await DenyAsync(caller, document, command.VersionId, "secrecy_permission_missing", ct) : Fail(400, decision.Error);
        if (decision.Changed && string.IsNullOrWhiteSpace(command.ConfidentialityJustification)) return Fail(400, "Informe a justificativa da alteração de sigilo.");
        var newConfidential = decision.Applied ?? document.Confidential;
        var mutate = newTitle != document.Title || newDescription != document.Description || newConfidential != document.Confidential;
        var record = Record(caller, command.ExecutionId, document, AiTask.ExtractMetadata, decisionJson, mutate ? "Applied" : "Recorded", false, "AI_METADATA_APPLY", "Metadados documentais revisados a partir de sugestão de IA", new { versionId = command.VersionId, before = new { document.Title, document.Description, isConfidential = document.Confidential }, decision = JsonSerializer.Deserialize<JsonElement>(decisionJson), after = new { title = newTitle, description = newDescription, isConfidential = newConfidential }, reviewer = caller.UserId });
        var written = await documents.ApplyMetadataAsync(record, command.ConcurrencyToken, newTitle, newDescription, newConfidential, mutate, ct);
        if (written.Code is "AlreadyApplied" or "DecisionConflict") return FromReview(written);
        return FromWrite(written, new { success = true, alreadyApplied = false, partial = false, retentionPending = false, retentionRecalculated = false, concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, title = newTitle, description = newDescription, isConfidential = newConfidential, message = mutate ? "Metadados gravados." : "Nenhum valor mudou. A revisão foi registrada uma única vez." });
    }

    public async Task<AssistResponse> SuggestDocumentTypeAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var available = await connection.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from ged.document_type where tenant_id=@tenantId and coalesce(reg_status,'A')='A'", new { tenantId = caller.TenantId }, cancellationToken: ct));
        var types = (await connection.QueryAsync<NamedOption>(new CommandDefinition("select id \"Id\", name \"Name\" from ged.document_type where tenant_id=@tenantId and coalesce(reg_status,'A')='A' order by name limit @limit", new { tenantId = caller.TenantId, limit = CandidateLimit }, cancellationToken: ct))).ToList();
        return await SuggestCatalogAsync(versionId, idempotencyKey, caller, AiTask.SuggestClassification, TypeSchema, types.Select(x => x.Name).ToArray(), available, "tipo documental", "typeName", $"Sugira o tipo documental usando somente os nomes autorizados, ou outcome=insufficient. Não escolha classe do plano de classificação. Não invente tipo, prazo ou norma. Nomes: {string.Join(", ", types.Select(x => x.Name))}.", (document, suggestion) =>
        {
            var match = suggestion.HasSuggestion ? types.FirstOrDefault(x => string.Equals(x.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase)) : null;
            return Ok(new { success = true, journey = "document-type", executionId = document.ExecutionId, state = "Completed", correlationId = document.CorrelationId, reviewRequired = true, concurrencyToken = document.Token, document.Title, document.VersionNumber, sources = Sources(document.Document), coverage = document.Coverage, currentTypeId = document.Document.TypeId, currentTypeName = document.Document.TypeName, suggestion = new { hasSuggestion = match is not null, typeId = match?.Id, typeName = match?.Name, suggestion.Evidence, suggestion.Justification, note = match is null && suggestion.HasSuggestion ? "O nome sugerido não pertence aos candidatos autorizados." : suggestion.Note }, types = types.Select(x => new { x.Id, x.Name }) });
        }, ct);
    }

    public Task<AssistResponse> ApplyDocumentTypeAsync(ApplyCatalogCommand command, AssistCaller caller, CancellationToken ct) =>
        ApplyCatalogAsync(command, caller, AiTask.SuggestClassification, "select name from ged.document_type where tenant_id=@tenantId and id=@id and coalesce(reg_status,'A')='A'", "AI_CLASSIFICATION_APPLY", "Tipo documental aplicado a partir de sugestão de IA", (application, token, id, mutate, tokenCt) => documents.ApplyDocumentTypeAsync(application, token, id, mutate, tokenCt), ct);

    public async Task<AssistResponse> SuggestArchivalClassAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct)
    {
        try
        {
            await using var connection = await db.OpenAsync(ct);
            const string countSql = """
select count(*) from ged.classification_plan_version_item i
where i.tenant_id=@tenantId and coalesce(i.is_active,true) and i.version_id=(select v.id from ged.classification_plan_version v where v.tenant_id=@tenantId order by v.version_no desc limit 1)
""";
            var available = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, new { tenantId = caller.TenantId }, cancellationToken: ct));
            var classes = (await connection.QueryAsync<ClassOption>(new CommandDefinition("""
select i.classification_id "Id", i.code "Code", i.name "Name"
from ged.classification_plan_version_item i
where i.tenant_id=@tenantId and coalesce(i.is_active,true)
  and i.version_id=(select v.id from ged.classification_plan_version v where v.tenant_id=@tenantId order by v.version_no desc limit 1)
order by i.code limit @limit
""", new { tenantId = caller.TenantId, limit = CandidateLimit }, cancellationToken: ct))).ToList();
            if (classes.Count == 0) return Fail(422, "O plano de classificação vigente não possui classes ativas.");
            return await SuggestCatalogAsync(versionId, idempotencyKey, caller, AiTask.SuggestArchivalClassification, ArchivalSchema, classes.Select(x => x.Code).ToArray(), available, "classificação arquivística", "classCode", $"Escolha somente um código do plano vigente, ou outcome=insufficient. Não invente código, prazo, evento ou destino. Códigos: {string.Join(", ", classes.Select(x => $"{x.Code} ({x.Name})"))}.", (document, suggestion) =>
            {
                var match = suggestion.HasSuggestion ? classes.FirstOrDefault(x => string.Equals(x.Code, suggestion.Name, StringComparison.OrdinalIgnoreCase)) : null;
                return Ok(new { success = true, journey = "archival-class", executionId = document.ExecutionId, state = "Completed", correlationId = document.CorrelationId, reviewRequired = true, concurrencyToken = document.Token, document.Title, document.VersionNumber, sources = Sources(document.Document), coverage = document.Coverage, currentClassificationId = document.Document.ClassificationId, suggestion = new { hasSuggestion = match is not null, classificationId = match?.Id, code = match?.Code, name = match?.Name, suggestion.Evidence, suggestion.Justification, note = suggestion.Note }, classes = classes.Select(x => new { x.Id, x.Code, x.Name }) });
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable) { return Fail(422, "O catálogo do plano de classificação não está disponível neste ambiente."); }
    }

    public Task<AssistResponse> ApplyArchivalClassAsync(ApplyCatalogCommand command, AssistCaller caller, CancellationToken ct) =>
        ApplyCatalogAsync(command, caller, AiTask.SuggestArchivalClassification, """
select i.code from ged.classification_plan_version_item i
where i.tenant_id=@tenantId and i.classification_id=@id and coalesce(i.is_active,true)
  and i.version_id=(select v.id from ged.classification_plan_version v where v.tenant_id=@tenantId order by v.version_no desc limit 1)
""", "AI_ARCHIVAL_APPLY", "Classificação arquivística aplicada a partir de sugestão de IA", (application, token, id, mutate, tokenCt) => documents.ApplyArchivalClassAsync(application, token, id, mutate, tokenCt), ct);

    private async Task<AssistResponse> ApplyCatalogAsync(ApplyCatalogCommand command, AssistCaller caller, AiTask task, string nameSql, string auditAction, string auditMessage, Func<AssistedApplicationRecord, long, Guid, bool, CancellationToken, Task<AssistedWriteResult>> write, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadByDocumentAsync(connection, caller.TenantId, command.DocumentId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "EDIT", ct)) return await DenyAsync(caller, document, command.VersionId, "edit_permission_missing", ct);
        var decisionJson = ReviewIdentity.CatalogJson(task.ToString(), command.SelectedId);
        var replay = await ReplayIfRecordedAsync(caller, command.ExecutionId, command.DocumentId, command.VersionId, task, decisionJson, ct);
        if (replay is not null) return replay;
        if (document.VersionId != command.VersionId) return Fail(409, "A versão atual do documento mudou; revalide a sugestão antes de aplicar.");
        var bound = await BindAsync(connection, caller, command.ExecutionId, command.DocumentId, command.VersionId, task, ct);
        if (bound.Error is not null) return bound.Error;
        string? selectedName = null;
        try
        {
            selectedName = command.SelectedId is null || command.SelectedId == Guid.Empty ? null : await connection.ExecuteScalarAsync<string?>(new CommandDefinition(nameSql, new { tenantId = caller.TenantId, id = command.SelectedId }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable) { return Fail(422, "O catálogo do plano de classificação não está disponível neste ambiente."); }
        if (command.SelectedId is not null && command.SelectedId != Guid.Empty && string.IsNullOrWhiteSpace(selectedName)) return Fail(400, "O item escolhido não pertence ao catálogo autorizado vigente.");
        var currentId = task == AiTask.SuggestArchivalClassification ? document.ClassificationId : document.TypeId;
        var mutate = command.SelectedId is not null && command.SelectedId != Guid.Empty && command.SelectedId != currentId;
        var record = Record(caller, command.ExecutionId, document with { VersionId = command.VersionId }, task, decisionJson, mutate ? "Applied" : "Recorded", mutate, auditAction, auditMessage, new { command.VersionId, selectedId = command.SelectedId, selectedName, reviewer = caller.UserId });
        AssistedWriteResult written;
        try { written = await write(record, command.ConcurrencyToken, command.SelectedId ?? Guid.Empty, mutate, ct); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable) { return Fail(422, "O catálogo do plano de classificação não está disponível neste ambiente."); }
        catch (Exception) { return Fail(500, "A gravação foi revertida. Nenhum dado desta tentativa permanece aplicado."); }
        if (written.Code is "Conflict" or "NotFound" or "DecisionConflict" or "AlreadyApplied") return written.Code is "Conflict" or "NotFound" ? FromWrite(written, null) : FromReview(written);
        if (!mutate || written.PendingId is null) return Ok(new { success = true, alreadyApplied = false, partial = false, retentionRecalculated = false, retentionPending = false, retentionState = "nao-aplicavel", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, message = "Nenhuma alteração de cadastro foi necessária. A revisão foi registrada." });
        return await FinishRetentionAsync(caller, command.DocumentId, written, ct);
    }

    private async Task<AssistResponse> SuggestAsync(Guid versionId, string idempotencyKey, AssistCaller caller, AiTask task, string schemaJson, bool requireOcr, bool truncate, string instructions, Func<IDbConnection, WorkDocument, AiResult, long, Task<AssistResponse>> project, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        if (!Guid.TryParse(idempotencyKey, out _)) return Fail(400, "Identificador da solicitação inválido.");
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadAsync(connection, caller.TenantId, versionId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "VIEW", ct)) return await DenyAsync(caller, document, versionId, "authorization_missing", ct);
        document.Text = await OcrAsync(connection, caller.TenantId, document, ct);
        if (string.IsNullOrWhiteSpace(document.Text))
        {
            if (requireOcr) return Fail(422, "Esta versão não possui texto OCR para esta sugestão.");
            document.Text = $"Título: {document.Title}\nDescrição: {document.Description}";
        }
        if (document.Text.Length > TextLimit)
        {
            if (!truncate) return Fail(422, "O documento excede 120.000 caracteres. Nenhum conteúdo foi enviado nem truncado.");
            document.Text = document.Text[..TextLimit];
            document.Truncated = true;
        }
        using var schema = JsonDocument.Parse(schemaJson);
        var result = await ai.ExecuteAsync(new(caller.TenantId, caller.UserId, task, instructions, [new(Reference(document), document.Text)], schema, idempotencyKey, Sources: [new AiExecutionSource(document.DocumentId, document.VersionId)]), ct);
        if (!result.Success || result.ExecutionId is null || result.StructuredData is null) return ProviderFailure(result);
        var current = await CurrentAsync(connection, caller.TenantId, document.DocumentId, ct);
        if (current is null || current.VersionId != document.VersionId || !await CanAsync(caller, document with { Confidential = current.Confidential }, "VIEW", ct))
            return await DenyAsync(caller, document, versionId, "authorization_or_state_changed", ct, 409, "O acesso, a situação ou a versão mudou durante a sugestão; o resultado foi descartado.");
        return await project(connection, document with { Token = current.Token }, result, current.Token);
    }

    private async Task<AssistResponse> SuggestCatalogAsync(Guid versionId, string idempotencyKey, AssistCaller caller, AiTask task, string schemaJson, IReadOnlyCollection<string> names, int available, string journey, string nameProperty, string instructions, Func<CatalogContext, CatalogSuggestion, AssistResponse> project, CancellationToken ct)
    {
        if (names.Count == 0) return Fail(422, $"Nenhum {journey} ativo está disponível para este cliente.");
        return await SuggestAsync(versionId, idempotencyKey, caller, task, schemaJson, false, true, instructions, (connection, document, result, token) =>
        {
            var notes = new List<string>();
            if (document.Truncated) notes.Add("O texto enviado ao modelo foi limitado a 120.000 caracteres. A cobertura do conteúdo é parcial.");
            if (available > names.Count) notes.Add($"Cobertura parcial do catálogo: {names.Count} de {available} candidatos autorizados foram apresentados ao modelo.");
            var suggestion = CatalogSuggestionReader.Read(result.StructuredData!.RootElement, document.Text, nameProperty);
            if (!string.IsNullOrWhiteSpace(suggestion.Note)) notes.Add(suggestion.Note);
            var catalog = new CatalogContext(document, result.ExecutionId!.Value, result.CorrelationId, token, new { partial = document.Truncated || available > names.Count || !suggestion.HasSuggestion, considered = names.Count, available, notes });
            return Task.FromResult(project(catalog, suggestion));
        }, ct);
    }

    private async Task<(AiExecutionStatus? Execution, AssistResponse? Error)> BindAsync(IDbConnection connection, AssistCaller caller, Guid executionId, Guid documentId, Guid versionId, AiTask task, CancellationToken ct)
    {
        var execution = await governance.GetExecutionAsync(caller.TenantId, caller.UserId, executionId, ct);
        if (execution is null) return (null, Fail(404, "Não há execução correspondente para este revisor."));
        if (execution.ResultMalformed) return (null, Fail(422, "O resultado persistido está malformado e não pode ser aplicado."));
        if (execution.Task != task) return (null, Fail(409, "A execução não pertence a esta tarefa."));
        if (execution.State != AiExecutionState.Completed || execution.Result is not { Success: true } || execution.ResultExpiresAt is null || execution.ResultExpiresAt <= DateTimeOffset.UtcNow)
            return (null, Fail(409, "O resultado expirou ou não está vigente e não pode ser aplicado."));
        var sources = await AuthorizedSourcesAsync(execution, caller, ct);
        if (sources.Error is not null) return (null, sources.Error);
        if (sources.Rows.All(x => x.DocumentId != documentId || x.VersionId != versionId)) return (null, Fail(409, "A execução não está vinculada a este documento e versão."));
        return (execution, null);
    }

    private async Task<(IReadOnlyList<SourceRow> Rows, AssistResponse? Error)> AuthorizedSourcesAsync(AiExecutionStatus execution, AssistCaller caller, CancellationToken ct)
    {
        if (execution.SourceIntegrity is AiSourceIntegrity.Corrupted) return ([], Fail(422, "As fontes da execução estão corrompidas e o resultado permanece inacessível."));
        if (execution.SourceIntegrity is AiSourceIntegrity.Missing || execution.Sources is not { Count: > 0 }) return ([], Fail(422, "A execução não possui fontes comprováveis e o resultado permanece inacessível."));
        await using var connection = await db.OpenAsync(ct);
        var ids = execution.Sources.Select(x => x.VersionId).Distinct().ToArray();
        var rows = (await connection.QueryAsync<SourceRow>(new CommandDefinition("""
select v.id "VersionId", d.id "DocumentId", coalesce(nullif(d.title,''),'Documento sem título') "Title", v.version_number "VersionNumber", coalesce(d.is_confidential,false) "Confidential"
from ged.document_version v join ged.document d on d.id=v.document_id and d.tenant_id=v.tenant_id
where v.tenant_id=@tenantId and v.id=any(@ids) and coalesce(d.reg_status,'A')='A' and d.status<>'ARCHIVED'::ged.document_status_enum
""", new { tenantId = caller.TenantId, ids }, cancellationToken: ct))).ToDictionary(x => x.VersionId);
        foreach (var source in execution.Sources)
        {
            if (!rows.TryGetValue(source.VersionId, out var row) || row.DocumentId != source.DocumentId) return ([], Fail(422, "O vínculo legado não tem correspondência verificável e o resultado permanece inacessível."));
            if (!await CanAsync(caller, new WorkDocument(row.DocumentId, row.VersionId, row.VersionNumber, row.Title, "", row.Confidential, null, null, null, 0), "VIEW", ct))
                return ([], await DenyAsync(caller, new WorkDocument(row.DocumentId, row.VersionId, 0, row.Title, "", row.Confidential, null, null, null, 0), row.VersionId, "authorization_or_state_changed", ct));
        }
        if (execution.SourceIntegrity == AiSourceIntegrity.LegacyFormat) await governance.MigrateVerifiedLegacySourcesAsync(caller.TenantId, caller.UserId, execution.ExecutionId, execution.Sources.ToArray(), ct);
        return (execution.Sources.Select(x => rows[x.VersionId]).ToArray(), null);
    }

    private AssistedApplicationRecord Record(AssistCaller caller, Guid executionId, WorkDocument document, AiTask task, string decisionJson, string outcome, bool queueRetention, string action, string message, object details) =>
        new(caller.TenantId, executionId, document.DocumentId, document.VersionId, task.ToString(), caller.UserId, ReviewIdentity.Fingerprint(decisionJson), decisionJson, outcome, queueRetention, action, message, JsonSerializer.Serialize(details), caller.Ip, caller.UserAgent, executionId.ToString("N"), queueRetention, ReviewIdentity.OperationKey(caller.TenantId, executionId, document.DocumentId, document.VersionId, caller.UserId, task.ToString()));

    private async Task<AssistResponse?> ReplayIfRecordedAsync(AssistCaller caller, Guid executionId, Guid documentId, Guid versionId, AiTask task, string decisionJson, CancellationToken ct)
    {
        var existing = await documents.FindReviewAsync(caller.TenantId, ReviewIdentity.OperationKey(caller.TenantId, executionId, documentId, versionId, caller.UserId, task.ToString()), ct);
        if (existing is null) return null;
        var fingerprint = ReviewIdentity.Fingerprint(decisionJson);
        if (!ReviewIdentity.Equivalent(existing.Fingerprint, existing.DecisionJson, fingerprint, decisionJson))
            return Fail(409, "A mesma revisão já foi registrada com outra decisão. Gere uma nova sugestão para revisar de novo.");
        return FromStored(existing);
    }

    private async Task<AssistResponse> FinishRetentionAsync(AssistCaller caller, Guid documentId, AssistedWriteResult written, CancellationToken ct)
    {
        var claimToken = Guid.NewGuid();
        RetentionClaim? claim = null;
        try
        {
            claim = await documents.ClaimRetentionAsync(caller.TenantId, written.PendingId!.Value, claimToken, ct);
            if (claim is null)
                return Ok(new { success = true, alreadyApplied = false, partial = true, retentionRecalculated = false, retentionPending = true, retentionState = "em processamento", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, message = "A classificação foi gravada. O recálculo de temporalidade está em processamento e permanece recuperável. HOLD, empréstimos e impedimentos não foram alterados." });
            await retention.RunOneAsync(caller.TenantId, documentId, 30, ct);
            var resolved = await documents.ResolveRetentionAsync(caller.TenantId, claim.Id, claimToken, CancellationToken.None);
            var recovered = resolved && claim.Attempts > 1;
            return Ok(new { success = true, alreadyApplied = false, partial = false, retentionRecalculated = true, retentionPending = false, retentionState = recovered ? "recuperada" : "concluida", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, message = recovered ? "Pendência de temporalidade recuperada. HOLD, empréstimos e impedimentos foram preservados." : "Gravação concluída e temporalidade recalculada. HOLD, empréstimos e impedimentos foram preservados." });
        }
        catch (OperationCanceledException)
        {
            if (claim is not null) await documents.FailRetentionAsync(caller.TenantId, claim.Id, claimToken, "temporalidade:cancelada", CancellationToken.None);
            return Ok(new { success = true, alreadyApplied = false, partial = true, retentionRecalculated = false, retentionPending = true, retentionState = "pendente", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, message = "Conclusão parcial: a classificação permanece gravada e o recálculo ficou pendente após o cancelamento. HOLD, empréstimos e impedimentos não foram alterados." });
        }
        catch (Exception ex)
        {
            var code = ex is PostgresException pg ? "temporalidade:" + pg.SqlState : "temporalidade:falha";
            if (claim is not null) await documents.FailRetentionAsync(caller.TenantId, claim.Id, claimToken, code, CancellationToken.None);
            return Ok(new { success = true, alreadyApplied = false, partial = true, retentionRecalculated = false, retentionPending = true, retentionState = "pendente", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, message = "Conclusão parcial: a classificação foi gravada e a pendência de temporalidade permanece para recuperação. HOLD, empréstimos e impedimentos não foram alterados." });
        }
    }

    public async Task<AssistResponse> ListReviewsAsync(Guid documentId, int page, int pageSize, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadByDocumentAsync(connection, caller.TenantId, documentId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "VIEW", ct)) return await DenyAsync(caller, document, document.VersionId, "authorization_missing", ct);
        var rows = await documents.ListReviewsAsync(caller.TenantId, documentId, (page - 1) * pageSize, pageSize, ct);
        var total = rows.Count == 0 ? 0 : rows[0].Total;
        return Ok(new
        {
            success = true,
            page,
            pageSize,
            total,
            items = rows.Select(row =>
            {
                var expired = row.ResultExpiresAt is null || row.ResultExpiresAt <= DateTimeOffset.UtcNow || string.Equals(row.ExecutionState, "Expired", StringComparison.OrdinalIgnoreCase);
                var pendingOpen = row.PendingId is not null && row.ResolvedAt is null;
                var pendingResolved = row.PendingId is not null && row.ResolvedAt is not null;
                JsonElement? suggestion = null;
                return new
                {
                    row.Id,
                    kind = row.Kind,
                    situation = ReviewIdentity.Situation(row.Kind, row.Outcome, pendingOpen, pendingResolved, row.Attempts, expired),
                    task = row.Task,
                    row.DocumentId,
                    row.VersionId,
                    at = row.CreatedAt,
                    reviewerId = row.ReviewerId,
                    outcome = row.Outcome,
                    aiResultExpired = expired,
                    retentionState = row.PendingId is null ? "ausente" : pendingOpen ? "pendente" : "resolvida",
                    attempts = row.PendingId is null ? (int?)null : row.Attempts,
                    lastError = row.LastError,
                    fields = row.Kind == "application" ? ReviewIdentity.Fields(row.DecisionJson, false, suggestion) : []
                };
            })
        });
    }

    public async Task<AssistResponse> ListRetentionAsync(Guid documentId, int page, int pageSize, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadByDocumentAsync(connection, caller.TenantId, documentId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "VIEW", ct)) return await DenyAsync(caller, document, document.VersionId, "authorization_missing", ct);
        var rows = await documents.ListRetentionAsync(caller.TenantId, documentId, true, (page - 1) * pageSize, pageSize, ct);
        return Ok(new { success = true, page, pageSize, items = rows });
    }

    public async Task<AssistResponse> RetryRetentionAsync(Guid documentId, Guid pendingId, AssistCaller caller, CancellationToken ct)
    {
        if (!caller.Authenticated) return new(401, new { success = false, message = "Autenticação obrigatória." });
        await using var connection = await db.OpenAsync(ct);
        var document = await LoadByDocumentAsync(connection, caller.TenantId, documentId, ct);
        if (document is null) return Fail(404, "Documento não encontrado.");
        if (!await CanAsync(caller, document, "EDIT", ct)) return await DenyAsync(caller, document, document.VersionId, "edit_permission_missing", ct);
        var pending = await documents.GetRetentionAsync(caller.TenantId, pendingId, ct);
        if (pending is null || pending.DocumentId != documentId) return Fail(404, "Pendência não encontrada.");
        if (pending.ResolvedAt is not null)
        {
            await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_RETENTION_RETRY", "DOCUMENT", documentId, "Recuperação de temporalidade já estava concluída", caller.Ip, caller.UserAgent, new { pendingId, pending.State, pending.Attempts }, ct);
            return Ok(new { success = true, retentionPending = false, retentionRecalculated = true, retentionState = "resolvida", partial = false, message = "A pendência já estava resolvida." });
        }
        var claimToken = Guid.NewGuid();
        var claim = await documents.ClaimRetentionAsync(caller.TenantId, pendingId, claimToken, ct);
        if (claim is null)
        {
            await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_RETENTION_RETRY", "DOCUMENT", documentId, "Recuperação de temporalidade já em processamento", caller.Ip, caller.UserAgent, new { pendingId, state = "em processamento" }, ct);
            return Ok(new { success = true, retentionPending = true, retentionRecalculated = false, retentionState = "em processamento", partial = true, message = "O recálculo já está em processamento." });
        }
        try
        {
            await retention.RunOneAsync(caller.TenantId, documentId, 30, ct);
            await documents.ResolveRetentionAsync(caller.TenantId, claim.Id, claimToken, CancellationToken.None);
            await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_RETENTION_RETRY", "DOCUMENT", documentId, "Recuperação de temporalidade concluída", caller.Ip, caller.UserAgent, new { pendingId, claim.Attempts, state = "resolvida" }, ct);
            return Ok(new { success = true, retentionPending = false, retentionRecalculated = true, retentionState = claim.Attempts > 1 ? "recuperada" : "concluida", partial = false, attempts = claim.Attempts, message = "Pendência de temporalidade recuperada. HOLD, empréstimos e impedimentos foram preservados." });
        }
        catch (Exception ex)
        {
            var code = ex is OperationCanceledException ? "temporalidade:cancelada" : ex is PostgresException pg ? "temporalidade:" + pg.SqlState : "temporalidade:falha";
            await documents.FailRetentionAsync(caller.TenantId, claim.Id, claimToken, code, CancellationToken.None);
            await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_RETENTION_RETRY", "DOCUMENT", documentId, "Recuperação de temporalidade permanece pendente", caller.Ip, caller.UserAgent, new { pendingId, claim.Attempts, error = code }, ct);
            return Ok(new { success = false, retentionPending = true, retentionRecalculated = false, retentionState = "pendente", partial = true, attempts = claim.Attempts, message = "A nova tentativa não concluiu o recálculo. A pendência continua recuperável." });
        }
    }

    private static AssistResponse FromStored(StoredReview existing)
    {
        var pending = existing.RetentionPending;
        var concluded = !pending && string.Equals(existing.Outcome, "Applied", StringComparison.OrdinalIgnoreCase);
        var message = pending
            ? "Esta revisão já estava registrada. O recálculo de temporalidade ainda está pendente."
            : concluded
                ? "Esta revisão já estava registrada. O recálculo vinculado está concluído. Nenhum efeito foi duplicado."
                : "Esta revisão já estava registrada, sem nova auditoria nem novo recálculo.";
        return Ok(new { success = true, alreadyApplied = true, partial = pending, retentionPending = pending, retentionRecalculated = concluded && !pending, retentionState = pending ? "pendente" : concluded ? "concluida" : "nao-aplicavel", applicationId = existing.Id, attempts = existing.RetentionAttempts, message });
    }

    private static AssistResponse FromReview(AssistedWriteResult written)
    {
        if (written.Code == "DecisionConflict") return Fail(409, written.Message ?? "A mesma revisão já foi registrada com outra decisão.");
        var pending = written.RetentionPending;
        var concluded = !pending && string.Equals(written.Outcome, "Applied", StringComparison.OrdinalIgnoreCase);
        var message = pending
            ? "Esta revisão já estava registrada. O recálculo de temporalidade ainda está pendente."
            : concluded
                ? "Esta revisão já estava registrada. O recálculo vinculado está concluído. Nenhum efeito foi duplicado."
                : "Esta revisão já estava registrada, sem nova auditoria nem novo recálculo.";
        return Ok(new { success = true, alreadyApplied = true, partial = pending, retentionPending = pending, retentionRecalculated = concluded && !pending, retentionState = pending ? "pendente" : concluded ? "concluida" : "nao-aplicavel", concurrencyToken = written.ConcurrencyToken, applicationId = written.ApplicationId, attempts = written.RetentionAttempts, message });
    }

    private async Task<bool> CanAsync(AssistCaller caller, WorkDocument document, string action, CancellationToken ct) =>
        await authorization.CanAccessDocumentAsync(caller.TenantId, caller.UserId, document.DocumentId, action, new Dictionary<string, string> { ["classification"] = document.Confidential ? "SENSITIVE" : "PUBLIC" }, ct);

    private async Task<AssistResponse> DenyAsync(AssistCaller caller, WorkDocument document, Guid versionId, string reason, CancellationToken ct, int status = 403, string? message = null)
    {
        await audit.WriteAsync(caller.TenantId, caller.UserId, "AI_ACCESS_DENIED", "DOCUMENT", document.DocumentId, "Acesso a processamento de IA negado", caller.Ip, caller.UserAgent, new { versionId, reason }, ct);
        return new(status, new { success = false, message = message ?? "Acesso negado." }, Denied: status == 403);
    }

    private static AssistResponse FromWrite(AssistedWriteResult written, object? success) => written.Code switch
    {
        "Conflict" => Fail(409, written.Message ?? "O documento foi alterado por outra edição."),
        "NotFound" => Fail(404, written.Message ?? "Documento não encontrado."),
        _ => Ok(success)
    };

    private static AssistResponse ProviderFailure(AiResult result) => Fail(result.Failure == AiFailureKind.Disabled ? 403 : result.Failure == AiFailureKind.IdempotencyConflict ? 409 : 422, result.Limitation ?? "A IA não concluiu a solicitação.", result.CorrelationId);
    private static AssistResponse Ok(object? body) => new(200, body);
    private static AssistResponse Fail(int status, string message, string? correlation = null) => new(status, new { success = false, message, correlationId = correlation });
    private static string Reference(WorkDocument document) => $"{document.DocumentId:N}/{document.VersionId:N}/texto-extraido";
    private static object Sources(WorkDocument document) => new[] { new { document.DocumentId, document.VersionId, document.Title, document.VersionNumber } };
    private static string Label(string name) => name switch { "title" => "Título", "description" => "Descrição", "isConfidential" => "Sigilo", _ => name };
    private static bool SummaryEvidenceIsLiteral(JsonElement root, string source) { foreach (var name in new[] { "facts", "dates", "pending" }) { if (!root.TryGetProperty(name, out var items)) return false; foreach (var item in items.EnumerateArray()) { var evidence = item.GetProperty("evidence").GetString(); if (string.IsNullOrWhiteSpace(evidence) || !source.Contains(evidence, StringComparison.OrdinalIgnoreCase)) return false; } } return true; }
    private static async Task<string> OcrAsync(IDbConnection connection, Guid tenantId, WorkDocument document, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<string?>(new CommandDefinition("select ocr_text from ged.document_search where tenant_id=@tenantId and document_id=@documentId and version_id=@versionId", new { tenantId, documentId = document.DocumentId, versionId = document.VersionId }, cancellationToken: ct)) ?? "";

    private static Task<WorkDocument?> LoadAsync(IDbConnection connection, Guid tenantId, Guid versionId, CancellationToken ct) => connection.QuerySingleOrDefaultAsync<WorkDocument>(new CommandDefinition("""
select d.id "DocumentId", v.id "VersionId", v.version_number "VersionNumber", coalesce(nullif(d.title,''),'') "Title", coalesce(nullif(d.description,''),'') "Description", coalesce(d.is_confidential,false) "Confidential", d.type_id "TypeId", t.name "TypeName", d.classification_id "ClassificationId", d.xmin::text::bigint "Token"
from ged.document_version v join ged.document d on d.id=v.document_id and d.tenant_id=v.tenant_id
left join ged.document_type t on t.id=d.type_id and t.tenant_id=d.tenant_id
where v.tenant_id=@tenantId and v.id=@versionId and coalesce(d.reg_status,'A')='A' and d.status<>'ARCHIVED'::ged.document_status_enum
""", new { tenantId, versionId }, cancellationToken: ct));

    private static Task<WorkDocument?> LoadByDocumentAsync(IDbConnection connection, Guid tenantId, Guid documentId, CancellationToken ct) => connection.QuerySingleOrDefaultAsync<WorkDocument>(new CommandDefinition("""
select d.id "DocumentId", d.current_version_id "VersionId", 0 "VersionNumber", coalesce(nullif(d.title,''),'') "Title", coalesce(nullif(d.description,''),'') "Description", coalesce(d.is_confidential,false) "Confidential", d.type_id "TypeId", t.name "TypeName", d.classification_id "ClassificationId", d.xmin::text::bigint "Token"
from ged.document d left join ged.document_type t on t.id=d.type_id and t.tenant_id=d.tenant_id
where d.tenant_id=@tenantId and d.id=@documentId and coalesce(d.reg_status,'A')='A' and d.status<>'ARCHIVED'::ged.document_status_enum
""", new { tenantId, documentId }, cancellationToken: ct));

    private static async Task<CurrentRow?> CurrentAsync(IDbConnection connection, Guid tenantId, Guid documentId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition("select current_version_id \"VersionId\", coalesce(is_confidential,false) \"Confidential\", xmin::text::bigint \"Token\" from ged.document where tenant_id=@tenantId and id=@documentId and coalesce(reg_status,'A')='A' and status<>'ARCHIVED'::ged.document_status_enum", new { tenantId, documentId }, cancellationToken: ct));

    private sealed class SourceRow { public Guid VersionId { get; set; } public Guid DocumentId { get; set; } public string Title { get; set; } = ""; public int VersionNumber { get; set; } public bool Confidential { get; set; } }
    private sealed class NamedOption { public Guid Id { get; set; } public string Name { get; set; } = ""; }
    private sealed class ClassOption { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
    private sealed record CurrentRow(Guid VersionId, bool Confidential, long Token);
    private sealed record CatalogContext(WorkDocument Document, Guid ExecutionId, string? CorrelationId, long Token, object Coverage) { public string Title => Document.Title; public int VersionNumber => Document.VersionNumber; }
}

public sealed record WorkDocument(Guid DocumentId, Guid VersionId, int VersionNumber, string Title, string Description, bool Confidential, Guid? TypeId, string? TypeName, Guid? ClassificationId, long Token)
{
    public string Text { get; set; } = "";
    public bool Truncated { get; set; }
}
