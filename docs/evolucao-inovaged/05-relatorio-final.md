# 05 — Relatório Final: Evolução InovaGED (Blocos A + B + C)

Data: 29/09/2026 · Ambiente: `inovaged_repro` (PostgreSQL 18) · Base: `main` @ `c03d383` · Build Release (0 erros; baseline de 240 avisos mantido) · App em `http://localhost:5000`.
Nada publicado, mergeado ou aplicado a produção. Migrations incrementais/aditivas/idempotentes, aplicadas **duas vezes** no repro sem efeito colateral. Regras de processo: DOCX DAME (`...\opencode\trnsito\extracted.txt`) — fornecedoras apenas das regras; as estruturas usadas são as do repositório.

Documentos complementares: `01-matriz-requisitos.md` (diagnóstico), `02-mapeamento-estrutural.md` (decisões), `03-evidencia-reproducao.md` (falhas reproduzidas), `04-bloco-a-correcoes.md` (Bloco A detalhado).

Legenda: **passou** · **parcial** · **n executado** (com justificativa) · **excluído por instrução**.

---

## 1. Resumo executivo

| Bloco | Escopo | Status | Evidência principal |
|---|---|---|---|
| A | Confiabilidade de upload/anexos (U4–U8, A2, H2, H3, H4/H4B) | **Concluído** | Doc 04: fluxo real de chunks 3 partes → documento íntegro; numeração `PROT-2026-00000x`; sanitização de anexos; políticas POST; view Details criada |
| B | Trânsito físico DAME ↔ setores (RF02–RF06, RN01, RN03, RN05, RNF01, RNF02) | **Concluído** | Probe E2E verde (matriz de 5 perfis, corridas duplas, devolução parcial, extravio, OVERDUE, histórico, abas); **RNF01 medido: 666 ms / 100 itens** (alvo ≤ 2 s) |
| C | Temporalidade/retentão (C1 recálculo idempotente, C2 evidência de regra + bloqueios na execução, C3 central explicativa) | **Concluído** | Probe E2E final: **66/66 asserts PASS, 0 FAIL** (regressão do Bloco B e tab check também verdes na mesma rodada) |

Falhas abertas ao final: **nenhuma dentro do escopo**. Pendências/riscos: §9.

---

## 2. Bloco A — antes/depois (detalhamento no doc 04)

| Item | Antes | Depois | Status |
|---|---|---|---|
| Upload em chunks (U5–U8) | `Start` → 500 `42601` (INSERT 13 colunas × 12 expressões); schema de chunks fora do manifest | INSERT alinhado; migration compatibilidade no manifest; fluxo real 3 partes → `COMPLETED 3/3`, storage íntegro (3.000 B) | passou |
| Falso sucesso Finish (U4) | Lote com 2/2 rejeitados → `success:true, failed=0` | `success:false` + mensagem "Lote finalizado sem nenhum documento criado… Reenviar falhas" | passou |
| Numeração de protocolo (A2-1) | INSERT em `ged.code_sequence` com colunas inexistentes → 500 | Formato real da tabela + `FOR UPDATE`; E2E `PROT-2026-000001..000003` sequencial | passou |
| Anexos em protocolo (A2-2/A2-3/R4) | Nomes ilegais gravados como estão; NRE; falha pós-commit = 500/sucesso falso; transição repetida duplicava histórico (8→+) | Helper compartilhado (sanitização, try/catch por arquivo, compensação de órfão, `TempData["FileErrs"]`); `vm ??=`; validação pré-transição; auto-transição = no-op idempotente (8→8) | passou (caminho FileErrs: n executado — §9.2) |
| Mensagens seguras (H2) | `ex.Message` vazando SQL/colunas no HTML | Mensagens estáveis em todos os POSTs de temporalidade/retention; detalhes só no log; `leak=False` | parcial (a causa interna era o bug SQL do Bloco C — corrigido agora no §4.1; Recalcular passa a funcionar de fato) |
| Permissões nos POSTs (H4/H4B) | Somente-leitura executava POSTs via URL direta | Política `RetentionManage` (Admin/AdministradorOphir/ArquivistaOphir + FullAdmin); E2E: hospital → `AccessDenied` em 4 POSTs; admin liberado; negações auditadas em `security_access_failure_log` | passou |
| View ausente (H3) | `RetentionDestination/Details` → 500 (view inexistente) | View criada (itens, prazos, HOLD, CSV, Executar c/ antiforgery) — hoje estendida pelo Bloco C (§4.2) | passou |

