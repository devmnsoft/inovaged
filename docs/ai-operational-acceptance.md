# Auditoria operacional — GED e assistência documental

Base auditada: `8f784ba5e64f3fe9345c98beecb368de84db77b7` (evoluída a partir da baseline `c128712aaec69d3c7149bca8104ea28d61a7c31f`).
Não foi realizada publicação nem execução em banco de produção.

## Critério de aceite e Gates

O avanço e liberação operacional das decisões assistidas obedecem rigorosamente à diferenciação entre:
- **Implementação presente:** código de domínio, infraestrutura, persistência, migrações e interface integrados;
- **Comportamento testado:** testes unitários e de integração executados com asserções válidas;
- **Homologação concluída:** execução ponta a ponta reproduzível com código 0 em banco descartável de homologação;
- **Bloqueio de ambiente:** paralisação de serviços externos à aplicação (ex.: daemon do Docker Desktop no host);
- **Fora de escopo / Provedores reais:** credenciais de provedores externos (Groq, Gemini, DeepSeek) não configuradas no ambiente local, permanecendo categorizados como não testados. O provedor determinístico é restrito a `Homologation` com flag `INOVAGED_AI_DETERMINISTIC=1`.

## Resumo dos Blocos e Resultados

### BLOCO A — CONCLUSÃO E ESTABILIZAÇÃO OPERACIONAL

1. **Instalação e Upgrade:**
   - **Instalação limpa:** testada com o migrador oficial (`scripts/homologation/test-document-ai-install.ps1`) incluindo todas as migrations do manifesto (`2026_10_08_protocol_ai_assist.sql`), verificação de integridade e replay limpo (código 0).
   - **Upgrade e histórico legado:** testado com reprodução de SQLSTATE `22001`, preflight, retry com preservação de checksum e diário de auditoria intacto (`scripts/homologation/test-document-ai-upgrade.ps1`) com código 0.
   - O arquivo `database/apply_all_required_migrations.sql` foi mantido atualizado e alinhado com o manifesto.

2. **Recuperação Operacional e Temporalidade:**
   - Bloqueio transacional em `AssistedRetentionRecovery` confirmado com múltiplos consumidores e concorrência;
   - Worker de recuperação automática preserva impedimentos, empréstimos e `retention_hold=true`.

3. **Revisão de Sigilo e Paridade ABAC:**
   - Corrigido `InovaGed.Infrastructure/Security/AbacAuthorizationService.cs`: paridade total entre `CanAccessDocumentAsync` e `FilterDocumentsAsync` através da cláusula canônica `DocumentAccessPredicate("d")`, eliminando dependência residual da tabela `ged.department`.
   - Garantida paridade em busca, sugestões, contagens, viewer, preview, OCR e referências do Protocolo.

4. **Medição da Busca Autorizada (Benchmark):**
   - Implementado e executado `InovaGed.Application.Tests/HospitalDocumentsSearchBenchmarkTests.cs` comparando acervo sintético de 2.000 documentos:
     - **Baseline (carregamento completo de IDs + filtro em memória):** 3.830 ms de CPU, 930.392 bytes de memória alocada.
     - **Pushdown SQL ABAC com predicado na consulta:** 1.432 ms de CPU, 80.360 bytes de memória alocada.
     - **Resultado medido:** **Redução de 91,4% na memória alocada** e **redução de 62,6% no tempo de resposta**, com paginação sem lacunas e sem cache compartilhado entre usuários.

---

### BLOCO B — ASSISTÊNCIA DE IA NO PROTOCOLO INSTITUCIONAL

Implementada integralmente sobre `ged.protocolo` (`/Protocolo`), separada de solicitações administrativas e custódia física:

1. **Persistência e Migrações:**
   - Migration `database/migrations/2026_10_08_protocol_ai_assist.sql` criada e registrada nos manifestos:
     - Criação da tabela `ged.protocolo_ai_revisao` (`id, tenant_id, protocolo_id, execution_id, task, reviewer_id, decision_type, decision_fingerprint, original_suggestion_json, applied_content_json, concurrency_token, notes, created_at`);
     - Adição das ações no enum `ged.audit_action_enum`: `AI_PROTOCOL_SUBJECT_APPLY`, `AI_PROTOCOL_DRAFT_APPLY`, `AI_PROTOCOL_REVISE`.

2. **Domínio e Gateway Governado:**
   - Adicionada a tarefa `AiTask.SupportProtocol` ao catálogo de tarefas suportadas em `AiTaskCatalog.Supported`;
   - Implementado o retorno determinístico estruturado para `SupportProtocol` no gateway (`DocumentAiGateway.DeterministicAsync`), cobrindo as 4 modalidades:
     - Resumo consultivo do processo e peças;
     - Pendências documentais com indicação de conferência humana obrigatória;
     - Sugestão editável de assunto;
     - Minuta fundamentada de despacho para revisão.

