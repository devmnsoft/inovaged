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

    private async Task<bool> CanOperateProtocolAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid protocolId, Guid userId, CancellationToken ct)
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
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, protocolId, userId }, cancellationToken: ct));
    }

    public async Task<ProtocolAiAssistResultDto> AssistAsync(ProtocolAiAssistRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuário não autenticado.");

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

        var concurrencyToken = ((DateTimeOffset)(proto.UpdatedAt ?? proto.CreatedAt)).ToUnixTimeMilliseconds();

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
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
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

        contextItems.Add(new AiContextItem($"PROCESSO-{proto.Numero}", sbProtoContext.ToString()));
        sourcesList.Add(new ProtocolAiSourceDto
        {
            Title = $"Protocolo {proto.Numero}",
            SourceType = "PROTOCOLO",
            HasOcr = true,
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
                coverageNotes.Add($"Documento '{doc.Title}' sem texto OCR disponível; analisado por metadados.");
            }
        }

        // 5. Chamar Gateway Governado com AiTask.SupportProtocol
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? Guid.NewGuid().ToString() : request.IdempotencyKey;
        var aiSources = sourcesList.Where(s => s.DocumentId.HasValue).Select(s => new AiExecutionSource(s.DocumentId!.Value, s.VersionId ?? Guid.Empty)).ToList();

        var aiRequest = new AiRequest(
            tenantId,
            userId,
            AiTask.SupportProtocol,
            "Você é o assistente institucional do Protocolo InovaGED. Analise o processo, seu histórico e documentos vinculados. " +
            "Produza: 1. Resumo consultivo; 2. Pendências sustentadas em evidências; 3. Sugestão de assunto conciso; 4. Minuta de despacho fundamentada. " +
            "Não invente normas, prazos ou exigências inexistentes. Não execute nenhuma tramitação automaticamente.",
            contextItems,
            null,
            idempotencyKey,
            Sources: aiSources
        );

        var aiResult = await gateway.ExecuteAsync(aiRequest, ct);
        if (!aiResult.Success)
        {
            return new ProtocolAiAssistResultDto
            {
                Success = false,
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

        // 6. Interpretar saída estruturada
        string summaryText = "Processo institucional em tramitação.";
        string suggestedSubject = proto.Assunto;
        string suggestedDescription = !string.IsNullOrWhiteSpace(proto.Descricao) ? proto.Descricao : "Análise processual com base nas peças instrutórias.";
        string dispatchDraft = "Encaminho os presentes autos para manifestação da área técnica.";
        var pendingItems = new List<ProtocolAiPendingItemDto>();
        var limitations = new List<string> { "Decisões de tramitação, despacho e assunto exigem revisão humana." };

        if (aiResult.StructuredData is not null)
        {
            var root = aiResult.StructuredData.RootElement;
            if (root.TryGetProperty("summary", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                summaryText = sProp.GetString() ?? summaryText;
            if (root.TryGetProperty("suggestedSubject", out var subProp) && subProp.ValueKind == JsonValueKind.String)
                suggestedSubject = subProp.GetString() ?? suggestedSubject;
            if (root.TryGetProperty("suggestedDescription", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                suggestedDescription = descProp.GetString() ?? suggestedDescription;
            if (root.TryGetProperty("dispatchDraft", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                dispatchDraft = dProp.GetString() ?? dispatchDraft;
            if (root.TryGetProperty("limitations", out var limProp) && limProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in limProp.EnumerateArray())
                    if (l.ValueKind == JsonValueKind.String) limitations.Add(l.GetString()!);
            }
            if (root.TryGetProperty("pending", out var penProp) && penProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in penProp.EnumerateArray())
                {
                    var item = p.TryGetProperty("item", out var ip) ? ip.GetString() : "Item pendente";
                    var status = p.TryGetProperty("status", out var stp) ? stp.GetString() : "CONFERENCIA_HUMANA";
                    var ev = p.TryGetProperty("evidence", out var ep) ? ep.GetString() : null;
                    var reqCheck = p.TryGetProperty("requiresHumanCheck", out var rcp) && rcp.GetBoolean();
                    pendingItems.Add(new ProtocolAiPendingItemDto
                    {
                        Item = item ?? "Item",
                        Status = status ?? "CONFERENCIA_HUMANA",
                        Evidence = ev,
                        RequiresHumanCheck = reqCheck
                    });
                }
            }
        }

        var executionId = aiResult.ExecutionId ?? Guid.NewGuid();
        var execExists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM ged.ai_execution WHERE tenant_id=@tenantId AND id=@executionId)",
            new { tenantId, executionId }, cancellationToken: ct));

        if (!execExists)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{userId}:SupportProtocol:{idempotencyKey}"))).ToLowerInvariant()[..64];
            var sourcesJson = JsonSerializer.Serialize(aiSources.Select(s => new { document_id = s.DocumentId, version_id = s.VersionId }));
            await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.ai_execution (
    id, tenant_id, user_id, task, provider, model, idempotency_key, input_fingerprint,
    policy_revision, state, reservation_period, document_refs, source_documents, expires_at, created_at
) VALUES (
    @executionId, @tenantId, @userId, 'SupportProtocol', 'Deterministic', 'deterministic-v1',
    @idempotencyKey, @fingerprint, 1, 'Completed', date_trunc('month', now())::date,
    '[]'::jsonb, cast(@sourcesJson as jsonb),
    now() + interval '1 day', now()
) ON CONFLICT DO NOTHING
""", new { executionId, tenantId, userId, idempotencyKey, fingerprint, sourcesJson }, cancellationToken: ct));
        }

        return new ProtocolAiAssistResultDto
        {
            Success = true,
            ExecutionId = executionId,
            State = "Completed",
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

        // 2. Checar concorrência e status de fechamento
        var proto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", assunto AS \"Assunto\", descricao AS \"Descricao\", status AS \"Status\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId });
        if (proto is null)
            throw new KeyNotFoundException("Protocolo não encontrado.");

        var closedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" };
        if (closedStatuses.Contains(proto.Status ?? string.Empty))
            throw new InvalidOperationException($"O protocolo está em status encerrado ({proto.Status}) e não pode ser alterado.");

        var currentToken = ((DateTimeOffset)(proto.UpdatedAt ?? proto.CreatedAt)).ToUnixTimeMilliseconds();

        // 3. Idempotência e replay
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.ExecutionId}:{request.Subject.Trim()}:{request.Accepted}"))).ToLowerInvariant();
        var existing = await conn.QuerySingleOrDefaultAsync<RevisionRow>("SELECT id AS \"Id\", decision_fingerprint AS \"DecisionFingerprint\" FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND task='SUGGEST_SUBJECT'", new { tenantId, executionId = request.ExecutionId });
        if (existing is not null)
        {
            if (existing.DecisionFingerprint == fingerprint)
            {
                return new ProtocolAiApplyResultDto
                {
                    Success = true,
                    AlreadyApplied = true,
                    RevisionId = existing.Id,
                    ConcurrencyToken = currentToken,
                    Message = "Esta revisão já foi registrada com a mesma decisão; nenhum efeito foi duplicado.",
                    AppliedContent = request.Subject
                };
            }
            throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
        }

        if (request.ConcurrencyToken > 0 && Math.Abs(currentToken - request.ConcurrencyToken) > 1000)
            throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");

        // 4. Transação atômica de gravação
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
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
            var appliedJson = JsonSerializer.Serialize(new { subject = request.Subject.Trim(), description = request.Description?.Trim(), accepted = request.Accepted });

            const string insertRevisionSql = """
INSERT INTO ged.protocolo_ai_revisao (
    id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type,
    decision_fingerprint, original_suggestion_json, applied_content_json,
    concurrency_token, notes, created_at
) VALUES (
    @revisionId, @tenantId, @protocoloId, @executionId, 'SUGGEST_SUBJECT', @userId,
    @decisionType, @decisionFingerprint, @appliedJson::jsonb, @appliedJson::jsonb,
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
                appliedJson,
                concurrencyToken = currentToken,
                notes = request.Notes
            }, tx);

            await audit.WriteAsync(tenantId, userId, "AI_PROTOCOL_SUBJECT_APPLY", "PROTOCOLO", request.ProtocoloId, "Assunto do protocolo revisado com assistência de IA", null, null, new { executionId = request.ExecutionId, subject = request.Subject, accepted = request.Accepted }, ct);

            await tx.CommitAsync(ct);

            return new ProtocolAiApplyResultDto
            {
                Success = true,
                AlreadyApplied = false,
                RevisionId = revisionId,
                ConcurrencyToken = currentToken,
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

        // 2. Checar concorrência e setor atual
        var proto = await conn.QuerySingleOrDefaultAsync<ProtocolRow>("SELECT id AS \"Id\", status AS \"Status\", setor_atual_id AS \"SetorAtualId\", updated_at AS \"UpdatedAt\", created_at AS \"CreatedAt\" FROM ged.protocolo WHERE tenant_id=@tenantId AND id=@protocoloId AND reg_status='A'", new { tenantId, protocoloId = request.ProtocoloId });
        if (proto is null)
            throw new KeyNotFoundException("Protocolo não encontrado.");

        var closedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" };
        if (closedStatuses.Contains(proto.Status ?? string.Empty))
            throw new InvalidOperationException($"O protocolo está em status encerrado ({proto.Status}) e não pode ser alterado.");

        var currentToken = ((DateTimeOffset)(proto.UpdatedAt ?? proto.CreatedAt)).ToUnixTimeMilliseconds();

        // 3. Idempotência e replay
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.ExecutionId}:{request.DraftText.Trim()}:{request.Accepted}"))).ToLowerInvariant();
        var existing = await conn.QuerySingleOrDefaultAsync<RevisionRow>("SELECT id AS \"Id\", decision_fingerprint AS \"DecisionFingerprint\" FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND task='PREPARE_DISPATCH_DRAFT'", new { tenantId, executionId = request.ExecutionId });
        if (existing is not null)
        {
            if (existing.DecisionFingerprint == fingerprint)
            {
                return new ProtocolAiApplyResultDto
                {
                    Success = true,
                    AlreadyApplied = true,
                    RevisionId = existing.Id,
                    ConcurrencyToken = currentToken,
                    Message = "Esta minuta já foi registrada com a mesma decisão; nenhum efeito foi duplicado.",
                    AppliedContent = request.DraftText
                };
            }
            throw new InvalidOperationException("Conflito: esta execução já possui outra decisão humana registrada.");
        }

        var hasRelatedRevision = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM ged.protocolo_ai_revisao WHERE tenant_id=@tenantId AND execution_id=@executionId AND protocolo_id=@protocoloId)",
            new { tenantId, executionId = request.ExecutionId, protocoloId = request.ProtocoloId });

        if (!hasRelatedRevision && request.ConcurrencyToken > 0 && Math.Abs(currentToken - request.ConcurrencyToken) > 1000)
            throw new InvalidOperationException("Conflito de concorrência: o protocolo foi alterado por outro usuário.");

        // 4. Transação de gravação: minuta salva como rascunho de observação/despacho
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (request.Accepted)
            {
                // Salvar como anotação/rascunho de despacho do setor atual
                var setorNome = await conn.ExecuteScalarAsync<string>("SELECT nome FROM ged.protocolo_setor WHERE tenant_id=@tenantId AND id=@setorId", new { tenantId, setorId = proto.SetorAtualId }, tx) ?? "Setor Atual";
                const string sqlObs = """
INSERT INTO ged.protocolo_observacao (
    tenant_id, protocolo_id, setor_id, setor_nome, usuario_id, usuario_nome, tipo, observacao, created_at, reg_status
) VALUES (
    @tenantId, @protocoloId, @setorId, @setorNome, @userId, @userName, 'DESPACHO', @obs, now(), 'A'
)
""";
                await conn.ExecuteAsync(sqlObs, new
                {
                    tenantId,
                    protocoloId = request.ProtocoloId,
                    setorId = proto.SetorAtualId,
                    setorNome,
                    userId,
                    userName = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? currentUser.Email ?? "Usuário",
                    obs = $"[MINUTA DE DESPACHO REVISADA]\n{request.DraftText.Trim()}"
                }, tx);
            }

            var revisionId = Guid.NewGuid();
            var appliedJson = JsonSerializer.Serialize(new { draft = request.DraftText.Trim(), accepted = request.Accepted });

            const string insertRevisionSql = """
INSERT INTO ged.protocolo_ai_revisao (
    id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type,
    decision_fingerprint, original_suggestion_json, applied_content_json,
    concurrency_token, notes, created_at
) VALUES (
    @revisionId, @tenantId, @protocoloId, @executionId, 'PREPARE_DISPATCH_DRAFT', @userId,
    @decisionType, @decisionFingerprint, @appliedJson::jsonb, @appliedJson::jsonb,
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
                appliedJson,
                concurrencyToken = currentToken,
                notes = request.Notes
            }, tx);

            await audit.WriteAsync(tenantId, userId, "AI_PROTOCOL_DRAFT_APPLY", "PROTOCOLO", request.ProtocoloId, "Minuta de despacho revisada com assistência de IA", null, null, new { executionId = request.ExecutionId, draft = request.DraftText, accepted = request.Accepted }, ct);

            await tx.CommitAsync(ct);

            return new ProtocolAiApplyResultDto
            {
                Success = true,
                AlreadyApplied = false,
                RevisionId = revisionId,
                ConcurrencyToken = currentToken,
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
       r.task AS "Task", r.decision_type AS "DecisionType", u.name AS "ReviewerName",
       r.created_at AS "ReviewedAt", r.applied_content_json::text AS "AppliedContent", r.notes AS "Notes"
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
    }
}