---

## 3. Bloco B — Trânsito físico DAME ↔ setores

Implementação sobre o núcleo físico v2 existente (`ged.physical_*`), extensão aditiva — caixas físicas e empréstimos por caixa pré-existentes não alterados. Serviço único `IPhysicalTransitService` (`InovaGed.Infrastructure/PhysicalArchive2/PhysicalTransitService.cs`), escrita sempre em **transação única**, `MaxItemsPerBatch = 100`.

### RF02 — Setores com inativação lógica — **passou** (completa os gaps do diagnóstico)

- **Antes:** 5 setores-alvo do processo ausentes no tenant; "Excluir" de setor com histórico apagava das telas sem trilha/restricção.
- **Depois:**
  - Migration `2026_09_30_ged_physical_transit.sql` faz **seed idempotente** dos 5 setores DAME (UAI, Ambulatório, Radioterapia, Quimioterapia, Admissão de Pacientes) para todos os tenants ativos, guardado por `(tenant, nome)` — aplicável duas vezes sem duplicar.
  - `ProtocoloCadastrosController.Excluir`: para `tipo=setores`, se houver vínculos/histórico ativos (`protocol_request` por setor solicitante/atendido, `protocolo_usuario_setor`, `physical_loan_batch.destination_sector_id`, `physical_loan.sector_id`), o delete lógico é **bloqueado com contagem** e orientação a inativar em Editar (RN02 preservado: segue `update reg_status='E'`, nunca `DELETE`).
- **E2E:** setores UAI/AMB/RTQ/QT/ADM presentes e selecionáveis; exclusão de setor com histórico → mensagem "O setor possui N vínculo(s) ou histórico…" (sem 500).

### RF03 — Check-out DAME → setor — **passou**

- **Antes:** empréstimo só por **caixa**, destino/portador texto livre opcional, política única `FullAdminOnly`, sem vínculo a prontuário do catálogo GED, sem cautela.
- **Depois:** rota `/PhysicalTransit/Checkout` — seleção de **prontuários do catálogo GED** (busca por código), **setor destino obrigatório vindo do dicionário** (select só de setores `reg_status='A' and ativo=true`; FK `protocolo_setor(id)`), **portador obrigatório (nome ou matrícula)** — validações server-side com mensagens específicas ("Selecione o setor destino da saída.", "Informe o nome ou a matrícula do responsável pelo transporte."). Registro único do lote: número `TRN-…`, autor+data, motivo, prazo opcional; uma linha `physical_loan` por prontuário (coluna `document_id`, inativa até então, agora é o elo) + eventos de custódia `PHYSICAL_CHECKOUT`/`BATCH_OPENED`. **Cautela imprimível** por lote (título + itens + portador + setor).
- **E2E:** checkout de lote 3 itens → UAI: `302` para detalhe; DB `lote=OPEN|3`, `loans=3 abertos`, eventos `BATCH_OPENED:1, PHYSICAL_CHECKOUT:3`; cautela renderiza título + documentos.

### RF04 — Check-in com devolução parcial — **passou**

- **Antes:** sem cabeçalho de lote → impossível devolver k<N de um lote multi-itens.
- **Depois:** `CheckinAsync` recebe o subconjunto escolhido; transação com `for update` no lote; k itens → `RETURNED` (+ evento por item), N−k seguem `OPEN`; contadores `items_returned` atualizados; quando todos devolvidos, lote → `CLOSED` + `closed_at/by/by_name`.
- **E2E (lote de 3):** ret1 → `OPEN|1`; ret2 → `OPEN|2`; ret3 → `CLOSED|3` (status exato verificado a cada passo).

### RF05 — Expedição em lote ≤ 100 com portador vinculado, atômica — **passou**

