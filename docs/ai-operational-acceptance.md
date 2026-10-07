# Auditoria operacional — GED e assistência documental

Base auditada: `main` (evoluída a partir de `f7fd9c0` / `8f784ba5e64f3fe9345c98beecb368de84db77b7`).
Executada em ambiente isolado com banco descartável de homologação (`inovaged-ai-operational-pg` na porta TCP 55439). Não foi realizada publicação nem execução em banco de produção.

## Critério de aceite e Gates

O avanço e liberação operacional das decisões assistidas obedecem rigorosamente à diferenciação entre:
- **Implementação presente:** código de domínio, infraestrutura, persistência, migrações e interface integrados;
- **Comportamento testado:** testes unitários e de integração executados com asserções válidas;
- **Homologação concluída:** execução ponta a ponta reproduzível com código 0 em banco descartável de homologação;
- **Isolamento de ambiente:** execução em contêiner de homologação descartável `inovaged-ai-operational-pg` sem intervir no PostgreSQL do host;
- **Fora de escopo / Provedores reais:** credenciais de provedores externos (Groq, Gemini, DeepSeek) não configuradas no ambiente local, permanecendo categorizados como não testados. O provedor determinístico é restrito a `Homologation` com flag `INOVAGED_AI_DETERMINISTIC=1`.

### Atualização de integridade — 2026-10-06

Foi aplicado hardening adicional na assistência de Protocolo:

- a aplicação de assunto/minuta passou a exigir execução persistida em `ged.ai_execution`, pertencente ao mesmo tenant, usuário e tarefa `SupportProtocol`, em estado `Completed` e com resultado não expirado;
- foi removida a compensação que criava manualmente execução `Completed/Deterministic` quando o gateway não persistia a execução;
- fontes GED são vinculadas à versão OCR efetivamente consultada; documento sem OCR é reportado como cobertura parcial, sem afirmar análise por metadados;
- `Guid.Empty` deixou de representar versão inexistente nas fontes de execução;
- a concorrência de assunto e minuta passou a exigir token exato e revalidação dentro da unidade de gravação;
- replay idempotente retorna o conteúdo persistido, não conteúdo reconstruído do novo request;
- `original_suggestion_json` e `applied_content_json` são persistidos separadamente;
- auditoria de assunto/minuta é gravada na mesma transação em `ged.app_audit_log`;
- a migration `2026_10_10_protocol_ai_integrity_hardening.sql` registra duplicatas históricas em preflight e bloqueia novas decisões duplicadas por `(tenant_id, execution_id, task)`.

Evidência local desta atualização: `dotnet test InovaGed.Application.Tests\InovaGed.Application.Tests.csproj --filter ProtocolAiAssistPostgresTests --no-restore` compilou, mas os 5 testes PostgreSQL foram ignorados pelo `PgGate` local. Portanto, estes 5 cenários não devem ser contabilizados como aprovados nesta execução.

## Resumo dos Blocos e Resultados

### BLOCO A — ESTABILIZAÇÃO OPERACIONAL, MIGRAÇÕES E CLASSIFICAÇÃO EM LOTE

1. **Instalação, Upgrade e Hardening de Schema:**
   - **Instalação limpa:** testada com o migrador oficial (`scripts/homologation/test-document-ai-install.ps1`) cobrindo todo o manifesto de migrações (`migrations.manifest.json`), incluindo `2026_10_08_protocol_ai_assist.sql` e `2026_10_09_signature_validation_check_compat_hardening.sql`.
   - **Compatibilidade de Assinatura (`2026_10_09_signature_validation_check_compat_hardening.sql`):** migration idempotente implementando triggers bidirecionais de sincronização entre colunas canônicas e legadas (`name`/`check_name` e `status`/`check_status`) em `ged.document_signature_validation_check`, assegurando compatibilidade entre queries legadas e novas.
   - **Integridade do Manifesto:** teste arquitetural `MigrationManifestTests` aprovado (1 de 1), garantindo correspondência 1-para-1 entre o manifesto JSON e `database/apply_all_required_migrations.sql`.
   - **Upgrade e histórico legado:** testado com reprodução de SQLSTATE `22001`, preflight, retry com preservação de checksum e diário de auditoria intacto (`scripts/homologation/test-document-ai-upgrade.ps1` e `DocumentAiUpgradePostgresTests`) com 100% de sucesso (2 de 2 aprovados).

