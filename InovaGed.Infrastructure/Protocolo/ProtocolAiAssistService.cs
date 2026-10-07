using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Protocolo;
using InovaGed.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Protocolo;

public sealed class ProtocolAiAssistService(
    IDbConnectionFactory db,
    ICurrentUser currentUser,
    IDocumentAiGateway gateway,
    IProtocolAccessService protocolAccess,
    IAbacAuthorizationService authorization,
    IAuditWriter audit,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ProtocolAiAssistService> logger) : IProtocolAiAssistService
{
    private System.Security.Claims.ClaimsPrincipal GetPrincipal()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is not null && principal.Identity?.IsAuthenticated == true)
            return principal;

        var identity = new System.Security.Claims.ClaimsIdentity("ApplicationAuth");
        if (currentUser.IsAuthenticated)
        {
            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, currentUser.UserId.ToString()));
            if (!string.IsNullOrWhiteSpace(currentUser.Email))
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, currentUser.Email));
            foreach (var role in currentUser.Roles)
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role));
        }
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }

    private async Task<bool> CanOperateProtocolAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid protocolId, Guid userId, CancellationToken ct, IDbTransaction? tx = null)
    {
        var principal = GetPrincipal();
        if (principal.IsInRole("ADMIN") || principal.IsInRole("ADMINISTRADOR") || principal.IsInRole("ADMINISTRADOROPHIR"))
            return true;

        const string sql = """
SELECT EXISTS (
    SELECT 1
    FROM ged.protocolo p
    JOIN ged.protocolo_usuario_setor us ON us.tenant_id=p.tenant_id AND us.setor_id=p.setor_atual_id AND us.usuario_id=@userId AND us.ativo=true AND COALESCE(us.reg_status,'A')='A'
    JOIN ged.protocolo_setor s ON s.tenant_id=p.tenant_id AND s.id=p.setor_atual_id AND s.ativo=true AND COALESCE(s.reg_status,'A')='A'
    WHERE p.tenant_id=@tenantId AND p.id=@protocolId AND p.reg_status='A'
) OR EXISTS (
    SELECT 1
    FROM ged.user_role ur
    JOIN ged.role r ON r.id=ur.role_id AND r.tenant_id=@tenantId
    WHERE ur.user_id=@userId AND r.code IN ('ADMIN', 'ADMINISTRADOR', 'ADMINISTRADOROPHIR')
);
""";
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, protocolId, userId }, tx, cancellationToken: ct));
    }

    private Task<bool> CanViewProtocolAsync(Guid tenantId, Guid protocolId, Guid userId, CancellationToken ct) =>
        protocolAccess.CanViewProtocolAsync(tenantId, protocolId, userId, GetPrincipal(), ct);

    public async Task<ProtocolAiAssistResultDto> AssistAsync(ProtocolAiAssistRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");

        if (!TryParseTaskKind(request.TaskKind, out var taskKind))
            throw new ArgumentException("Modalidade de assistência do Protocolo inválida.", nameof(request.TaskKind));

        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;

        await using var conn = await db.OpenAsync(ct);

        // 1. Validar autorização institucional de visualização do protocolo
        var canView = await protocolAccess.CanViewProtocolAsync(tenantId, request.ProtocoloId, userId, GetPrincipal(), ct);
        if (!canView)
            throw new UnauthorizedAccessException("Acesso não autorizado ao protocolo institucional.");

        // 2. Carregar dados do protocolo
        const string sqlProtocol = """
SELECT p.id AS "Id", p.numero AS "Numero", p.assunto AS "Assunto", p.descricao AS "Descricao",
       p.status AS "Status", p.prioridade AS "Prioridade", p.setor_atual_id AS "SetorAtualId",
       sa.nome AS "SetorAtualNome", p.updated_at AS "UpdatedAt", p.created_at AS "CreatedAt"
FROM ged.protocolo p
LEFT JOIN ged.protocolo_setor sa ON sa.id=p.setor_atual_id
WHERE p.tenant_id=@tenantId AND p.id=@protocoloId AND p.reg_status='A'
""";
        var proto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>(new CommandDefinition(sqlProtocol, new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct));
        if (proto is null)
            throw new KeyNotFoundException("Protocolo não encontrado ou inativo.");

        var concurrencyToken = ToConcurrencyToken(proto.UpdatedAt, proto.CreatedAt);

        // 3. Carregar movimentações e despachos recentes
        const string sqlTramitacoes = """
SELECT t.acao AS "Acao", t.despacho AS "Despacho", t.observacao AS "Observacao",
       t.setor_origem_nome AS "SetorOrigemNome", t.setor_destino_nome AS "SetorDestinoNome",
       t.data_tramitacao AS "DataTramitacao"
FROM ged.protocolo_tramitacao t
WHERE t.tenant_id=@tenantId AND t.protocolo_id=@protocoloId AND t.reg_status='A'
ORDER BY t.data_tramitacao DESC
LIMIT 8
""";
        var tramitacoes = (await conn.QueryAsync<TramitacaoRow>(new CommandDefinition(sqlTramitacoes, new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct))).ToList();

        // 4. Carregar documentos GED vinculados autorizados com OCR
        const string sqlVinculos = """
SELECT g.ged_document_id AS "DocumentId", d.title AS "Title", d.is_confidential AS "IsConfidential",
       v.id AS "VersionId", v.version_number AS "VersionNumber", NULL::text AS "FileName",
       s.ocr_text AS "OcrText"
FROM ged.protocolo_documento_ged g
JOIN ged.document d ON d.tenant_id=g.tenant_id AND d.id=g.ged_document_id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=COALESCE(d.current_version_id, (SELECT vx.id FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC LIMIT 1))
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id AND s.version_id=v.id
WHERE g.tenant_id=@tenantId AND g.protocolo_id=@protocoloId AND g.reg_status='A' AND coalesce(d.reg_status,'A')='A'
""";
        var docs = (await conn.QueryAsync<VinculoDocRow>(new CommandDefinition(sqlVinculos, new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct))).ToList();

        var sourcesList = new List<ProtocolAiSourceDto>();
        var contextItems = new List<AiContextItem>();
        var coverageNotes = new List<string>();
        var partial = false;

        // Contexto do próprio processo
        var sbProtoContext = new StringBuilder();
        sbProtoContext.AppendLine($"PROTOCOLO: {proto.Numero}");
        sbProtoContext.AppendLine($"ASSUNTO ATUAL: {proto.Assunto}");
        sbProtoContext.AppendLine($"SETOR ATUAL: {proto.SetorAtualNome ?? "Não informado"}");
        sbProtoContext.AppendLine($"STATUS: {proto.Status}");
        if (!string.IsNullOrWhiteSpace(proto.Descricao))
            sbProtoContext.AppendLine($"DESCRICAO: {proto.Descricao}");

        if (tramitacoes.Count > 0)
        {
            sbProtoContext.AppendLine("\nHISTORICO DE MOVIMENTACOES:");
            foreach (var t in tramitacoes)
            {
                sbProtoContext.AppendLine($"- {t.DataTramitacao:dd/MM/yyyy HH:mm} | Acao: {t.Acao} | De: {t.SetorOrigemNome} Para: {t.SetorDestinoNome}");
                if (!string.IsNullOrWhiteSpace(t.Despacho)) sbProtoContext.AppendLine($"  Despacho: {t.Despacho}");
                if (!string.IsNullOrWhiteSpace(t.Observacao)) sbProtoContext.AppendLine($"  Observacao: {t.Observacao}");
            }
        }

        contextItems.Add(new AiContextItem($"PROTOCOLO-{proto.Id:N}", sbProtoContext.ToString()));
        sourcesList.Add(new ProtocolAiSourceDto
        {
            ProtocolId = proto.Id,
            Title = $"Protocolo {proto.Numero}",
            SourceType = "PROTOCOLO",
            HasOcr = false,
            Evidence = $"Processo {proto.Numero} - {proto.Assunto}"
        });

        // Filtrar documentos por autorização ABAC
        foreach (var doc in docs)
        {
            var canAccessDoc = await authorization.CanAccessDocumentAsync(tenantId, userId, doc.DocumentId, "VIEW", new Dictionary<string, string>(), ct);
            if (!canAccessDoc)
            {
                partial = true;
                coverageNotes.Add($"1 documento vinculado restrito por sigilo/permissão (conteúdo resguardado).");
                continue;
            }

            var hasOcr = !string.IsNullOrWhiteSpace(doc.OcrText);
            sourcesList.Add(new ProtocolAiSourceDto
            {
                DocumentId = doc.DocumentId,
                VersionId = doc.VersionId,
                Title = doc.Title ?? doc.FileName ?? "Documento GED",
                SourceType = "GED_DOCUMENT",
                HasOcr = hasOcr,
                Evidence = hasOcr ? doc.OcrText!.Substring(0, Math.Min(60, doc.OcrText.Length)) : null
            });

            if (hasOcr)
            {
                contextItems.Add(new AiContextItem($"DOC-{doc.Title ?? doc.FileName ?? "GED"}", doc.OcrText!));
            }
            else
            {
                partial = true;
                coverageNotes.Add($"Documento '{doc.Title}' sem texto OCR disponível; conteúdo documental não analisado.");
            }
        }

        // 5. Chamar Gateway Governado com AiTask.SupportProtocol
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? Guid.NewGuid().ToString() : request.IdempotencyKey;
        var aiSources = sourcesList
            .Where(s => s.DocumentId.HasValue && s.VersionId.HasValue)
            .Select(s => new AiExecutionSource(s.DocumentId!.Value, s.VersionId!.Value))
            .ToList();

        var taskLabel = taskKind switch
        {
            ProtocolAiTaskKind.SummarizeProcess => "Produza apenas um resumo consultivo do processo.",
            ProtocolAiTaskKind.DocumentPending => "Identifique apenas possíveis pendências documentais e sustente cada item com trecho literal de fonte identificável; sem comprovação, marque conferência humana.",
            ProtocolAiTaskKind.SuggestSubject => "Produza apenas uma sugestão editável de assunto e sua justificativa.",
            ProtocolAiTaskKind.PrepareDispatchDraft => "Produza apenas uma minuta editável de despacho fundamentada.",
            _ => "Produza resumo consultivo, possíveis pendências documentais, sugestão editável de assunto e minuta fundamentada de despacho."
        };
        using var executionMetadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            protocolId = proto.Id,
            protocolNumber = proto.Numero,
            requestedTaskKind = taskKind.ToString(),
            protocolState = new
            {
                status = proto.Status,
                sectorId = proto.SetorAtualId,
                concurrencyToken
            },
            sources = aiSources.Select(source => new
            {
                documentId = source.DocumentId,
                versionId = source.VersionId
            }),
            coverage = new
            {
                partial,
                notes = coverageNotes,
                totalSources = sourcesList.Count,
                processedSources = contextItems.Count
            }
        }));
        using var outputSchema = BuildProtocolAssistSchema(taskKind);
        var aiRequest = new AiRequest(
            tenantId,
            userId,
            AiTask.SupportProtocol,
            "Você é o assistente institucional do Protocolo InovaGED. Analise o processo, seu histórico e documentos vinculados. " +
            taskLabel + " " +
            "Não invente normas, prazos ou exigências inexistentes. Não execute nenhuma tramitação automaticamente.",
            contextItems,
            outputSchema,
            idempotencyKey,
            Sources: aiSources,
            ExecutionMetadata: executionMetadata
        );

        var aiResult = await gateway.ExecuteAsync(aiRequest, ct);
        if (!aiResult.Success)
        {
            return new ProtocolAiAssistResultDto
            {
                Success = false,
                RequestedTaskKind = taskKind.ToString(),
                ErrorMessage = aiResult.Limitation ?? aiResult.Failure.ToString(),
                CorrelationId = aiResult.CorrelationId,
                ProtocolNumber = proto.Numero,
                CurrentSubject = proto.Assunto,
                CurrentSector = proto.SetorAtualNome,
                CurrentStatus = proto.Status,
                ConcurrencyToken = concurrencyToken,
                Sources = sourcesList,
                Coverage = new ProtocolAiCoverageDto { Partial = partial, Notes = coverageNotes, TotalSources = sourcesList.Count, ProcessedSources = contextItems.Count }
            };
        }

        if (!await CanViewProtocolAsync(tenantId, request.ProtocoloId, userId, ct))
            throw new UnauthorizedAccessException("Acesso ao protocolo foi revogado durante a análise.");
        var currentProto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>(new CommandDefinition(sqlProtocol, new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct));
        if (currentProto is null)
            throw new UnauthorizedAccessException("O protocolo deixou de estar disponível durante a análise.");
        var currentToken = ToConcurrencyToken(currentProto.UpdatedAt, currentProto.CreatedAt);
        var currentDocs = (await conn.QueryAsync<VinculoDocRow>(new CommandDefinition(sqlVinculos, new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct))).ToList();
        if (currentToken != concurrencyToken ||
            !string.Equals(currentProto.Status, proto.Status, StringComparison.Ordinal) ||
            currentProto.SetorAtualId != proto.SetorAtualId ||
            !SameDocumentSnapshot(docs, currentDocs))
        {
            return new ProtocolAiAssistResultDto
            {
                Success = false,
                State = "Conflict",
                RequestedTaskKind = taskKind.ToString(),
                ErrorMessage = "O protocolo, o setor ou as fontes mudaram durante a análise. Nenhum resultado foi apresentado; consulte o estado atual e tente novamente.",
                CorrelationId = aiResult.CorrelationId,
                ProtocolNumber = currentProto.Numero,
                CurrentSubject = currentProto.Assunto,
                CurrentSector = currentProto.SetorAtualNome,
                CurrentStatus = currentProto.Status,
                ConcurrencyToken = currentToken,
                Sources = sourcesList,
                Coverage = new ProtocolAiCoverageDto { Partial = true, Notes = coverageNotes, TotalSources = sourcesList.Count, ProcessedSources = contextItems.Count }
            };
        }
        foreach (var source in aiSources)
        {
            if (!await authorization.CanAccessDocumentAsync(tenantId, userId, source.DocumentId, "VIEW", new Dictionary<string, string>(), ct))
                throw new UnauthorizedAccessException("Acesso a uma fonte foi revogado durante a análise.");
        }

        // 6. Interpretar saída estruturada. Resultado incompleto não vira sucesso.
        if (!TryReadProtocolAssist(aiResult.StructuredData, taskKind, out var parsed, out var validationError))
        {
            return new ProtocolAiAssistResultDto
            {
                Success = false,
                RequestedTaskKind = taskKind.ToString(),
                ErrorMessage = validationError,
                CorrelationId = aiResult.CorrelationId,
                ProtocolNumber = proto.Numero,
                CurrentSubject = proto.Assunto,
                CurrentSector = proto.SetorAtualNome,
                CurrentStatus = proto.Status,
                ConcurrencyToken = concurrencyToken,
                Sources = sourcesList,
                Coverage = new ProtocolAiCoverageDto { Partial = true, Notes = coverageNotes.Concat(["A resposta da IA não atendeu ao contrato estruturado."]).ToArray(), TotalSources = sourcesList.Count, ProcessedSources = contextItems.Count }
            };
        }

        string? summaryText = IncludesTask(taskKind, ProtocolAiTaskKind.SummarizeProcess) ? parsed.Summary : null;
        string? suggestedSubject = IncludesTask(taskKind, ProtocolAiTaskKind.SuggestSubject) ? parsed.SuggestedSubject : null;
        string? suggestedDescription = IncludesTask(taskKind, ProtocolAiTaskKind.SuggestSubject) ? parsed.SuggestedDescription : null;
        string? dispatchDraft = IncludesTask(taskKind, ProtocolAiTaskKind.PrepareDispatchDraft) ? parsed.DispatchDraft : null;
        var pendingItems = parsed.Pending;
        var limitations = parsed.Limitations.Count == 0
            ? new List<string> { "Decisões de tramitação, despacho e assunto exigem revisão humana." }
            : parsed.Limitations;

        foreach (var pending in pendingItems)
        {
            var evidenceSource = FindEvidenceSource(pending.Evidence, sourcesList, docs);
            pending.Status = "CONFERENCIA_HUMANA";
            pending.RequiresHumanCheck = true;
            pending.EvidenceSource = evidenceSource;
        }
        if (pendingItems.Count > 0 && !limitations.Contains("Sugestões de pendência não são tarefas operacionais; confirme-as explicitamente antes de acompanhar como requisito.", StringComparer.Ordinal))
            limitations.Add("Sugestões de pendência não são tarefas operacionais; confirme-as explicitamente antes de acompanhar como requisito.");

        var executionId = aiResult.ExecutionId;
        if (!executionId.HasValue)
        {
            return new ProtocolAiAssistResultDto
            {
                Success = false,
                RequestedTaskKind = taskKind.ToString(),
                ErrorMessage = "A execução governada não foi persistida. Nenhuma decisão pode ser aplicada.",
                CorrelationId = aiResult.CorrelationId,
                ProtocolNumber = proto.Numero,
                CurrentSubject = proto.Assunto,
                CurrentSector = proto.SetorAtualNome,
                CurrentStatus = proto.Status,
                ConcurrencyToken = concurrencyToken,
                Sources = sourcesList,
                Coverage = new ProtocolAiCoverageDto { Partial = true, Notes = coverageNotes.Concat(["Gateway não retornou executionId persistido."]).ToArray(), TotalSources = sourcesList.Count, ProcessedSources = contextItems.Count }
            };
        }

        return new ProtocolAiAssistResultDto
        {
            Success = true,
            ExecutionId = executionId.Value,
            State = "Completed",
            RequestedTaskKind = taskKind.ToString(),
            CorrelationId = aiResult.CorrelationId,
            ReviewRequired = true,
            ConcurrencyToken = concurrencyToken,
            ProtocolNumber = proto.Numero,
            CurrentSubject = proto.Assunto,
            CurrentSector = proto.SetorAtualNome,
            CurrentStatus = proto.Status,
            CurrentDescription = proto.Descricao,
            Summary = summaryText,
            PendingItems = pendingItems,
            SuggestedSubject = suggestedSubject,
            SuggestedDescription = suggestedDescription,
            DispatchDraft = dispatchDraft,
            Sources = sourcesList,
            Coverage = new ProtocolAiCoverageDto
            {
                Partial = partial,
                Notes = coverageNotes,
                TotalSources = sourcesList.Count,
                ProcessedSources = contextItems.Count
            },
            Limitations = limitations
        };
    }

    public async Task<ProtocolPendingConfirmResultDto> ConfirmPendingItemAsync(ProtocolPendingConfirmRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");
        if (request.PendingIndex is < 0 or > 19)
            throw new ArgumentException("Índice de pendência inválido.");

        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;
        if (!await protocolAccess.CanViewProtocolAsync(tenantId, request.ProtocoloId, userId, GetPrincipal(), ct))
            throw new UnauthorizedAccessException("Acesso não autorizado ao protocolo.");

        await using var conn = await db.OpenAsync(ct);
        if (!await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct))
            throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

        var execution = await LoadValidExecutionAsync(conn, tenantId, userId, request.ProtocoloId, request.ExecutionId, "DOCUMENT_PENDING", ct);
        var suggestion = ReadPendingSuggestion(execution.OriginalSuggestionJson, request.PendingIndex);
        var fingerprint = PendingFingerprint(request.ExecutionId, request.PendingIndex, suggestion);
        var protocol = await conn.QuerySingleOrDefaultAsync<ProtocolRow>(new CommandDefinition("""
select id as "Id", status as "Status", setor_atual_id as "SetorAtualId",
       updated_at as "UpdatedAt", created_at as "CreatedAt"
from ged.protocolo
where tenant_id=@tenantId and id=@protocoloId and reg_status='A';
""", new { tenantId, protocoloId = request.ProtocoloId }, cancellationToken: ct));
        if (protocol is null)
            throw new KeyNotFoundException("Protocolo não encontrado.");
        if (ProtocolStatusIsClosed(protocol.Status))
            throw new InvalidOperationException("O protocolo está encerrado e não aceita novas pendências.");
        if (!protocol.SetorAtualId.HasValue)
            throw new InvalidOperationException("O protocolo não possui setor atual para acompanhar pendências.");
        if (request.ConcurrencyToken <= 0 || ToConcurrencyToken(protocol.UpdatedAt, protocol.CreatedAt) != request.ConcurrencyToken)
            throw new InvalidOperationException("Conflito de concorrência: o protocolo mudou desde a geração da sugestão.");

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var locked = await conn.QuerySingleOrDefaultAsync<ProtocolRow>(new CommandDefinition("""
select id as "Id", status as "Status", setor_atual_id as "SetorAtualId",
       updated_at as "UpdatedAt", created_at as "CreatedAt"
from ged.protocolo
where tenant_id=@tenantId and id=@protocoloId and reg_status='A'
for update;
""", new { tenantId, protocoloId = request.ProtocoloId }, tx, cancellationToken: ct));
            if (locked is null)
                throw new KeyNotFoundException("Protocolo não encontrado.");
            if (!string.Equals(locked.Status, protocol.Status, StringComparison.Ordinal) ||
                locked.SetorAtualId != protocol.SetorAtualId ||
                ToConcurrencyToken(locked.UpdatedAt, locked.CreatedAt) != request.ConcurrencyToken)
                throw new InvalidOperationException("Conflito de concorrência: o protocolo mudou desde a geração da sugestão.");
            if (ProtocolStatusIsClosed(locked.Status))
                throw new InvalidOperationException("O protocolo está encerrado e não aceita novas pendências.");
            if (!await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct, tx))
                throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

            var existing = await conn.QuerySingleOrDefaultAsync<PendingIdentityRow>(new CommandDefinition("""
select id as "Id", fingerprint as "Fingerprint"
from ged.protocolo_pendencia
where tenant_id=@tenantId and execution_id=@executionId and item_index=@itemIndex;
""", new { tenantId, executionId = request.ExecutionId, itemIndex = request.PendingIndex }, tx, cancellationToken: ct));
            if (existing is not null)
            {
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("A sugestão desta execução já foi confirmada com conteúdo diferente.");
                await tx.RollbackAsync(ct);
                return new ProtocolPendingConfirmResultDto
                {
                    Success = true,
                    AlreadyApplied = true,
                    PendingId = existing.Id,
                    Message = "Esta sugestão já foi confirmada como pendência."
                };
            }

            var pendingId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