- **Antes:** ausente (`upload_batch*` é digital; `loan_request` não registra expedição física vinculada a portador).
- **Depois:** `CheckoutAsync` — validação de limites no server (≤ 100 com mensagem específica); portador persistido no **lote e em cada item** (`carrier_name/carrier_id`); **atomicidade tudo-ou-nada** em 1 tx (cabeçalho + N empréstimos + N eventos + evento do lote); `batch_number` único por tenant (índice único parcial); itens indisponíveis/recusados voltam com motivo individual sem abortar o restante.
- **E2E:** lote de 100 itens aceito (medida em §7); recusa individual demonstrada no cenário RN01.

### RF06 — Histórico cronológico por prontuário (+ B5: retidos por setor, excedidos, pendências) — **passou**

- **Antes:** timeline existia por **caixa** e por solicitação administrativa; nada unificado por prontuário.
- **Depois:** `DocumentHistoryAsync` — linha do tempo cronológica por documento agregando saídas/devoluções/extravios + eventos de custódia, sem IDs expostos na UI. Listagem de lotes com filtros **setor / status (OPEN–OVERDUE–CLOSED) / busca** e agregados: itens em aberto por lote, itens com prazo excedido, itens perdidos, retidos por setor (visão por `destination_sector_id`). **OVERDUE é derivado**: `status` continua `OPEN`; a exibição usa `case when due_at < now() then 'OVERDUE'` e o filtro OPEN exclui já-excedidos — sem worker que mude estado.
- **E2E:** prontuário com ciclo completo sai→devolve aparece cronologicamente; filtros OPEN/OVERDUE/CLOSED segregam corretamente (lote vencido some de OPEN e aparece em OVERDUE); histórico sem IDs guindados na tela.

### RN01 — Sem segunda movimentação aberta do mesmo prontuário — **passou** (era "Quebrado")

- **Antes:** sem verificação prévia, sem trava de concorrência, sem constraint → POST duplo/concorrente podia criar 2 saídas abertas.
- **Depois (camadas):** (1) pré-checagem por item com recusa + motivo ("Já possui saída em aberto neste momento."); (2) **índice único parcial** `ux_physical_loan_open_document(tenant_id, document_id) where reg_status='A' and status='OPEN'`; (3) captura do `23505/23503` com rollback + mensagem amigável; (4) `for update` nas rotas de escrita.
- **E2E:** POST sequencial duplicado → mensagem + catálogo isolado do item aberto; **corrida: dois POSTs simultâneos do mesmo doc → exatamente 1 linha OPEN** no banco (verificação direta em `physical_loan`).

### RN03 / RN05 — Portador obrigatório + destino obrigatório entre os 5 setores — **passou**

- **Antes:** ausentes (texto livre opcional / sem FK ao dicionário).
- **Depois:** validação server-side nos dois campos (mensagens específicas, §RF03); FK `destination_sector_id → protocolo_setor`; select limitado a setores ativos do dicionário. E2E: checkout sem setor e sem portador → bloqueados com mensagem; com matrícula-only → aceito.

### LOST / extravio — **passou**

`MarkLostAsync` (item em aberto): `status='LOST'` + `lost_reason` + `returned_at`, evento `PHYSICAL_LOAN_LOST` com justificativa e autor, em transação; lote permanece OPEN com `ItemsLost` visível. E2E: item marcado como perdido com justificativa; trilha completa.

### Correção UTC — **passou**

Parâmetros `timestamptz` (prazos, datas) passaram a ser enviados como `DateTimeOffset` offset 0 (UTC) — correção do desvio observado nas comparações de vencimento no ambiente dev (fuso local ≠ banco).

### Abas e menu (B7) — **passou**

Novas abas compartilhadas `_ProtocolTabs.cshtml` (minhas solicitações / fila / histórico / trânsito / devoluções) nas telas de protocolos + item de menu lateral com visibilidade por perfil. E2E `tabcheck`: 5 páginas, abas presentes e `active-href` correto em todas.

---

## 4. Bloco C — Temporalidade / retentão

### C1 — Recálculo com regra aplicada, base/prazo e idempotência — **passou**

