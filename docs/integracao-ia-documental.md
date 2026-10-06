## Evidência operacional atual

Consulte a [auditoria operacional](ai-operational-acceptance.md) para distinguir implementação, evidência real e pendências. A recuperação atual usa bloqueio e transação PostgreSQL no worker existente, com backoff persistido; lease expirável isolado não garante exclusão durante recálculos longos. O upgrade usa um preflight novo antes da migration publicada para preservar identidades com 64 caracteres, sem sufixos maiores que a coluna.

# Integração de IA documental e Protocolo Institucional

Estado da implementação sobre o baseline `8f784ba5e64f3fe9345c98beecb368de84db77b7` (evoluída a partir de `c128712aaec69d3c7149bca8104ea28d61a7c31f`); evidências e limitações atuais no relatório operacional.
Nada deste trabalho foi publicado, integrado ou aplicado em produção.

## Escopo

A IA continua estritamente opcional e subordinada à autoridade do usuário. Busca, OCR, upload, protocolo convencional, tramitações e custódia não dependem do provedor. Se a política, a credencial ou o provedor falharem, o GED e a tramitação do Protocolo seguem 100% disponíveis.

As jornadas documentais já entregues foram preservadas: resumo, preenchimento, tipo documental, governança e administração. A assistência de IA ao **Protocolo institucional (`/Protocolo`)** foi implementada nesta entrega. Comparação documental, voz e multimodalidade permanecem em etapas posteriores.

## Jornadas

| Jornada | Tarefa | Catálogo | O que grava |
|---|---|---|---|
| Pergunte ao acervo | `AskCollection` | fontes já autorizadas da busca | não altera documento; trechos convencionais permanecem se a síntese falhar |
| Resumir documento | `Summarize` | OCR da versão | não altera documento |
| Sugerir preenchimento | `ExtractMetadata` | OCR da versão | título, descrição e sigilo confirmados |
| Sugerir tipo documental | `SuggestClassification` | `ged.document_type` ativo | `type_id` e a linha canônica de classificação do tipo |
| Sugerir classificação arquivística | `SuggestArchivalClassification` | itens ativos da versão mais recente de `ged.classification_plan_version` | `classification_id` e `classification_version_id` |
| **Assistência ao Protocolo** | `SupportProtocol` | `ged.protocolo` + histórico de tramitações + documentos GED autorizados com OCR | resumo consultivo, pendências, **assunto** (atualiza `ged.protocolo.assunto`), **minuta de despacho** (salva como rascunho de observação do tipo `DESPACHO` em `ged.protocolo_observacao`) e histórico em `ged.protocolo_ai_revisao` |

---

## Assistência de IA ao Protocolo Institucional (`SupportProtocol`)

### 1. Construção do Contexto no Servidor
- A seleção de fontes ocorre exclusivamente no servidor: dados do protocolo (`ged.protocolo`), últimas movimentações (`ged.protocolo_tramitacao`), e documentos GED vinculados (`ged.protocolo_documento_ged`).
- Validação prévia de acesso:
  - O usuário precisa ter autorização no setor atual do processo (`IProtocolAccessService`);
  - Cada documento vinculado passa pela política de autorização ABAC (`IAbacAuthorizationService.CanAccessDocumentAsync`);
  - Peças sem permissão ou sigilosas têm conteúdo resguardado, sendo sinalizada cobertura parcial (`coverage.partial = true`);
  - Para peças autorizadas, utiliza-se o texto OCR extraído (`ged.document_search.ocr_text`).
- O texto de documentos é tratado apenas como dado de entrada: nenhuma instrução embutida neles sobrepõe a instrução da tarefa.

### 2. Modalidades Disponíveis na Interface
Na tela de detalhes do processo (`/Protocolo/Details/{id}`), a aba "IA Assistência" fornece:
- **Resumo do processo:** síntese dos fatos, peças e tramitações;
- **Pendências documentais:** itens confirmados versus itens que exigem conferência humana obrigatória;
- **Sugestão de assunto:** proposta comparada com o assunto atual, permitindo edição humana antes da confirmação;
- **Minuta de despacho:** proposta textual fundamentada para revisão e edição pelo operador.

### 3. Regras de Decisão Humana e Governança
- A IA **NÃO** executa automaticamente tramitação, despacho definitivo, deferimento, indeferimento, encerramento, reabertura, alteração de sigilo ou assinatura.
- **Assunto:** Quando aceito pelo usuário, é aplicado via comando canônico no banco (`UPDATE ged.protocolo SET assunto = @Subject...`).
- **Minuta de Despacho:** Salva exclusivamente como rascunho de observação institucional do tipo `DESPACHO` (`ged.protocolo_observacao`). O status do processo permanece `TRAMITANDO`.
- **Concorrência Otimista:** O token de concorrência (`concurrencyToken`, timestamp em milissegundos) é validado antes de qualquer gravação; se o processo foi alterado concorrentemente, a aplicação retorna conflito (`HTTP 409`).
- **Idempotência (Replay):** Cada decisão gera um `decision_fingerprint`. Replays da mesma decisão retornam o resultado já gravado sem duplicar histórico nem auditoria. Decisão divergente para o mesmo ciclo é rejeitada.
- **Auditoria Transacional:** Toda decisão grava na tabela `ged.app_audit_log` sob as ações `AI_PROTOCOL_SUBJECT_APPLY` ou `AI_PROTOCOL_DRAFT_APPLY` na mesma transação.