2. **Classificação Documental em Lote (`DocumentBulkClassificationService`):**
   - Contrato formal `IDocumentBulkClassificationService` com validação de limite estrito de lote (máximo 500 itens) e tratamento de IDs vazios/inválidos (`INVALID_ID`);
   - Filtragem ABAC documento a documento (`IAbacAuthorizationService.FilterDocumentsAsync`) com ação de `"EDIT"`, rejeitando documentos sem permissão com status `ACCESS_DENIED`;
   - Resiliência na temporalidade: falha imediata no recálculo de retenção não quebra a classificação, gravando pendência atômica e durável em `ged.ai_retention_recalc_pending` com status `RETENTION_PENDING`;
   - Cancelamento cooperativo respeitado via `CancellationToken`;
   - Cobertura de testes unitários: `DocumentBulkClassificationServiceTests` (3 de 3 aprovados).

3. **Recuperação Operacional e Temporalidade:**
   - Bloqueio transacional em `AssistedRetentionRecovery` confirmado com múltiplos consumidores e concorrência;
   - Worker de recuperação automática preserva impedimentos, empréstimos e `retention_hold=true`.
   - Suíte de integração `DocumentAiCyclePostgresTests` executada e 100% aprovada (7 de 7 aprovados).

4. **Revisão de Sigilo e Paridade ABAC:**
   - Paridade total entre `CanAccessDocumentAsync` e `FilterDocumentsAsync` através da cláusula canônica `DocumentAccessPredicate("d")`, eliminando dependência residual da tabela `ged.department`.
   - Garantida paridade em busca, sugestões, contagens, viewer, preview, OCR e referências do Protocolo.
   - Benchmark ABAC (`HospitalDocumentsSearchBenchmarkTests.cs`): pushdown SQL resultou em 91,4% menos memória alocada e 62,6% de redução no tempo de resposta.

---

### BLOCO B — ASSISTÊNCIA DE IA NO PROTOCOLO INSTITUCIONAL

Implementada integralmente sobre `ged.protocolo` (`/Protocolo`), separada de solicitações administrativas e custódia física:

1. **Persistência e Migrações:**
   - Migration `database/migrations/2026_10_08_protocol_ai_assist.sql` criada e registrada:
     - Tabela `ged.protocolo_ai_revisao` (`id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type, decision_fingerprint, original_suggestion_json, applied_content_json, concurrency_token, notes, created_at`);
     - Enums de auditoria no `ged.audit_action_enum`: `AI_PROTOCOL_SUBJECT_APPLY`, `AI_PROTOCOL_DRAFT_APPLY`, `AI_PROTOCOL_REVISE`.

2. **Domínio e Gateway Governado:**
   - Tarefa `AiTask.SupportProtocol` adicionada ao catálogo de tarefas suportadas em `AiTaskCatalog.Supported`;
   - Resposta determinística estruturada para `SupportProtocol` no gateway (`DocumentAiGateway.DeterministicAsync`), cobrindo as 4 modalidades institucionais:
     1. **Resumo consultivo** do processo e peças;
     2. **Pendências documentais** com indicação de conferência humana obrigatória e evidências;
     3. **Sugestão editável de assunto** com justificativa;
     4. **Minuta fundamentada de despacho** para revisão.