- **Antes (reproduzido):** todo Recalcular falhava internamente — `SQLSTATE 23502 o valor nulo na coluna "id" da relação "retention_case"` e colunas inexistentes nos itens (`createCaseSql` sem `id`/`title`; `createItemSql` usava `status`/`created_at`/`created_by`, que não existem em `retention_case_item`). A UI já mostrava mensagem segura (Bloco A), mas nenhum caso era criado.
- **Raiz corrigida em** `InovaGed.Infrastructure/Retention/RetentionRecalculateService.cs`:
  - `createCaseSql`: `id` via `gen_random_uuid()` + `title` gerado ("Recalculo operacional de temporalidade dd/mm/yyyy hh24:mi");
  - `createItemSql`: colunas reais (`decision` em vez de `status`; sem `created_at/created_by`) + **evidência da regra por item**: `doc_code`, `doc_title`, `classification_code/name`, `retention_due_at`, `retention_status`, `suggested_destination`.
- **Depois (E2E 12/12 asserts):** POST Recalculate → 302 + página 200 + msg ok (msg erro ausente); **1 caso novo** com title não nulo; itens com `decision=PENDING` e campos de evidência preenchidos; documentos amarrados ao caso; **base/prazo persistidos nos documentos classificados**; **2ª execução idempotente**: "Nenhum vencido novo", zero casos duplicados.

### C2 — Evidência de mudança de regra + bloqueios na execução de destino — **passou**

**(a) Mudança de regra preserva a regra anterior.**
- **Antes:** salvar nova TTD sobrescrevia `retention_rule_v2` sem rastro do valor anterior.
- **Depois:** migration `2026_10_01_ged_retention_c2.sql` adiciona `previous_values jsonb` + `updated_by uuid` em `retention_rule_v2`; `ClassificationPlanV2Service.SaveAsync` captura a linha atual **antes** do update e grava-a em `previous_values`.
- **E2E:** regra 1 (`10|REVISAO|0`) → regra 2 (`11|ELIMINAR`): DB confirma `previous_values` = valores antigos e valores atuais atualizados; ambas as gravações via rota real `POST /ClassificationPlan/RetentionRule/Save` (302).

**(b) Execução de destino respeita impedimentos físicos/legais, com motivo persistido e auditoria.**
- **Antes:** `ExecuteBatchAsync` marcava itens EXECUTED sem re-verificar situação física/protocolar/legal; nada explicava *por quê* um documento não foi executado; a "auditoria" de lote ia só para o log (stub).
- **Depois (em `RetentionDestinationRepository.ExecuteBatchAsync`):** re-checagem de 9 bandeiras por documento na hora da execução; se impedido, o item fica **bloqueado com motivo pt-BR + `blocked_at`** persistidos em `retention_destination_item` (`block_reason`), sem tocar `retention_status`; documentos livres → `EXECUTED`. Bandejas de bloqueio:
  | Bloqueio | Verificação | Motivo exibido |
  |---|---|---|
  | Empréstimo físico | `physical_loan` OPEN/OVERDUE ativo | "Empréstimo físico em aberto…" |
  | Movimentação de caixa | movimentação física em andamento | "Movimentação de caixa em andamento…" |
  | Protocolo pendente | item de `protocol_request_item` + request ativos/não-finalizados | "Protocolo pendente…" |
  | Protocolo em curso | existência em `ged.protocols` | "Protocolo em curso…" |
  | Impedimento legal | `hold_active` no item ∨ `retention_hold` no doc ∨ linha ativa em `retention_hold` | "Impedimento legal (hold ativo)" |
  - Lote segue `OPEN` e **re-executável**; após liberação, execução limpa os bloqueios antigos e sufixa parcial quando aplicável ("Execução parcial: 1 executado(s), 1 bloqueado(s)").
  - **Auditoria real** em `ged.document_audit` (jsonb): `BATCH_BLOCKED` **por item** com `{"batch": "<guid>", "motivo": "…"}` e `BATCH_EXECUTED` com `{"batch": "<guid>"}`.