insert into ged.protocolo_pendencia (
    id, tenant_id, protocolo_id, setor_id, descricao, evidencia, fonte_evidencia,
    origem, status, execution_id, item_index, fingerprint, confirmada_por,
    confirmada_em, created_at, updated_at, reg_status
) values (
    @id, @tenantId, @protocoloId, @setorId, @description, @evidence, @evidenceSource,
    'ASSISTENTE_IA', 'ABERTA', @executionId, @itemIndex, @fingerprint, @userId,
    now(), now(), now(), 'A'
)
on conflict (tenant_id, execution_id, item_index) where execution_id is not null do nothing
returning id;
""", new
            {
                id = Guid.NewGuid(),
                tenantId,
                protocoloId = request.ProtocoloId,
                setorId = locked.SetorAtualId,
                description = suggestion.Item,
                evidence = suggestion.Evidence,
                evidenceSource = suggestion.EvidenceSource,
                executionId = request.ExecutionId,
                itemIndex = request.PendingIndex,
                fingerprint,
                userId
            }, tx, cancellationToken: ct));
            if (!pendingId.HasValue)
            {
                var concurrent = await conn.QuerySingleOrDefaultAsync<PendingIdentityRow>(new CommandDefinition("""
select id as "Id", fingerprint as "Fingerprint"
from ged.protocolo_pendencia
where tenant_id=@tenantId and execution_id=@executionId and item_index=@itemIndex;
""", new { tenantId, executionId = request.ExecutionId, itemIndex = request.PendingIndex }, tx, cancellationToken: ct));
                if (concurrent is null || !string.Equals(concurrent.Fingerprint, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("A sugestão foi confirmada concorrentemente com outro conteúdo.");
                await tx.RollbackAsync(ct);
                return new ProtocolPendingConfirmResultDto
                {
                    Success = true,
                    AlreadyApplied = true,
                    PendingId = concurrent.Id,
                    Message = "Esta sugestão já foi confirmada como pendência."
                };
            }

            var userName = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? currentUser.Email ?? "Usuário";
            await WritePendingHistoryAsync(
                conn, tx, tenantId, request.ProtocoloId, pendingId.Value, "CONFIRMADA",
                "ABERTA", suggestion.Item, userId, userName,
                new { executionId = request.ExecutionId, itemIndex = request.PendingIndex }, ct,
                proofDocumentId: null);
            await tx.CommitAsync(ct);
            return new ProtocolPendingConfirmResultDto
            {
                Success = true,
                PendingId = pendingId,
                Message = "Sugestão confirmada por você e registrada na fila de pendências documentais."
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ProtocolAiApplyResultDto> ApplySubjectAsync(ProtocolAiApplySubjectRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");

        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("O assunto a ser aplicado não pode ser vazio.");

        await using var conn = await db.OpenAsync(ct);

        // 1. Checar acesso de visualização e operação no setor atual
        var canView = await protocolAccess.CanViewProtocolAsync(tenantId, request.ProtocoloId, userId, GetPrincipal(), ct);
        if (!canView)
            throw new UnauthorizedAccessException("Acesso não autorizado ao protocolo.");

        var canOperate = await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct);
        if (!canOperate)
            throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

        var execution = await LoadValidExecutionAsync(conn, tenantId, userId, request.ProtocoloId, request.ExecutionId, "SUGGEST_SUBJECT", ct);

        // 2. Checar concorrência e status de fechamento
        var proto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", assunto AS \"Assunto\", descricao AS \"Descricao\", status AS \"Status\", setor_atual_id AS \"SetorAtualId\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId });
        if (proto is null)
            throw new KeyNotFoundException("Protocolo não encontrado.");

        var closedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" };
        if (closedStatuses.Contains(proto.Status ?? string.Empty))
            throw new InvalidOperationException($"O protocolo está em status encerrado ({proto.Status}) e não pode ser alterado.");

        var currentToken = ToConcurrencyToken(proto.UpdatedAt, proto.CreatedAt);

        // 3. Idempotência e replay
        var fingerprint = DecisionFingerprint(request.ExecutionId, "SUGGEST_SUBJECT", request.Accepted, request.Subject, request.Description);
        var existing = await conn.QuerySingleOrDefaultAsync<RevisionRow>("SELECT id AS \"Id\", decision_fingerprint AS \"DecisionFingerprint\", applied_content_json::text AS \"AppliedContent\", concurrency_token AS \"ConcurrencyToken\" FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND task='SUGGEST_SUBJECT'", new { tenantId, executionId = request.ExecutionId });
        if (existing is not null)
        {
            if (existing.DecisionFingerprint == fingerprint)
                return CreateReplayResult(existing, "subject", "Esta revisão já foi registrada com a mesma decisão; nenhum efeito foi duplicado.");
            throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
        }

        if (request.ConcurrencyToken <= 0 || currentToken != request.ConcurrencyToken)
            throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");

        // 4. Transação atômica de gravação
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var locked = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", status AS \"Status\", setor_atual_id AS \"SetorAtualId\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A' FOR UPDATE", new { tenantId, protocoloId = request.ProtocoloId }, tx);
            if (locked is null)
                throw new KeyNotFoundException("Protocolo não encontrado.");
            var concurrentDecision = await LoadExistingDecisionAsync(conn, tx, tenantId, request.ExecutionId, "SUGGEST_SUBJECT", ct);
            if (concurrentDecision is not null)
            {
                if (concurrentDecision.DecisionFingerprint == fingerprint)
                {
                    await tx.RollbackAsync(ct);
                    return CreateReplayResult(concurrentDecision, "subject", "Esta revisão já foi registrada com a mesma decisão; nenhum efeito foi duplicado.");
                }
                throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
            }
            var lockedToken = ToConcurrencyToken(locked.UpdatedAt, locked.CreatedAt);
            if (lockedToken != request.ConcurrencyToken)
                throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");
            if (!await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct, tx))
                throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

            if (request.Accepted)
            {
                if (!string.IsNullOrWhiteSpace(request.Description))
                {
                    await conn.ExecuteAsync("UPDATE ged.protocolo SET assunto=@subject, descricao=@description, updated_at=now(), updated_by=@userId WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId, subject = request.Subject.Trim(), description = request.Description.Trim(), userId }, tx);
                }
                else
                {
                    await conn.ExecuteAsync("UPDATE ged.protocolo SET assunto=@subject, updated_at=now(), updated_by=@userId WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId, subject = request.Subject.Trim(), userId }, tx);
                }
            }

            var revisionId = Guid.NewGuid();
            var originalJson = execution.OriginalSuggestionJson;
            var appliedJson = JsonSerializer.Serialize(new { subject = request.Subject.Trim(), description = request.Description?.Trim(), accepted = request.Accepted });

            const string insertRevisionSql = """
