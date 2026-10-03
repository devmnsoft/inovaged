# Integração de IA documental

## Escopo e segurança

A IA é opcional e subordinada ao GED. O SmartSearch recupera fontes já autorizadas; somente trechos/versões selecionados são enviados. Conteúdo documental é dado não confiável, nunca instrução. Se política, credencial ou provedor falhar, GED, OCR e pesquisa convencional continuam disponíveis.

O gateway não executa protocolo, eliminação, HOLD, temporalidade ou empréstimo. Resumo e sugestões são revisáveis e não alteram o original.

## Configuração segura e política efetiva

A configuração global `DocumentAi` começa com `Enabled=false`, autoriza provedores/modelos e estabelece máximos. Chaves existem somente nas variáveis `GROQ_API_KEY`, `GEMINI_API_KEY` e `DEEPSEEK_API_KEY`; a UI mostra apenas “configurada/ausente”. Nunca grave segredos em banco, logs ou frontend.

A migration aplicada `2026_10_02_document_ai_governance.sql` cria a política explícita por tenant; ela não deve ser reescrita. O hardening incremental está em `2026_10_03_document_ai_governance_hardening.sql`. Ausência de registro bloqueia antes da rede. **Administração → IA por cliente** reapresenta os valores salvos, permite habilitar cada tarefa e aceita somente provedor/modelo estruturado do catálogo global e limites que não o excedam. A revisão implementa concorrência otimista. Tenant e usuário vêm de `ICurrentUser`, não do navegador.

`BaseUrl` exige HTTPS e host oficial (`api.groq.com`, `generativelanguage.googleapis.com` ou `api.deepseek.com`) ou host explicitamente confiável.

## Contratos dos provedores

Referências oficiais usadas no desenho (a consulta automatizada recebeu HTTP 401 em 2 de outubro de 2026; repita em homologação):