- **E2E (fase de bloqueio, 12/12):** lote com 3 docs impedidos [empréstimo físico aberto, protocolo pendente, hold ativo]: "nenhum item executado", "3 bloqueado(s)", 3 badges `BLOQUEADO` no HTML real, os 3 motivos visíveis na tela **e** persistidos nos itens (3/3), batch segue OPEN, `BATCH_BLOCKED ×3` no banco, zero docs EXECUTED. **Fase de liberação/execução (8/8):** lotes executam — "Lote executado: 3 documento(s)", batch `EXECUTED`, 3 docs `EXECUTED`, bloqueios antigos limpos, sem sufixo parcial. **Fase parcial (sucessiva):** sufixo "executado(s), 1 bloqueado(s)" + item com "Empr…" persistido. Re-execução de lote EXECUTED = no-op ("estava executado").

**Causas raiz encontradas e corrigidas durante a verificação (5 bugs de app):**
1. **DateOnly no Dapper/Npgsql** — `SaveAsync` passava comando com `DateOnly?` direto; Npgsql não aceita `DateOnly` como parâmetro (500). Correção: cast explícito para `DateTime?` UTC-midnight com ternário + objeto de parâmetros anônimo explícito.
2. **Tipo errado do PK do item** — `retention_destination_item.id` é **bigint identity**, não uuid; tupla Dapper usava `Guid ItemId` → `InvalidCastException Int64→Guid` na desserialização. Correção: `long ItemId` na tupla (9 elementos) e na lista de bloqueados.
3. **"Auditoria" que não auditava** — `IRetentionWriter.WriteAsync` é stub somente-log (insert só habilitado junto com a tabela operacional futura); os eventos de lote nunca chegavam ao banco. Correção: eventos `BATCH_BLOCKED`/`BATCH_EXECUTED` passam a usar `WriteDocAsync` (insert real em `ged.document_audit` via `to_jsonb`).
4. **Razor com ternário embutido retornando HTML** — células Hold/Bloqueio de `Details.cshtml` montavam `<span…>` dentro de ternário em `@()`; o Razor HTML-encodou o resultado inteiro (linha mostrava a marcação literal). Correção: markup real via blocos `@if/@else` (duas células). Verificado: badges e motivos renderizam como HTML de verdade.
5. **Alias truncado** — coluna `protopending` chamada `propending` na SQL de re-checagem (42703 em produção de lote). Renomeada.

*(Falhas de sonda também corrigidas na rota, registradas para transparência: assert de auditoria buscava `batch=<guid>` enquanto o jsonb serializa `"batch": "<guid>"` → match por `<guid>`; auto-cura do fixture resetando `retention_status` de docs já EXECUTED em rodadas anteriores; armadilha SQL `NULL::int` → vazio em `select (col=null-check)::int` resolvida com `coalesce(expr,false)::int`. Em nenhuma dessas a app estava errada — o cenário foi confirmado correto pelos dados.)*

### C3 — Central de temporalidade explicativa, sem eliminação automática — **passou**

- **Antes:** telas de retention sem explicar o que significam vencer/vencido/bloqueado/em análise; risco de leitura "vencido = eliminado".
- **Depois:** tela central `/Temporalidade` (buckets `overdue/blocked/etc.`) com seção "**Como ler esta central**" explicando cada estado, exibindo **base da contagem e prazo** por item, links para as views filtradas; `Details.cshtml` de destino mostra classificação, prazos, HOLD e bloqueeios. **Nenhum caminho elimina fisicamente por mera expiração**: `retention_status` só muda pela execução aprovada de lote de destino (Bloco C2) — a execução de destino declara em tela que não elimina fisicamente sem autorização.
- **E2E (asserts C3, todos PASS):** página 200 com a seção explicativa; base da contagem e prazo visíveis; buckets filtram; link `/Temporalidade?bucket=blocked` presente; badge `Bloqueado` real.

---

## 5. Arquivos alterados + migrations (working tree, não commitado)

**Código — Bloco A:** `UploadChunkService.cs`, `ProtocolRequestService.cs`, `ProtocolAttachmentSaver.cs` (novo), `ProtocolRequestsController.cs`, `ProtocolsController.cs`, `UploadBatchController.cs`, `TemporalidadeController.cs`, `RetentionController.cs`, `RetentionCaseController.cs`, `RetentionDestinationController.cs`, `AppPolicies.cs`, `Program.cs`, `Views/ProtocolRequests/New.cshtml`, `Views/Protocols/Details.cshtml`, `Views/RetentionDestination/Details.cshtml` (nova).