INSERT INTO ged.protocolo_ai_revisao (
    id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type,
    decision_fingerprint, original_suggestion_json, applied_content_json,
    concurrency_token, notes, created_at
) VALUES (
    @revisionId, @tenantId, @protocoloId, @executionId, 'SUGGEST_SUBJECT', @userId,
    @decisionType, @decisionFingerprint, @originalJson::jsonb, @appliedJson::jsonb,
    @concurrencyToken, @notes, now()
)
""";
            await conn.ExecuteAsync(insertRevisionSql, new
            {
                revisionId,
                tenantId,
                protocoloId = request.ProtocoloId,
                executionId = request.ExecutionId,
                userId,
                decisionType = request.Accepted ? "ACCEPTED" : "REJECTED",
                decisionFingerprint = fingerprint,
                originalJson,
                appliedJson,
                concurrencyToken = currentToken,
                notes = request.Notes
            }, tx);

            await WriteProtocolAuditAsync(conn, tx, tenantId, userId, "AI_PROTOCOL_SUBJECT_APPLY", request.ProtocoloId, "Assunto do protocolo revisado com assistência de IA", new { executionId = request.ExecutionId, subject = request.Subject, accepted = request.Accepted }, ct);

            await tx.CommitAsync(ct);
            var updatedToken = await GetProtocolTokenAsync(conn, tenantId, request.ProtocoloId, ct);

            return new ProtocolAiApplyResultDto
            {
                Success = true,
                AlreadyApplied = false,
                RevisionId = revisionId,
                ConcurrencyToken = updatedToken,
                Message = request.Accepted ? "Assunto do protocolo atualizado com sucesso." : "Sugestão de assunto rejeitada pelo revisor.",
                AppliedContent = request.Subject
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ProtocolAiApplyResultDto> ApplyDispatchDraftAsync(ProtocolAiApplyDraftRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");

        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(request.DraftText))
            throw new ArgumentException("O texto do despacho não pode ser vazio.");

        await using var conn = await db.OpenAsync(ct);

        // 1. Checar acesso de visualização e operação no setor atual
        var canView = await protocolAccess.CanViewProtocolAsync(tenantId, request.ProtocoloId, userId, GetPrincipal(), ct);
        if (!canView)
            throw new UnauthorizedAccessException("Acesso não autorizado ao protocolo.");

        var canOperate = await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct);
        if (!canOperate)
            throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

        var execution = await LoadValidExecutionAsync(conn, tenantId, userId, request.ProtocoloId, request.ExecutionId, "PREPARE_DISPATCH_DRAFT", ct);

        // 2. Checar concorrência e setor atual
        var proto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", status AS \"Status\", setor_atual_id AS \"SetorAtualId\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId });
        if (proto is null)
            throw new KeyNotFoundException("Protocolo não encontrado.");

        var closedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" };
        if (closedStatuses.Contains(proto.Status ?? string.Empty))
            throw new InvalidOperationException($"O protocolo está em status encerrado ({proto.Status}) e não pode ser alterado.");

        var currentToken = ToConcurrencyToken(proto.UpdatedAt, proto.CreatedAt);

        // 3. Idempotência e replay
        var fingerprint = DecisionFingerprint(request.ExecutionId, "PREPARE_DISPATCH_DRAFT", request.Accepted, request.DraftText, null);
        var existing = await conn.QuerySingleOrDefaultAsync<RevisionRow>("SELECT id AS \"Id\", decision_fingerprint AS \"DecisionFingerprint\", applied_content_json::text AS \"AppliedContent\", concurrency_token AS \"ConcurrencyToken\" FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND task='PREPARE_DISPATCH_DRAFT'", new { tenantId, executionId = request.ExecutionId });
        if (existing is not null)
        {
            if (existing.DecisionFingerprint == fingerprint)
                return CreateReplayResult(existing, "draft", "Esta minuta já foi registrada com a mesma decisão; nenhum efeito foi duplicado.");
            throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
        }

        if (request.ConcurrencyToken <= 0 || currentToken != request.ConcurrencyToken)
            throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");

        // 4. Transação de gravação: minuta salva como rascunho de observação/despacho
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var locked = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", status AS \"Status\", setor_atual_id AS \"SetorAtualId\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A' FOR UPDATE", new { tenantId, protocoloId = request.ProtocoloId }, tx);
            if (locked is null)
                throw new KeyNotFoundException("Protocolo não encontrado.");
            var concurrentDecision = await LoadExistingDecisionAsync(conn, tx, tenantId, request.ExecutionId, "PREPARE_DISPATCH_DRAFT", ct);
            if (concurrentDecision is not null)
            {
                if (concurrentDecision.DecisionFingerprint == fingerprint)
                {
                    await tx.RollbackAsync(ct);
                    return CreateReplayResult(concurrentDecision, "draft", "Esta minuta já foi registrada com a mesma decisão; nenhum efeito foi duplicado.");
                }
                throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
            }
            var lockedToken = ToConcurrencyToken(locked.UpdatedAt, locked.CreatedAt);
            if (lockedToken != request.ConcurrencyToken)
                throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");
            if (!await CanOperateProtocolAsync(conn, tenantId, request.ProtocoloId, userId, ct, tx))
                throw new UnauthorizedAccessException("Usuário não possui vínculo ativo com o setor responsável pelo protocolo.");

            Guid? minutaId = null;
            var userName = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? currentUser.Email ?? "Usuário";
            if (request.Accepted)
            {
                if (!proto.SetorAtualId.HasValue)
                    throw new InvalidOperationException("O protocolo não possui setor atual para vincular a minuta.");

                minutaId = Guid.NewGuid();
                const string insertDraftSql = """