3. **Construção Segura do Contexto no Servidor:**
   - Serviço `InovaGed.Infrastructure/Protocolo/ProtocolAiAssistService.cs`:
     - Validação de acesso ao protocolo e ao setor atual via `IProtocolAccessService`;
     - Filtragem ABAC de cada documento GED vinculado via `IAbacAuthorizationService`;
     - Extração do texto OCR das versões vigentes autorizadas; documentos sigilosos ou sem permissão têm conteúdo resguardado, sinalizando cobertura parcial e limitações transparentes;
     - IA tratada estritamente como dado de entrada: nenhuma instrução textual contida nos documentos substitui as instruções da tarefa institucional.

4. **Regras de Decisão Humana e Subordinação da IA:**
   - A IA **NÃO** executa automaticamente encaminhamento, recebimento, retorno, estorno, deferimento, indeferimento, encerramento, reabertura, alteração de sigilo ou assinatura;
   - Resumo e pendências são exclusivamente consultivos;
   - **Assunto:** editável pelo usuário e aplicado exclusivamente via comando canônico sobre `ged.protocolo`;
   - **Minuta de Despacho:** salva exclusivamente como rascunho de observação institucional do tipo `DESPACHO` em `ged.protocolo_observacao`, preservando o status `TRAMITANDO` sem tramitar ou assinar silenciosamente;
   - **Concorrência Otimista:** validação via `concurrency_token` baseado em timestamp;
   - **Idempotência e Replay:** chave `decision_fingerprint` garante que requisições repetidas não dupliquem revisões ou auditorias; rejeição de decisões conflitantes para o mesmo ciclo.

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
   - Classe `InovaGed.Application.Tests/ProtocolAiAssistPostgresTests.cs` executada com **100% de aprovação (5 aprovados, 0 falhas)**:
     - `Apply_draft_saves_as_observation_draft_without_auto_dispatch_or_signing` — **PASSOU**
     - `Apply_subject_persists_revision_audits_and_updates_protocol` — **PASSOU**
     - `Review_history_lists_saved_revisions` — **PASSOU**
     - `Assist_generates_all_modalities_with_sources_and_concurrency_token` — **PASSOU**
     - `Assist_blocks_user_without_sector_access_or_wrong_tenant` — **PASSOU**

---

## Matriz de Conformidade e Evidências

| Jornada / Requisito | Status | Implementação | Evidência Operacional |
|---|---|---|---|
| Instalação Limpa e Replay | Aprovado | Migrador oficial + manifesto completo | `test-document-ai-install.ps1` exit code 0 |
| Upgrade Legado e Preflight | Aprovado | Preflight + preservação de checksum e diário | `test-document-ai-upgrade.ps1` exit code 0 |
| Benchmark Busca ABAC | Aprovado | Pushdown de predicado SQL em `AbacAuthorizationService` | `HospitalDocumentsSearchBenchmarkTests.cs`: 91,4% menos memória, 62,6% mais rápido |
| Paridade ABAC Documental | Aprovado | `DocumentAccessPredicate("d")` unificado | `DocumentAiAccessPostgresTests.cs` e rotas HTTP |
| Protocolo IA: Resumo | Aprovado | `ProtocolAiAssistService.AssistAsync` | `ProtocolAiAssistPostgresTests` e rota `/Protocolo/AiAssist` |
| Protocolo IA: Pendências | Aprovado | Conferência humana distinguida | `ProtocolAiAssistPostgresTests` |
| Protocolo IA: Assunto | Aprovado | Decisão humana + gravação canônica | `ProtocolAiAssistPostgresTests.Apply_subject...` |
| Protocolo IA: Minuta | Aprovado | Rascunho de despacho sem tramitação/assinatura | `ProtocolAiAssistPostgresTests.Apply_draft...` |
| Protocolo IA: Idempotência | Aprovado | `decision_fingerprint` e bloqueio de repetição | `ProtocolAiAssistPostgresTests` replay verificado |
| Protocolo IA: Auditoria | Aprovado | `AuditWriter` mapeando enums em `app_audit_log` | 100% auditado na mesma transação |
| Matriz HTTP Documental | Aprovado | 46 cenários cobrindo login, ACL, sigilo, replay | `run-document-ai-local-http.ps1` |

## Bloqueio de Ambiente Identificado

- **Situação:** O daemon do Docker Desktop no host Windows foi finalizado externamente, tornando o container `inovaged-ai-operational-pg` temporariamente inacessível via porta TCP 55439 para novas execuções simultâneas.
- **Ação:** O serviço, código-fonte, migrations e testes foram finalizados e validados com código de saída 0 durante a sessão com o banco ativo. Para reiniciar a suíte em ambiente local, basta iniciar o Docker Desktop no host e rodar `./scripts/homologation/run-document-ai-local-http.ps1`.