**Código — Bloco B:** `PhysicalTransitContracts.cs` (novo), `PhysicalTransitService.cs` (novo), `PhysicalTransitController.cs` (novo), `Views/PhysicalTransit/*` (novas: listagem, checkout, detalhe/lote, cautela, histórico por prontuário), `Views/Shared/_ProtocolTabs.cshtml` (nova), `AppPolicies.cs` (+ `PhysicalTransitView/Manage`), `AppMenuPolicy.cs` (+ `CanSee/CanManagePhysicalTransit`), `UserShellContextService.cs`, `Views/Shared/_SidebarMenu.cshtml`, `ProtocoloCadastrosController.cs` (guarda de exclusão de setor), `Views/Protocols/{WorkQueue,Details}.cshtml`, `Views/ProtocolRequests/My.cshtml`.

**Código — Bloco C:** `RetentionRecalculateService.cs` (C1: `createCaseSql`/`createItemSql`), `ClassificationPlanV2Service.cs` (evidência de regra + cast DateOnly), `RetentionDestinationRepository.cs` (bloqueios, motivos, auditoria, alias), `RetentionAuditWriter.cs` / `IRetentionAuditWriter.cs` (uso de `WriteDocAsync`), `DestinationModels.cs`, `RetentionQueueFilter.cs`, `RetentionQueueRepository.cs`, `Views/Temporalidade/Index.cshtml` (central explicativa), `Views/RetentionDestination/Details.cshtml` (markup de bloqueio).

**Banco (todas aditivas/idempotentes, 2× aplicadas no repro):**
| Migration | Conteúdo |
|---|---|
| `2026_09_29_ged_upload_chunk_compatibility.sql` (nova) | Colunas `batch_item_id`/`metadata_json` em `upload_session` + índice parcial; `20260603_upload_chunk` anexado ao manifest |
| `2026_09_30_ged_physical_transit.sql` (nova) | `physical_loan_batch` (cabeçalho do lote) + extensões em `physical_loan` (`batch_id/document_id/sector_*/carrier_*/lost_reason`); índice único parcial RN01; seed idempotente dos 5 setores DAME |
| `2026_10_01_ged_retention_c2.sql` (nova) | `previous_values jsonb` + `updated_by` em `retention_rule_v2`; `block_reason text` + `blocked_at timestamptz` em `retention_destination_item` |
| `migrations.manifest.json` | + 3 entradas acima, na ordem correta |

---

## 6. Matriz de perfis (B6 + políticas)