INSERT INTO ged.protocolo_minuta (
    id, tenant_id, protocolo_id, setor_id, titulo, conteudo, status, versao,
    origem_execucao_id, criado_por, criado_por_nome, atualizado_por, atualizado_por_nome,
    created_at, updated_at, reg_status
) VALUES (
    @minutaId, @tenantId, @protocoloId, @setorId, 'Minuta de despacho', @conteudo, 'RASCUNHO', 1,
    @executionId, @userId, @userName, @userId, @userName, now(), now(), 'A'
)
""";
                await conn.ExecuteAsync(insertDraftSql, new
                {
                    minutaId,
                    tenantId,
                    protocoloId = request.ProtocoloId,
                    setorId = proto.SetorAtualId,
                    conteudo = request.DraftText.Trim(),
                    executionId = request.ExecutionId,
                    userId,
                    userName
                }, tx);
                await WriteDraftHistoryAsync(
                    conn, tx, tenantId, request.ProtocoloId, minutaId.Value, 1,
                    "CRIADA_IA", "RASCUNHO", "Minuta de despacho", request.DraftText.Trim(),
                    userId, userName, new { executionId = request.ExecutionId }, ct);
            }

            var revisionId = Guid.NewGuid();
            var originalJson = execution.OriginalSuggestionJson;
            var appliedJson = JsonSerializer.Serialize(new { draft = request.DraftText.Trim(), accepted = request.Accepted, draftId = minutaId });

            const string insertRevisionSql = """