- [Groq API](https://console.groq.com/docs/api-reference) e [Structured Outputs](https://console.groq.com/docs/structured-outputs).
- [Gemini text generation](https://ai.google.dev/gemini-api/docs/text-generation) e [Structured output](https://ai.google.dev/gemini-api/docs/structured-output).
- [DeepSeek chat completion](https://api-docs.deepseek.com/api/create-chat-completion) e [JSON output](https://api-docs.deepseek.com/guides/json_mode).

O modelo precisa constar em `AllowedModels` e `StructuredOutputModels`; capacidade genérica do provedor não basta. O validador local aceita somente `type`, `required`, `enum`, `properties`, `additionalProperties`, `items`, `minLength`, `maxLength`, `minItems`, `maxItems`, `minimum` e `maximum`. Palavra-chave desconhecida é rejeitada, não ignorada. Tipos incorretos, JSON malformado, saída vazia/truncada e estrutura incompatível viram `InvalidOutput`.

## Execuções, cota e recuperação

`ai_execution` registra tenant/usuário, tarefa, provedor/modelo, referências, **fontes verificáveis da execução** (`source_documents`, adicionada pela migration `2026_10_04_document_ai_execution_sources.sql` com índice GIN e backfill idempotente a partir do formato legado `document_refs`), fingerprint da entrada, correlação, revisão, período da reserva, idempotência, estado, duração e uso — nunca OCR integral ou segredo. As fontes são os pares documento+versão resolvidos **no servidor** que autorizam a execução; o cliente nunca as fornece. A unicidade `(tenant_id, user_id, task, idempotency_key)` e o tratamento do conflito de inserção garantem uma execução local; reutilizar a chave com outro fingerprint é conflito explícito.

A reserva usa transação e atualização condicional em `ai_monthly_usage`, revalidando revisão, tarefa, provedor e modelo; o período mensal é lido do SQL (`date_trunc('month', now())`) para que a escrita sempre coincida com todos os relatórios de mês. O tamanho é estimativa de entrada mais o **máximo efetivo de saída** (`MaximumOutputTokens` limitado a 64..32768), não um valor fixo arbitrário.

`MarkRunningAsync` só marca o envio se a política ainda for a mesma; `sent_at` é carimbado apenas quando o gateway realmente entrega a requisição ao provedor (`MarkSentAsync`, vigente somente enquanto `Running` e não liquidado). Toda falha anterior à rede — política, schema, endpoint, credencial, tamanho do prompt — liquida o lease com **zero** consumo. Pedido enviado que volta sem medição contabiliza a reserva como estimativa, nunca como zero comprovado.

A liquidação é condicional e única (`settled_at` com trava de linha `for update`): resposta duplicada ou tardia, depois da liquidação, reconcilia apenas estado, medição reportada e resultado — sem tocar duas vezes no consumo mensal. O acerto usa o período gravado mesmo após virada do mês. Resultados estruturados ficam em armazenamento privado por 30 dias; `result_expires_at` apaga o corpo na expiração e a consulta só o devolve dentro da vigência, exigindo tenant e usuário.

A função reescrita `ged.expire_ai_reservations()` reconcilia contra o carimbo real de envio: reserva **nunca enviada** vira `Expired` consumindo zero; reserva **enviada** sem desfecho vira `RemoteOutcomeUnknown` e liquida a reserva como uso estimado; ambas liberam o saldo em `ai_monthly_usage`. Timeout ou cancelamento depois do envio nunca é reenviado automaticamente. O Operations Worker roda backup e expiração de IA em **ciclos isolados por falha** (uma falha não impede a outra; contagens de falhas consecutivas são registradas) e emite resumo de saúde de recuperação (`RemoteOutcomeUnknown` / `Expired` / pendentes de expirar).

## Resposta fundamentada e resumo

`DocumentEvidenceService` combina cobertura de recuperação e síntese, interpreta `answered`, `partial_coverage` e `insufficient_evidence`, rejeita combinações contraditórias, limita afirmações/texto/referências, descarta referência desconhecida e informa redução. Fontes convencionais permanecem visíveis. Após a chamada, acesso, situação e versão são revalidados contra a linha **atual** do documento (ver seção Autorização). Uma referência é evidência para revisão humana, não prova semântica automática.

No visualizador hospitalar, **Resumir documento** autoriza antes de ler o OCR e revalida acesso, situação e versão depois da chamada. Fatos, datas e pendências exigem trecho literal, validado pelo servidor contra o OCR enviado, e a interface oferece abertura da fonte. Sem OCR há erro compreensível. Acima de 120.000 caracteres o servidor recusa sem truncar; processamento em partes permanece pendente. Cancelamento é visível e o original não é alterado.

A consulta `GET AiExecution` reautoriza cada fonte registrada na execução contra a confidencialidade corrente do documento de origem; se qualquer fonte perder `VIEW`, o resultado inteiro é negado (nunca parcial). Linhas criadas antes da migration de fontes não têm `source_documents` e seguem o caminho legado, que exige `versionId` na consulta. **Contrato de formato:** `result_json` guarda `Failure` como número (serialização do enum); linhas escritas fora desta aplicação (ex.: seed de homologação) devem respeitar o formato — `"Failure":"None"` como texto quebra a desserialização do resultado (`JsonException Path: $.Failure`, HTTP 500).

## Preenchimento e classificação assistidos (Bloco B)

As duas jornadas seguem o mesmo padrão: fonte autorizada → sugestão com evidência literal por campo, validada pelo servidor contra o original → revisão humana campo a campo (aceitar/corrigir/rejeitar) → confirmação → gravação canônica com revalidação → auditoria before/after. Nada é gravado sem confirmação explícita do usuário.

### Preenchimento assistido (metadados)

- `POST SuggestMetadata` (tarefa `ExtractMetadata`): exige `VIEW`; sugere `title`, `description` e `isConfidential` com evidência literal validada contra o OCR extraído; devolve valores atuais, valores sugeridos e `updatedAt` (token de edição). Sem OCR → `422` compreensível.
- `POST ApplyMetadataSuggestion`: exige `EDIT`; aplica **apenas os campos confirmados**, individualmente. Revalida que a versão continua atual e que o documento não mudou desde a sugestão (janela `expectedUpdatedAt` ±25 ms) e finaliza com UPDATE condicional sobre `updated_at`: edição concorrente faz a aplicação falhar com `409` sem gravar nada. Auditoria `AI_METADATA_APPLY` com `before`/`after` (título, descrição, confidencialidade) e revisor.
- Interface: seção “Preenchimento assistido” do visualizador (`wwwroot/js/document-assist.js`) compara sugerido × atual campo a campo, permite abrir a evidência no OCR e confirma cada campo separadamente.

### Classificação assistida

- `POST SuggestClassification` (tarefa `SuggestClassification`): exige `VIEW`; o modelo deve escolher **exatamente um** tipo do catálogo ativo do tenant (`NENHUM` quando nenhum se aplicar); tipo fora do catálogo → `422`; evidência validada contra o conteúdo consultado (OCR truncado a 120.000 caracteres quando necessário, com aviso explícito).
- `POST ApplyClassification`: exige `EDIT`; valida o tipo escolhido (ativo, mesmo tenant) e repete a mesma revalidação de versão/edição acima. Grava pelo serviço canônico `IDocumentClassificationCommands.SaveManualAsync` (classificação + documento + tags manuais, com auditoria própria antes/depois) e recalcula a temporalidade via `RetentionRecalcService.RunOneAsync`; a resposta informa explicitamente se o recálculo ocorreu. Auditoria `AI_CLASSIFICATION_APPLY` com tipo `before`/`after` e revisor.

### Catálogo de tarefas

`AiTaskCatalog.Supported` expõe apenas as tarefas implementadas ponta a ponta (`Summarize`, `ExtractMetadata`, `SuggestClassification`). O painel do cliente lista somente essas, e o salvar rejeita tarefa desconhecida ou membro inválido de enum (`Enum.IsDefined`) em vez de descartá-las silenciosamente. Protocolo assistido, comparação, imagens e embeddings permanecem no backlog.

### Painel do cliente (Administração → IA por cliente)

O consumo aparece **separado**: total reportado pelos provedores versus total liquidado por estimativa (com total liquidado, número de execuções e falhas). As últimas execuções são paginadas (25/página) com estado, tokens reportados/liquidados, origem do consumo (“Medido” ou “Estimado”) e correlação; o contador de falhas agora inclui `Expired`.

## Autorização e revalidação pós-chamada (Bloco A)

As ações lógicas mapeiam para códigos reais do catálogo `ged.permission`, via papéis ativos do tenant (`AbacAuthorizationService`): `VIEW` → `Documents.View` e `EDIT` → `GED.DOCUMENTS` (removida a consulta legada à tabela inexistente `ged.permissions`). Documento confidencial mantém a restrição de horário comercial (06–20 UTC).

- **Pré-chamada:** leitura/sugestão exige `VIEW`; aplicação exige `EDIT`. Negação de escrita responde com `Forbid` (usuário autenticado: `302 → /Account/AccessDenied?ReturnUrl=…`, conforme `AccessDeniedPath`) e audita `AI_ACCESS_DENIED` com o motivo: `authorization_missing` (sugestão sem leitura), `edit_permission_missing` (aplicação sem edição) ou `authorization_or_state_changed` (revalidação falhou).
- **Pós-chamada:** após o provedor responder, acesso, situação e versão são revalidados contra a linha **atual** de `ged.document` (versão corrente + confidencialidade), não contra o instantâneo lido antes da chamada; qualquer mudança descarta o resultado com `409`.
- **Consulta de resultado:** `GET AiExecution` exige que todas as fontes registradas continuem existindo e passam por `VIEW` com a confidencialidade corrente (ver seção acima).

## Auditoria

| Ação | Quando | Detalhes |
|---|---|---|
| `AI_SUMMARY` | Resumo aceito após revalidação | versão, provedor, modelo, correlação |
| `AI_METADATA_APPLY` | Aplicação de metadados assistidos | `before`/`after` (título, descrição, confidencialidade) + revisor |
| `AI_CLASSIFICATION_APPLY` | Aplicação de classificação assistida | tipo `before`/`after` + revisor |
| `AI_ACCESS_DENIED` | Negação pré-chamada, pós-chamada ou em consulta | versão + motivo (`authorization_missing`, `edit_permission_missing`, `authorization_or_state_changed`) |

`AuditWriter.MapAction` mapeia estas quatro ações para si mesmas (identidade): `app_audit_log.action` é `text` e o padrão antigo (`_ => "VIEW"`) as mascarava como leitura comum.

## Homologação

Dois níveis complementares; mocks não homologam integração real.

### Comportamental (PostgreSQL real)

1. Aplique a migration em PostgreSQL descartável; valide instalação limpa e upgrade sobre a existente (checksums preservados).
2. Defina `INOVAGED_AI_PG_DSN` apontando para esse banco e rode a suíte comportamental (`AiGovernanceBehaviorTests`): 13 fatos PG — concorrência sobre a cota, idempotência (reuso/conflicto), revisão de política no envio, carimbo real de `sent_at`, liquidação única, resposta tardia sem cobrar duas vezes, expiração com zero consumo quando nunca enviado, timeout sem consumo antes do provedor, provedor atingido sem medição liquida a estimativa, escopo de consulta por usuário + fontes + vigência do resultado, saúde de recuperação, política fail-closed — mais testes de reserva in-memory (tamanho da reserva com máximo efetivo, falhas pré-rede sem consumo). Sem a variável, os fatos PG pulam em CI.
3. Regressão: a suíte completa reproduz a baseline pré-entrega (falhas de ambiente estáveis, sem novas).

### HTTP ponta a ponta (ambiente restaurado)

Ambiente: banco descartável restaurado de produção (`inova_ba_http`), PostgreSQL nativo na porta 5432 (usuário `postgres`), aplicativo web na porta 5210 (o harness força `Host: localhost` nos pedidos) e mock Groq na porta 8443 (fixo em 600 tokens por chamada: 420 entrada / 180 saída). Usuários da matriz: viewer/editor criados para o teste (prefixo de id `cccccccc`).

Fluxo (artefatos na pasta de operação `ba_http\` do operador; não fazem parte do repositório):

1. `reset_bahttp.sql` — reset idempotente para o estado de contrato (execuções por prefixo `ba000001-%` e janela `created_at >= '2026-10-02 23:00:00+00'`, cota, política, documento de teste, classificação e auditoria), com blocos de auto-verificação (inclui `LEGACY_FAILURE_FIXED`, que normaliza a seed legada `"Failure":"None"` para o número `0`, conforme o contrato de formato acima).
2. `precheck.sql` — captura do contrato pristine; comparar arquivo por arquivo com a captura esperada.
3. `matrix.ps1` (arquivo precisa de BOM UTF-8 para PowerShell 5.1) — 86 passos: logins 5/5; autorização (viewer/editor, outro tenant 404, outra situação 422, sem permissão de edição 302 → AccessDenied); k-loop com chaves de idempotência **dinâmicas** (GUID 32 hex) por passo de resumo; GETs legados curados; painel admin com paginação e totais reportado/estimado; salvar política (revisão 1→2) e redirect para `/AiAdministration`; substrings do OCR. Esperado: 86/86 PASS, `FAIL=0`.
4. `final_asserts.sql` — asserts de aceite FA0–FA13 sobre o banco: 27 execuções novas todas `Completed` com reportado = liquidado = 600; consumo 16.800 = 600 legado + 27×600 do mock; reservado 0; política DEFAULT rev 2 (3 tarefas) e T2 rev 1; `AI_ACCESS_DENIED` com `edit_permission_missing`; efeito do mock no documento (título/descrição/confidencialidade/tipo); usuários da matriz sem bandeira de acesso negado. Todos verdes.

Chaves de idempotência: sondas fixas `ba000001-…`; repetição de um passo deve reaproveitar a mesma chave com o mesmo conteúdo (mesmo fingerprint); conteúdo diferente sob a mesma chave é `IdempotencyConflict` explícito. Contagens absolutas entre execuções só fazem sentido após o `reset_bahttp.sql`. App e mock permanecem vivos entre execuções; para reconstruir, encerrar os processos antes do `dotnet build` (apphost bloqueia o binário) e relançar app (5210) e mock (8443) com readiness check.

Repita com credenciais reais (`GROQ_API_KEY`) para homologar a integração real; confirme teclado, foco, loading, cancelado, parcial, erro, responsividade e original intacto; execute regressão de upload, protocolo, custódia e temporalidade.

## Estado verificável desta entrega

- **Implementado:** Bloco A (fontes verificáveis server-side; carimbo real de envio; zero consumo para falha pré-rede; reserva dimensionada pelo máximo efetivo de saída; liquidação única com resposta tardia sem cobrança dupla; ABAC fail-closed contra o catálogo real de permissões; revalidação pós-chamada contra a linha atual do documento; worker isolado por falha; saúde de recuperação) e Bloco B (jornadas assistidas de preenchimento e classificação com confirmação campo a campo, serviço canônico de classificação, auditoria before/after, catálogo de tarefas e painel com reportado/estimado paginado).
- **Verificado em PostgreSQL real (descartável):** migrations `2026_10_03`/`2026_10_04` em instalação limpa e upgrade; suíte comportamental 13/13 verde.
- **Verificado via HTTP (ambiente restaurado):** matriz de aceite 86/86 (`FAIL=0`) com evidência crua por passo em `results\`; asserts finais FA0–FA13 todos verdes; comparação pristine/precheck idêntica.
- **Regressão:** build com 0 erros; suíte de testes reproduz a baseline exatamente (36F/516P/14I — falhas de ambiente estáveis, nenhuma nova).
- **Pendente:** chamada a provedor real em homologação (mock usado nesta execução); resumo em partes/fila para documentos acima de 120.000 caracteres; teste de conexão global; painel global multi-tenant (transversal a clientes). Protocolo assistido, comparação, imagens e embeddings permanecem no backlog.

## Evidências desta execução (continuação da PR #551)

- Baseline: `main` em `f7fd9c06` (merge da PR #551); trabalho uncommitted na working tree, commit local sem push/merge.
- Build: .NET SDK 8–10 disponível no ambiente; build com 0 erros.
- Comportamental: suíte PG 13/13 verde com `INOVAGED_AI_PG_DSN` em banco descartável; testes de reserva in-memory verdes; regressão completa idêntica à baseline.
- Migration: `2026_10_04_document_ai_execution_sources.sql` validada em instalação limpa e sobre a existente (checksums preservados); registrada em `migrations.manifest.json`, `required_migrations.json` e `apply_all_required_migrations.sql`.
- HTTP: `ba_http\results\summary.txt` 86/86 + evidência crua por passo; `final_asserts.out` FA0–FA13 verde; precheck/pristine idênticos.
- Defeito de dados resolvido: seed legada com `"Failure":"None"` (texto) no `result_json` quebrava a consulta de execução (HTTP 500); o reset normaliza para o formato numérico esperado (`jsonb_set(result_json,'{Failure}','0'::jsonb)`).
- Consulta às documentações oficiais permanece pendente em homologação (HTTP 401 em 2 de outubro de 2026).