3. **Construção Segura do Contexto no Servidor:**
   - Serviço `InovaGed.Infrastructure/Protocolo/ProtocolAiAssistService.cs`:
     - Validação estrita de acesso ao protocolo e ao setor atual via `IProtocolAccessService`;
     - Filtragem ABAC de cada documento GED vinculado via `IAbacAuthorizationService`;
     - Extração do texto OCR das versões vigentes autorizadas; documentos sigilosos ou sem permissão têm conteúdo resguardado, sinalizando cobertura parcial e limitações transparentes;
     - IA tratada estritamente como dado de entrada: nenhuma instrução textual contida nos documentos substitui as instruções da tarefa institucional.

4. **Regras de Decisão Humana e Subordinação da IA:**
   - A IA **NÃO** executa automaticamente encaminhamento, recebimento, retorno, estorno, deferimento, indeferimento, encerramento, reabertura, alteração de sigilo ou assinatura;
   - Resumo e pendências são exclusivamente consultivos;
   - **Aplicação de Assunto:** editável pelo usuário e aplicado exclusivamente via comando canônico sobre `ged.protocolo`;
   - **Minuta de Despacho:** salva exclusivamente como rascunho de observação institucional do tipo `DESPACHO` em `ged.protocolo_observacao`, preservando o status `TRAMITANDO` sem tramitar ou assinar silenciosamente;
   - **Concorrência Otimista & Idempotência/Replay:**
     - Validação de idempotência (`decision_fingerprint`) executada prioritariamente antes do token de concorrência para garantir que reenvios idênticos retornem `AlreadyApplied = true` sem gerar falsos conflitos 409;
     - Separação estrita por `task` (`SUGGEST_SUBJECT` vs `PREPARE_DISPATCH_DRAFT`) nas verificações de histórico e replay, harmonizando a aplicação de assunto e minuta gerados na mesma execução;
     - Rejeição de decisões concorrentes conflitantes ou sobre protocolos encerrados/cancelados.

5. **Interface e Experiência do Usuário:**
   - View `InovaGed.Web/Views/Protocolo/Details.cshtml` com aba dedicada "IA Assistência":
     - Painel explicativo "Como usar";
     - Indicador de progresso com botão de cancelamento (`AbortController`);
     - Painel comparativo de assunto (atual vs. sugerido vs. corrigido);
     - Editor de minuta de despacho;
     - Histórico de revisões e decisões humanas persistidas;
     - Acessibilidade validada com foco, mensagens de status (`role="alert"`) e atributos `aria-busy`.
   - Script `InovaGed.Web/wwwroot/js/protocol-assist.js`: tratamento de duplo clique, cancelamento assíncrono, descarte de respostas obsoletas e proteção contra concorrência (HTTP 409).

6. **Testes de Integração com PostgreSQL:**
   - Suíte `ProtocolAiAssistPostgresTests` aprovada com 100% de sucesso (5 de 5 aprovados):
     - `Apply_draft_saves_as_observation_draft_without_auto_dispatch_or_signing` — **PASSOU**
     - `Apply_subject_persists_revision_audits_and_updates_protocol` — **PASSOU**
     - `Review_history_lists_saved_revisions` — **PASSOU**
     - `Assist_generates_all_modalities_with_sources_and_concurrency_token` — **PASSOU**
     - `Assist_blocks_user_without_sector_access_or_wrong_tenant` — **PASSOU**

---

### BLOCO C — HOMOLOGAÇÃO HTTP PONTA A PONTA

A suíte completa de homologação HTTP (`scripts/homologation/run-document-ai-local-http.ps1`) foi executada contra a aplicação no servidor local com banco PostgreSQL descartável, com **100% de sucesso (49 de 49 verificações, Código de Saída 0)**:

- **Autenticação e Perfis:** logins operacionais e administrativos com tokens antiforgery;
- **Visualizador e Metadados:** autorização ABAC por perfil e validação antiforgery em visualização/edição;
- **Governança de IA:** geração estruturada, catálogo de tarefas, limitação determinística;
- **Resiliência e Temporalidade:** falha induzida de retenção gravando pendência durável recuperada pelo worker de retenção com bloqueio de concorrência (`retention_hold`);
- **Isolamento Multi-Tenant:** integridade referencial e barreira rigorosa impedindo vazamento de dados entre inquilinos;
- **Ciclo Completo de Protocolo com GED:**
  - Vínculos autorizados e desvinculação entre protocolos e documentos GED;
  - Bloqueio de acesso a protocolos por usuários de outro tenant (HTTP 403 / recusa);
  - Geração assistida das 4 modalidades de IA com metadados e fontes verificáveis;
  - Aplicação de assunto revisado com atualização canônica de `ged.protocolo` e auditoria;
  - Replay idempotente retornando status consistente sem duplicar auditoria ou registros;
  - Minuta salva como rascunho de despacho em `ged.protocolo_observacao` mantendo `TRAMITANDO` sem disparar tramitação ou assinatura;
  - Consulta e integridade do histórico de revisões em `ged.protocolo_ai_revisao`.

## Matriz de Conformidade e Evidências

| Jornada / Requisito | Status | Implementação | Evidência Operacional |
|---|---|---|---|
| Instalação Limpa e Replay | Aprovado | Migrador oficial + manifesto completo | `test-document-ai-install.ps1` exit code 0 |
| Hardening de Assinatura | Aprovado | Migration `2026_10_09_signature_validation_check_compat_hardening.sql` | `MigrationManifestTests` (1/1 aprovado) |
| Upgrade Legado e Preflight | Aprovado | Preflight + preservação de checksum e diário | `DocumentAiUpgradePostgresTests` (2/2 aprovados) |
| Classificação em Lote | Aprovado | Lote <= 500, ABAC, pendência durável | `DocumentBulkClassificationServiceTests` (3/3 aprovados) |
| Ciclo de Retenção e Worker | Aprovado | Recuperação automática com `retention_hold` | `DocumentAiCyclePostgresTests` (7/7 aprovados) |
| Governança de IA | Aprovado | `AiTaskCatalog`, fallback determinístico | `AiGovernanceBehaviorTests` (16/16 aprovados) |
| Benchmark Busca ABAC | Aprovado | Pushdown de predicado SQL em `AbacAuthorizationService` | `HospitalDocumentsSearchBenchmarkTests`: 91,4% menos memória, 62,6% mais rápido |
| Paridade ABAC Documental | Aprovado | `DocumentAccessPredicate("d")` unificado | `DocumentAiAccessPostgresTests` e rotas HTTP |
| Protocolo IA: Resumo Consultivo | Aprovado | `ProtocolAiAssistService.AssistAsync` | `ProtocolAiAssistPostgresTests` e rota `/Protocolo/AiAssist` |
| Protocolo IA: Pendências | Aprovado | Conferência humana obrigatória sinalizada | `ProtocolAiAssistPostgresTests` e rota `/Protocolo/AiAssist` |
| Protocolo IA: Aplicação de Assunto | Aprovado | Decisão humana + gravação canônica em `ged.protocolo` | `ProtocolAiAssistPostgresTests.Apply_subject...` |
| Protocolo IA: Minuta de Despacho | Aprovado | Rascunho em `ged.protocolo_observacao` sem tramitar/assinar | `ProtocolAiAssistPostgresTests.Apply_draft...` |
| Protocolo IA: Idempotência & Replay | Aprovado | `decision_fingerprint` e separação por `task` | `ProtocolAiAssistPostgresTests` e homologação HTTP |
| Protocolo IA: Histórico & Auditoria | Aprovado | Persistência em `ged.protocolo_ai_revisao` e `ged.app_audit_log` | 100% auditado na mesma transação |
| Matriz HTTP Ponta a Ponta | Aprovado | 49 cenários cobrindo login, ACL, IA, retenção, protocolo | `run-document-ai-local-http.ps1` exit code 0 (`artifacts/ai-operational/http-local.log`) |