INSERT INTO ged.protocolo_ai_revisao (
    id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type,
    decision_fingerprint, original_suggestion_json, applied_content_json,
    concurrency_token, notes, created_at
) VALUES (
    @revisionId, @tenantId, @protocoloId, @executionId, 'PREPARE_DISPATCH_DRAFT', @userId,
    @decisionType, @decisionFingerprint, @originalJson::jsonb, @appliedJson::jsonb,
    @concurrencyToken, @notes, now()
)
""";
            await conn.ExecuteAsync(insertRevisionSql, new
            {
                revisionId,
                tenantId,
                protocoloId = request.ProtocoloId,
                executionId = request.ExecutionId,
                userId,
                decisionType = request.Accepted ? "ACCEPTED" : "REJECTED",
                decisionFingerprint = fingerprint,
                originalJson,
                appliedJson,
                concurrencyToken = currentToken,
                notes = request.Notes
            }, tx);

            await WriteProtocolAuditAsync(conn, tx, tenantId, userId, "AI_PROTOCOL_DRAFT_APPLY", request.ProtocoloId, "Minuta de despacho revisada com assistência de IA", new { executionId = request.ExecutionId, draftId = minutaId, draft = request.DraftText, accepted = request.Accepted }, ct);

            await tx.CommitAsync(ct);
            var updatedToken = await GetProtocolTokenAsync(conn, tenantId, request.ProtocoloId, ct);

            return new ProtocolAiApplyResultDto
            {
                Success = true,
                AlreadyApplied = false,
                RevisionId = revisionId,
                DraftId = minutaId,
                ConcurrencyToken = updatedToken,
                Message = request.Accepted ? "Minuta de despacho salva como rascunho com sucesso." : "Minuta de despacho descartada pelo revisor.",
                AppliedContent = request.DraftText
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ProtocolAiHistoryDto> GetReviewHistoryAsync(Guid protocoloId, int page, int pageSize, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");

        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var offset = (page - 1) * pageSize;

        await using var conn = await db.OpenAsync(ct);

        var canView = await protocolAccess.CanViewProtocolAsync(tenantId, protocoloId, userId, GetPrincipal(), ct);
        if (!canView)
            throw new UnauthorizedAccessException("Acesso não autorizado ao protocolo.");

        const string sqlCount = "SELECT count(*)::int FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND protocolo_id=@protocoloId";
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlCount, new { tenantId, protocoloId }, cancellationToken: ct));

        const string sqlRows = """
SELECT r.id AS "Id", r.protocolo_id AS "ProtocoloId", r.execution_id AS "ExecutionId",
       case r.task when 'SUGGEST_SUBJECT' then 'Sugestão de assunto' when 'PREPARE_DISPATCH_DRAFT' then 'Minuta de despacho' else r.task end AS "Task",
       case r.decision_type when 'ACCEPTED' then 'Aceita' when 'REJECTED' then 'Descartada' else r.decision_type end AS "DecisionType", u.name AS "ReviewerName",
       r.created_at AS "ReviewedAt",
       case
         when r.task='SUGGEST_SUBJECT' then coalesce(r.applied_content_json->>'subject', r.applied_content_json::text)
         when r.task='PREPARE_DISPATCH_DRAFT' then coalesce(r.applied_content_json->>'draft', r.applied_content_json::text)
         else r.applied_content_json::text
       end AS "AppliedContent",
       r.notes AS "Notes"