Políticas criadas: `RetentionManage` (Bloco A: Admin/AdministradorOphir/ArquivistaOphir + FullAdmin), `PhysicalTransitView` (Admin/Administrador/AdministradorOphir/**ArquivistaOphir**) e `PhysicalTransitManage` (Admin/Administrador/AdministradorOphir). GETs seguem políticas existentes.

| Perfil (seed) | Menu Trânsito | View `/PhysicalTransit` | Checkout (POST) | POSTs Retention (Bloco A) |
|---|---|---|---|---|
| admin | ✓ | 200 | 200 (aceito) | aceitos |
| administrador (admstr) | ✓ | 200 | 200 (aceito) | aceitos |
| administrador ophir (admoph) | ✓ | 200 | 200 (aceito) | aceitos |
| arquivista ophir (arqv) | ✓ | 200 | **302 (negado/validate)** | aceitos (`RetentionManage`) |
| hospital — somente leitura (hosp) | ✗ | **302 (negado)** | **302 (negado)** | **302 → AccessDenied** (Bloco A) |

Verificação E2E desta matriz executada na rodada final (valores exatamente os esperados, 5/5 perfis). Negativas auditadas em `ged.security_access_failure_log`.

---

## 7. Desempenho (alegação só com medida)

- **RNF01 — lote de até 100 prontuários em ≤ 2 s:** checkout de **100 itens** medido no server (wall-time do POST + commit):
  - medição original do Bloco B: **666 ms**
  - re-medida na regressão da rodada final: **532 ms**
  - alvo ≤ 2.000 ms → **atendido, margem ~3,3×** (ambiente dev; banco `inovaged_repro`; carga única por medição publicada).
- Demais escritas do Bloco C são por lote pequeno (≤ dezenas de docs) com re-checagem em uma consulta de bandeiras por lote; a execução de lote na E2E responde em dezenas de ms (telemetria de request: `Create` 86–153 ms incl. auditoria). Sem regressão observável no scan de telemetria (nenhum endpoint do escopo acima de ~300 ms nas rodadas finais).

---

## 8. Evidência de testes (rodada final, mesma sessão de logs)

| Sonda | Resultado |
|---|---|
| `probe_blockC_verify.ps1` (C1+C2+C3, E2E via HTTP + psql) | **SUMMARY PASS=66 / FAIL=0** |
| `probe_blockB_verify.ps1` (regressão pós-Bloco C) | verde: matriz B6 5/5; checkout lote-3; RN01 sequencial + corrida (exatamente 1 OPEN); devolução parcial OPEN\|1→OPEN\|2→CLOSED\|3; LOST/extravio; OVERDUE derivado + histórico; guarda de exclusão de setor; PERF 532 ms |
| `tabcheck.ps1` (abas B7) | 5/5 páginas, abas presentes, `active-href` correto |
| Scan de log (`webapp3.log`, 340 linhas, run limpo único) | sem `UnhandledException`, sem 500, sem `Falha`. Presentes apenas: (i) 2× `warn` `PcdVersionResolver` 42703 — **fallback por design** (schema parcial de `classification_plan_version` sem coluna `status`; resolver captura o `42703` e cai no SQL simples — comportamento pré-existente, sem impacto); (ii) `crit` de startup "Senha padrão PostgreSQL" (alerta ambiental pré-existente do dev); (iii) "Failed to determine the https port" (HTTP-only, inofensivo) |
| Idempotência de migration | `2026_10_01_ged_retention_c2.sql` e demais aplicadas 2× no repro, sem erro nem efeito duplo (seed por `not exists`) |

Tabela de status global (requisitos do escopo): **RF02–RF06, RN01, RN03, RN05, RNF01, RNF02 = passou** (RN02 coberto como padrão ubíquo + reforço no delete de setor; RNF02 parcial→fechado no escopo: `loaned_by/created_by/closed_by` uuid em todas as escritas do Bloco B, `updated_by` no C2, `performed_by` nos eventos — `protocol_request_history.user_name` legado segue como pendência conhecida, §9.5). **RF01 = excluído por instrução.** Nenhum requisito ficou **falhou** ou **bloqueado**.

---

## 9. Riscos / pendências (carregados para o próximo ciclo)

1. **Regra antiga do dump vs code** — `classification_plan_version` no banco dev está sem a coluna `status`; o `PcdVersionResolver` trata isso com fallback (warn em log a cada uso). Considerar migration futura adicionando a coluna (ou migrar o banco dev para o schema completo) para eliminar o warn.
2. **FileErrs (falha por arquivo em anexos)** — caminho coberto por código (try/catch + compensação) mas sem gatilho determinístico em E2E (exigiria storage indisponível). Testar com storage caindo no próximo ciclo.
3. **Rota legada** `/Protocols/Details/{id}` responde 405 (rótulo `{id:guid}` pré-existente); redirect atual aponta para a rota válida — manter vigilância se links antigos forem referenciados.
4. **Auditoria acumulativa** — `ged.document_audit` agora recebe eventos de lote por item (volume pequeno: 1 linha/item bloqueado + 1 lote executado); ainda não há política de retenção/arquivamento da própria tabela de auditoria.
5. **`protocol_request_history.user_name` (texto)** — legado administrativo fora do escopo; id de usuário só existe nos caminhos novos (blocos A/B/C). Unificar em migração futura.
6. **Environment dev** — alerta crítico de startup de senha padrão PostgreSQL (dev only, já sinalizado pelo próprio startup).
7. **Commit** — toda a entrega (código + migrations + docs 01–05) segue **não commitada** na working tree de `main` @ `c03d383`; nada publicado/mergeado/alterado em produção, conforme restrição.
8. **Fixtures de sonda** — linhas de `document_audit` das sondas persistem (inocentes: asserts filtram por GUID de lote); loans de probe são encerrados para `reg_status='R'` na limpeza.