FROM ged.protocolo_ai_revisao r
LEFT JOIN ged.app_user u ON u.tenant_id=r.tenant_id AND u.id=r.reviewer_id
WHERE r.tenant_id=@tenantId AND r.protocolo_id=@protocoloId
ORDER BY r.created_at DESC
LIMIT @pageSize OFFSET @offset
""";
        var items = (await conn.QueryAsync<ProtocolAiRevisionRowDto>(new CommandDefinition(sqlRows, new { tenantId, protocoloId, pageSize, offset }, cancellationToken: ct))).ToList();

        return new ProtocolAiHistoryDto
        {
            Success = true,
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    private static JsonDocument BuildProtocolAssistSchema(ProtocolAiTaskKind taskKind)
    {
        var properties = new Dictionary<string, object>
        {
            ["limitations"] = new { type = "array", maxItems = 20, items = new { type = "string", minLength = 1, maxLength = 1000 } }
        };
        var required = new List<string> { "limitations" };

        if (IncludesTask(taskKind, ProtocolAiTaskKind.SummarizeProcess))
        {
            properties["summary"] = new { type = "string", minLength = 1, maxLength = 4000 };
            required.Add("summary");
        }
        if (IncludesTask(taskKind, ProtocolAiTaskKind.DocumentPending))
        {
            properties["pending"] = new
            {
                type = "array",
                maxItems = 20,
                items = new
                {
                    type = "object",
                    required = new[] { "item", "status", "evidence", "requiresHumanCheck" },
                    additionalProperties = false,
                    properties = new
                    {
                        item = new { type = "string", minLength = 1, maxLength = 500 },
                        status = new { type = "string", @enum = new[] { "CONFIRMADO", "CONFERENCIA_HUMANA" } },
                        evidence = new { type = "string", maxLength = 1000 },
                        requiresHumanCheck = new { type = "boolean" }
                    }
                }
            };
            required.Add("pending");
        }
        if (IncludesTask(taskKind, ProtocolAiTaskKind.SuggestSubject))
        {
            properties["suggestedSubject"] = new { type = "string", minLength = 1, maxLength = 500 };
            properties["suggestedDescription"] = new { type = "string", minLength = 1, maxLength = 4000 };
            required.Add("suggestedSubject");
            required.Add("suggestedDescription");
        }
        if (IncludesTask(taskKind, ProtocolAiTaskKind.PrepareDispatchDraft))
        {
            properties["dispatchDraft"] = new { type = "string", minLength = 1, maxLength = 12000 };
            required.Add("dispatchDraft");
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "object",
            required,
            additionalProperties = false,
            properties
        }));
    }

    private static bool TryReadProtocolAssist(JsonDocument? document, ProtocolAiTaskKind taskKind, out ParsedProtocolAssist parsed, out string error)
    {
        parsed = new ParsedProtocolAssist();
        error = "A IA não retornou JSON estruturado válido para o Protocolo.";
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            return false;

        var root = document.RootElement;
        if ((IncludesTask(taskKind, ProtocolAiTaskKind.SummarizeProcess) && !ReadRequiredString(root, "summary", 4000, out _)) ||
            (IncludesTask(taskKind, ProtocolAiTaskKind.SuggestSubject) &&
                (!ReadRequiredString(root, "suggestedSubject", 500, out _) || !ReadRequiredString(root, "suggestedDescription", 4000, out _))) ||
            (IncludesTask(taskKind, ProtocolAiTaskKind.PrepareDispatchDraft) && !ReadRequiredString(root, "dispatchDraft", 12000, out _)))
        {
            error = "A resposta da IA está incompleta ou excede os limites do contrato.";
            return false;
        }

        if (!ReadOptionalString(root, "summary", 4000, out var summary) ||
            !ReadOptionalString(root, "suggestedSubject", 500, out var suggestedSubject) ||
            !ReadOptionalString(root, "suggestedDescription", 4000, out var suggestedDescription) ||
            !ReadOptionalString(root, "dispatchDraft", 12000, out var dispatchDraft))
        {
            error = "A resposta da IA contém campos de modalidade inválidos.";
            return false;
        }
        parsed.Summary = summary;
        parsed.SuggestedSubject = suggestedSubject;
        parsed.SuggestedDescription = suggestedDescription;
        parsed.DispatchDraft = dispatchDraft;

        if (IncludesTask(taskKind, ProtocolAiTaskKind.DocumentPending))
        {
            if (!root.TryGetProperty("pending", out var pending) || pending.ValueKind != JsonValueKind.Array || pending.GetArrayLength() > 20)
            {
                error = "A lista de pendências da IA está ausente ou inválida.";
                return false;
            }

            foreach (var item in pending.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !ReadRequiredString(item, "item", 500, out var label) ||
                    !ReadRequiredString(item, "status", 64, out var status) ||
                    !item.TryGetProperty("requiresHumanCheck", out var human) ||
                    human.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !item.TryGetProperty("evidence", out var evidenceElement) ||
                    evidenceElement.ValueKind != JsonValueKind.String)
                {
                    error = "Uma pendência retornada pela IA não respeita o contrato.";
                    return false;
                }

                if (!status.Equals("CONFIRMADO", StringComparison.OrdinalIgnoreCase) &&
                    !status.Equals("CONFERENCIA_HUMANA", StringComparison.OrdinalIgnoreCase))
                {
                    error = "A IA retornou status de pendência não reconhecido.";
                    return false;
                }

                var evidence = evidenceElement.GetString();
                if (evidence?.Length > 1000)
                {
                    error = "Uma evidência retornada pela IA excede o limite permitido.";
                    return false;
                }

                parsed.Pending.Add(new ProtocolAiPendingItemDto
                {
                    Item = label,
                    Status = status.ToUpperInvariant(),
                    Evidence = evidence,
                    RequiresHumanCheck = human.GetBoolean()
                });
            }
        }

        if (!root.TryGetProperty("limitations", out var limitations) || limitations.ValueKind != JsonValueKind.Array || limitations.GetArrayLength() > 20)
        {
            error = "As limitações retornadas pela IA estão inválidas.";
            return false;
        }
        foreach (var limitation in limitations.EnumerateArray())
        {
            if (limitation.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(limitation.GetString()) ||
                limitation.GetString()!.Length > 1000)
            {
                error = "Uma limitação retornada pela IA não respeita o contrato.";
                return false;
            }
            parsed.Limitations.Add(limitation.GetString()!);
        }

        error = string.Empty;
        return true;
    }

    private static bool ReadRequiredString(JsonElement root, string property, int maxLength, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0 && value.Length <= maxLength;
    }

    private static bool ReadOptionalString(JsonElement root, string property, int maxLength, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var element))
            return true;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString()?.Trim() ?? string.Empty;
        return value.Length <= maxLength;
    }

    private async Task<ExecutionValidation> LoadValidExecutionAsync(
        System.Data.Common.DbConnection conn,
        Guid tenantId,
        Guid userId,
        Guid protocoloId,
        Guid executionId,
        string decisionTask,
        CancellationToken ct)
    {
        if (executionId == Guid.Empty)
            throw new UnauthorizedAccessException("Execução de IA inválida.");

        const string sql = """
SELECT id AS "ExecutionId", task AS "Task", state AS "State", result_expires_at AS "ResultExpiresAt",
       source_documents::text AS "SourcesJson", result_json::text AS "ResultJson",
       context_metadata::text AS "ContextMetadata"
FROM ged.ai_execution
WHERE tenant_id=@tenantId AND user_id=@userId AND id=@executionId
""";
        var execution = await conn.QuerySingleOrDefaultAsync<ExecutionRow>(new CommandDefinition(sql, new { tenantId, userId, executionId }, cancellationToken: ct));
        if (execution is null)
            throw new UnauthorizedAccessException("Execução de IA não encontrada para este usuário e tenant.");
        if (!execution.Task.Equals(AiTask.SupportProtocol.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Execução de IA pertence a outra tarefa.");
        if (!execution.State.Equals(AiExecutionState.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Execução de IA ainda não possui resultado concluído.");
        if (execution.ResultExpiresAt is null || execution.ResultExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Resultado de IA expirado; gere nova assistência antes de aplicar.");

        if (string.IsNullOrWhiteSpace(execution.ContextMetadata))
            throw new UnauthorizedAccessException("Execução sem vínculo institucional verificável.");
        var metadata = ReadExecutionMetadata(execution.ContextMetadata, protocoloId, decisionTask);
        var sources = ReadExecutionSources(execution.SourcesJson);
        if (!SameSources(sources, metadata.Sources))
            throw new UnauthorizedAccessException("Fontes persistidas da execução estão inconsistentes.");
        if (sources.Count > 0)
        {
            const string sourceSql = """
SELECT EXISTS (
SELECT 1
FROM ged.protocolo_documento_ged g
JOIN ged.document d ON d.tenant_id=g.tenant_id AND d.id=g.ged_document_id AND coalesce(d.reg_status,'A')='A'
JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.document_id=d.id
WHERE g.tenant_id=@tenantId AND g.protocolo_id=@protocoloId AND g.reg_status='A'
  AND g.ged_document_id=@documentId AND v.id=@versionId
  AND v.id=coalesce(d.current_version_id, (
      SELECT vx.id FROM ged.document_version vx
      WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id
      ORDER BY vx.version_number DESC LIMIT 1
  ))
)
""";
            foreach (var source in sources)
            {
                var linked = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sourceSql, new { tenantId, protocoloId, documentId = source.DocumentId, versionId = source.VersionId }, cancellationToken: ct));
                if (!linked)
                    throw new UnauthorizedAccessException("Execução de IA não corresponde aos documentos vinculados ao protocolo atual.");
                var canAccessDoc = await authorization.CanAccessDocumentAsync(tenantId, userId, source.DocumentId, "VIEW", new Dictionary<string, string>(), ct);
                if (!canAccessDoc)
                    throw new UnauthorizedAccessException("Acesso atual aos documentos da execução foi revogado.");
            }
        }

        var original = ExtractOriginalSuggestion(execution.ResultJson, decisionTask);
        if (original is null)
            throw new InvalidOperationException("Resultado persistido da IA está ausente ou malformado.");

        return new ExecutionValidation(JsonSerializer.Serialize(original));
    }

    private static IReadOnlyList<AiExecutionSource> ReadExecutionSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Fontes da execução estão ausentes.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Fontes da execução estão malformadas.");
            var result = new List<AiExecutionSource>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !TryReadGuidProperty(item, "documentId", "document_id", out var documentId) ||
                    !TryReadGuidProperty(item, "versionId", "version_id", out var versionId) ||
                    documentId == Guid.Empty || versionId == Guid.Empty)
                    throw new InvalidOperationException("Um vínculo de fonte da execução está incompleto ou inválido.");
                result.Add(new AiExecutionSource(documentId, versionId));
            }
            if (result.Distinct().Count() != result.Count)
                throw new InvalidOperationException("A execução contém vínculos de fonte duplicados.");
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Fontes da execução estão corrompidas.", ex);
        }
    }

    private static ExecutionMetadata ReadExecutionMetadata(string json, Guid protocoloId, string decisionTask)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("O contexto persistido da execução está malformado.");
            if (!TryReadGuidProperty(root, "protocolId", null, out var storedProtocolId) ||
                storedProtocolId != protocoloId)
                throw new UnauthorizedAccessException("Execução de IA pertence a outro protocolo.");
            if (!root.TryGetProperty("protocolNumber", out var protocolNumber) ||
                protocolNumber.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(protocolNumber.GetString()) ||
                !root.TryGetProperty("requestedTaskKind", out var taskElement) ||
                taskElement.ValueKind != JsonValueKind.String ||
                !TryParseTaskKind(taskElement.GetString(), out var taskKind))
                throw new InvalidOperationException("O contexto de protocolo da execução está incompleto.");

            var requiredTask = decisionTask switch
            {
                "SUGGEST_SUBJECT" => ProtocolAiTaskKind.SuggestSubject,
                "PREPARE_DISPATCH_DRAFT" => ProtocolAiTaskKind.PrepareDispatchDraft,
                "DOCUMENT_PENDING" => ProtocolAiTaskKind.DocumentPending,
                _ => throw new InvalidOperationException("Modalidade de decisão de IA não reconhecida.")
            };
            if (!IncludesTask(taskKind, requiredTask))
                throw new UnauthorizedAccessException("A execução não contém a modalidade solicitada.");

            if (!root.TryGetProperty("protocolState", out var state) ||
                state.ValueKind != JsonValueKind.Object ||
                !state.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String ||
                !state.TryGetProperty("sectorId", out var sector) ||
                !state.TryGetProperty("concurrencyToken", out var token) || !token.TryGetInt64(out var concurrencyToken) || concurrencyToken <= 0 ||
                !root.TryGetProperty("sources", out var sourcesElement) || sourcesElement.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("coverage", out var coverage) || coverage.ValueKind != JsonValueKind.Object ||
                !coverage.TryGetProperty("partial", out var partial) || partial.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !coverage.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array ||
                !coverage.TryGetProperty("totalSources", out var total) || !total.TryGetInt32(out var totalSources) || totalSources < 0 ||
                !coverage.TryGetProperty("processedSources", out var processed) || !processed.TryGetInt32(out var processedSources) || processedSources < 0 ||
                processedSources > totalSources)
                throw new InvalidOperationException("O contexto persistido da execução está incompleto ou malformado.");

            foreach (var note in notes.EnumerateArray())
            {
                if (note.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("A cobertura persistida da execução está malformada.");
            }
            Guid? sectorId = null;
            if (sector.ValueKind == JsonValueKind.String)
            {
                if (!Guid.TryParse(sector.GetString(), out var parsedSector) || parsedSector == Guid.Empty)
                    throw new InvalidOperationException("O setor persistido da execução está malformado.");
                sectorId = parsedSector;
            }
            else if (sector.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("O setor persistido da execução está malformado.");

            var sources = ReadExecutionSources(sourcesElement.GetRawText());
            return new ExecutionMetadata(taskKind, status.GetString()!, sectorId, concurrencyToken, sources);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("O contexto persistido da execução está corrompido.", ex);
        }
    }

    private static bool TryReadGuidProperty(JsonElement root, string property, string? legacyProperty, out Guid value)
    {
        value = Guid.Empty;
        JsonElement element;
        if (root.TryGetProperty(property, out element))
        {
        }
        else if (legacyProperty is null || !root.TryGetProperty(legacyProperty, out element))
        {
            return false;
        }
        return element.ValueKind == JsonValueKind.String &&
               Guid.TryParse(element.GetString(), out value) &&
               value != Guid.Empty;
    }

    private static bool TryParseTaskKind(string? value, out ProtocolAiTaskKind taskKind)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        taskKind = normalized switch
        {
            null or "" or "ALL" => ProtocolAiTaskKind.All,
            "SUMMARY" or "SUMMARIZEPROCESS" => ProtocolAiTaskKind.SummarizeProcess,
            "PENDING" or "DOCUMENTPENDING" => ProtocolAiTaskKind.DocumentPending,
            "SUBJECT" or "SUGGESTSUBJECT" => ProtocolAiTaskKind.SuggestSubject,
            "DRAFT" or "PREPAREDISPATCHDRAFT" => ProtocolAiTaskKind.PrepareDispatchDraft,
            _ => (ProtocolAiTaskKind)(-1)
        };
        return Enum.IsDefined(taskKind);
    }

    private static bool IncludesTask(ProtocolAiTaskKind selected, ProtocolAiTaskKind task) =>
        selected == ProtocolAiTaskKind.All || selected == task;

    private static bool SameSources(IReadOnlyList<AiExecutionSource> left, IReadOnlyList<AiExecutionSource> right) =>
        left.OrderBy(x => x.DocumentId).ThenBy(x => x.VersionId)
            .SequenceEqual(right.OrderBy(x => x.DocumentId).ThenBy(x => x.VersionId));

    private static bool SameDocumentSnapshot(IReadOnlyList<VinculoDocRow> before, IReadOnlyList<VinculoDocRow> after) =>
        before.OrderBy(x => x.DocumentId).ThenBy(x => x.VersionId)
            .Select(x => (x.DocumentId, x.VersionId, x.OcrText))
            .SequenceEqual(after.OrderBy(x => x.DocumentId).ThenBy(x => x.VersionId)
                .Select(x => (x.DocumentId, x.VersionId, x.OcrText)));

    private static string? FindEvidenceSource(
        string? evidence,
        IReadOnlyList<ProtocolAiSourceDto> authorizedSources,
        IReadOnlyList<VinculoDocRow> documents)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return null;

        var normalizedEvidence = NormalizeEvidence(evidence);
        if (normalizedEvidence.Length < 12)
            return null;

        foreach (var source in authorizedSources.Where(x => x.DocumentId.HasValue && x.HasOcr))
        {
            var document = documents.FirstOrDefault(x => x.DocumentId == source.DocumentId);
            if (document?.OcrText is null)
                continue;
            if (NormalizeEvidence(document.OcrText).Contains(normalizedEvidence, StringComparison.OrdinalIgnoreCase))
                return source.Title;
        }
        return null;
    }

    private static string NormalizeEvidence(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static object? ExtractOriginalSuggestion(string? resultJson, string decisionTask)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            return null;
        try
        {
            using var stored = JsonDocument.Parse(resultJson);
            JsonElement root = stored.RootElement;
            JsonDocument? structuredDocument = null;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (root.TryGetProperty("Structured", out var structured))
            {
                if (structured.ValueKind != JsonValueKind.String)
                    return null;
                var raw = structured.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                structuredDocument = JsonDocument.Parse(raw);
                root = structuredDocument.RootElement;
            }

            using (structuredDocument)
            {
                if (root.ValueKind != JsonValueKind.Object)
                    return null;
                return decisionTask switch
                {
                    "SUGGEST_SUBJECT" when ReadRequiredString(root, "suggestedSubject", 500, out var subject) &&
                                                    ReadRequiredString(root, "suggestedDescription", 4000, out var description) =>
                        new { subject, description },
                    "PREPARE_DISPATCH_DRAFT" when ReadRequiredString(root, "dispatchDraft", 12000, out var draft) =>
                        new { draft },
                    "DOCUMENT_PENDING" when root.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.Array =>
                        new { pending = pending.Clone() },
                    _ => null
                };
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string DecisionFingerprint(Guid executionId, string task, bool accepted, string content, string? description)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            executionId,
            task,
            accepted,
            content = content.Trim(),
            description = description?.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string PendingFingerprint(Guid executionId, int itemIndex, ProtocolAiPendingItemDto suggestion)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            executionId,
            itemIndex,
            item = suggestion.Item.Trim(),
            evidence = suggestion.Evidence?.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static ProtocolAiPendingItemDto ReadPendingSuggestion(string json, int itemIndex)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("pending", out var pending) ||
                pending.ValueKind != JsonValueKind.Array ||
                itemIndex >= pending.GetArrayLength())
                throw new InvalidOperationException("A sugestão de pendência não está disponível nesta execução.");
            var item = pending[itemIndex];
            if (item.ValueKind != JsonValueKind.Object ||
                !ReadRequiredString(item, "item", 500, out var label) ||
                !ReadRequiredString(item, "status", 64, out var status) ||
                !item.TryGetProperty("requiresHumanCheck", out var humanCheck) ||
                humanCheck.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !item.TryGetProperty("evidence", out var evidence) ||
                evidence.ValueKind != JsonValueKind.String ||
                evidence.GetString()?.Length > 1000)
                throw new InvalidOperationException("A sugestão de pendência armazenada está malformada.");
            if (!status.Equals("CONFIRMADO", StringComparison.OrdinalIgnoreCase) &&
                !status.Equals("CONFERENCIA_HUMANA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A sugestão de pendência possui status inválido.");

            return new ProtocolAiPendingItemDto
            {
                Item = label,
                Status = "CONFERENCIA_HUMANA",
                Evidence = evidence.GetString(),
                RequiresHumanCheck = true
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Sugestões de pendência armazenadas estão corrompidas.", ex);
        }
    }

    private static bool ProtocolStatusIsClosed(string? status) =>
        new[] { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" }
            .Contains(status ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string? ExtractAppliedText(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(property, out var value) ||
                value.ValueKind != JsonValueKind.String)
                return null;
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Guid? ExtractAppliedGuid(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(property, out var value) ||
                value.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(value.GetString(), out var id) ||
                id == Guid.Empty)
                return null;
            return id;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ProtocolAiApplyResultDto CreateReplayResult(RevisionRow revision, string property, string message)
    {
        var appliedContent = ExtractAppliedText(revision.AppliedContent, property);
        if (appliedContent is null)
            throw new InvalidOperationException("A decisão persistida está ausente ou corrompida.");
        return new ProtocolAiApplyResultDto
        {
            Success = true,
            AlreadyApplied = true,
            RevisionId = revision.Id,
            DraftId = ExtractAppliedGuid(revision.AppliedContent, "draftId"),
            ConcurrencyToken = revision.ConcurrencyToken,
            Message = message,
            AppliedContent = appliedContent
        };
    }

    private static Task<RevisionRow?> LoadExistingDecisionAsync(
        System.Data.Common.DbConnection conn,
        IDbTransaction transaction,
        Guid tenantId,
        Guid executionId,
        string task,
        CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<RevisionRow>(new CommandDefinition(
            "SELECT id AS \"Id\", decision_fingerprint AS \"DecisionFingerprint\", applied_content_json::text AS \"AppliedContent\", concurrency_token AS \"ConcurrencyToken\" FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND task=@task",
            new { tenantId, executionId, task },
            transaction,
            cancellationToken: ct));

    private static async Task<long> GetProtocolTokenAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid protocoloId, CancellationToken ct)
    {
        var row = await conn.QuerySingleAsync<ProtocolRow>(new CommandDefinition("SELECT created_at AS \"CreatedAt\", updated_at AS \"UpdatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId", new { tenantId, protocoloId }, cancellationToken: ct));
        return ToConcurrencyToken(row.UpdatedAt, row.CreatedAt);
    }

    private static long ToConcurrencyToken(DateTime? updatedAt, DateTime createdAt)
    {
        var timestamp = (DateTimeOffset)(updatedAt ?? createdAt);
        return timestamp.ToUnixTimeMilliseconds() * 1000 +
               timestamp.UtcTicks % TimeSpan.TicksPerMillisecond / 10;
    }

    private static async Task WriteDraftHistoryAsync(
        System.Data.Common.DbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid protocoloId,
        Guid minutaId,
        int versao,
        string evento,
        string status,
        string titulo,
        string conteudo,
        Guid userId,
        string userName,
        object details,
        CancellationToken ct)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.protocolo_minuta_historico (
    id, tenant_id, protocolo_id, minuta_id, versao, evento, status, titulo,
    conteudo, usuario_id, usuario_nome, detalhes, created_at
) VALUES (
    @id, @tenantId, @protocoloId, @minutaId, @versao, @evento, @status, @titulo,
    @conteudo, @userId, @userName, @detalhes::jsonb, now()
)
""", new
        {
            id = Guid.NewGuid(),
            tenantId,
            protocoloId,
            minutaId,
            versao,
            evento,
            status,
            titulo,
            conteudo,
            userId,
            userName,
            detalhes = JsonSerializer.Serialize(details)
        }, tx, cancellationToken: ct));
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar histórico transacional da minuta.");
    }

    private static async Task WritePendingHistoryAsync(
        System.Data.Common.DbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid protocoloId,
        Guid pendingId,
        string evento,
        string status,
        string description,
        Guid userId,
        string userName,
        object details,
        CancellationToken ct,
        Guid? assignedTo = null,
        string? resolution = null,
        Guid? proofDocumentId = null)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.protocolo_pendencia_historico (
    id, tenant_id, protocolo_id, pendencia_id, evento, status, descricao,
    atribuida_para, resolucao, comprovante_documento_id, usuario_id, usuario_nome,
    detalhes, created_at
) VALUES (
    @id, @tenantId, @protocoloId, @pendingId, @evento, @status, @description,
    @assignedTo, @resolution, @proofDocumentId, @userId, @userName, @details::jsonb, now()
)
""", new
        {
            id = Guid.NewGuid(),
            tenantId,
            protocoloId,
            pendingId,
            evento,
            status,
            description,
            assignedTo,
            resolution,
            proofDocumentId,
            userId,
            userName,
            details = JsonSerializer.Serialize(details)
        }, tx, cancellationToken: ct));
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar o histórico da pendência.");
    }

    private static async Task WriteProtocolAuditAsync(System.Data.Common.DbConnection conn, IDbTransaction tx, Guid tenantId, Guid userId, string action, Guid protocoloId, string message, object details, CancellationToken ct)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.app_audit_log (
    id, tenant_id, user_id, user_name, action, event_type, source, entity_name, entity_id,
    message, details, created_at, reg_status
) VALUES (
    gen_random_uuid(), @tenantId, @userId, @userName, @action, 'INFO', 'ProtocolAiAssistService',
    'PROTOCOLO', @entityId, @message, @details::jsonb, now(), 'A'
)
""", new
        {
            tenantId,
            userId,
            userName = userId.ToString(),
            action,
            entityId = protocoloId.ToString(),
            message,
            details = JsonSerializer.Serialize(details)
        }, tx, cancellationToken: ct));
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar auditoria transacional da decisão de IA.");
    }

    private sealed class ProtocolRow
    {
        public Guid Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public string Assunto { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Prioridade { get; set; }
        public Guid? SetorAtualId { get; set; }
        public string? SetorAtualNome { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class TramitacaoRow
    {
        public string Acao { get; set; } = string.Empty;
        public string? Despacho { get; set; }
        public string? Observacao { get; set; }
        public string? SetorOrigemNome { get; set; }
        public string? SetorDestinoNome { get; set; }
        public DateTime DataTramitacao { get; set; }
    }

    private sealed class VinculoDocRow
    {
        public Guid DocumentId { get; set; }
        public string? Title { get; set; }
        public bool IsConfidential { get; set; }
        public Guid? VersionId { get; set; }
        public int VersionNumber { get; set; }
        public string? FileName { get; set; }
        public string? OcrText { get; set; }
    }

    private sealed class RevisionRow
    {
        public Guid Id { get; set; }
        public string DecisionFingerprint { get; set; } = string.Empty;
        public string? AppliedContent { get; set; }
        public long ConcurrencyToken { get; set; }
    }

    private sealed class PendingIdentityRow
    {
        public Guid Id { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
    }

    private sealed class ExecutionRow
    {
        public Guid ExecutionId { get; set; }
        public string Task { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public DateTimeOffset? ResultExpiresAt { get; set; }
        public string? SourcesJson { get; set; }
        public string? ResultJson { get; set; }
        public string? ContextMetadata { get; set; }
    }

    private sealed record ExecutionValidation(string OriginalSuggestionJson);
    private sealed record ExecutionMetadata(
        ProtocolAiTaskKind TaskKind,
        string ProtocolStatus,
        Guid? SectorId,
        long ConcurrencyToken,
        IReadOnlyList<AiExecutionSource> Sources);

    private sealed class ParsedProtocolAssist
    {
        public string Summary { get; set; } = string.Empty;
        public string SuggestedSubject { get; set; } = string.Empty;
        public string SuggestedDescription { get; set; } = string.Empty;
        public string DispatchDraft { get; set; } = string.Empty;
        public List<ProtocolAiPendingItemDto> Pending { get; } = [];
        public List<string> Limitations { get; } = [];
    }
}
